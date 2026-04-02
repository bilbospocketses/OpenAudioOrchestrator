using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;

namespace FishAudioOrchestrator.Web.Services;

/// <summary>
/// Handles writing application settings and database encryption during setup.
/// </summary>
public class SetupSettingsService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SetupSettingsService> _logger;
    private readonly IDataProtectionProvider _dataProtection;

    public SetupSettingsService(
        IWebHostEnvironment env,
        ILogger<SetupSettingsService> logger,
        IDataProtectionProvider dataProtection)
    {
        _env = env;
        _logger = logger;
        _dataProtection = dataProtection;
    }

    public static string GenerateDatabaseKey()
    {
        return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    }

    public async Task SaveSettingsAsync(
        string checkpointsDir,
        string referencesDir,
        string outputDir,
        int portStart,
        int portEnd,
        string? domain,
        string? email,
        string? databaseKey = null)
    {
        var settingsPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        var json = await File.ReadAllTextAsync(settingsPath);
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;

        // Derive DataRoot as the common parent of the three directories
        var dataRoot = Path.GetDirectoryName(checkpointsDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                       ?? checkpointsDir;

        // ConnectionStrings
        root["ConnectionStrings"]!["Default"] = $"Data Source={Path.Combine(dataRoot, "fishorch.db")}";

        // FishOrchestrator
        var fish = root["FishOrchestrator"]!;
        fish["DataRoot"] = dataRoot;
        fish["PortRange"]!["Start"] = portStart;
        fish["PortRange"]!["End"] = portEnd;
        fish["Domain"] = domain ?? "";
        if (databaseKey is not null)
        {
            var protector = _dataProtection.CreateProtector("DatabaseKey");
            fish["DatabaseKey"] = protector.Protect(databaseKey);
        }

        // LettuceEncrypt
        var le = root["LettuceEncrypt"]!;
        if (!string.IsNullOrWhiteSpace(domain))
        {
            le["DomainNames"] = new JsonArray(domain);
            le["EmailAddress"] = email ?? "";
        }
        else
        {
            le["DomainNames"] = new JsonArray();
            le["EmailAddress"] = "";
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        var output = root.ToJsonString(options);
        await File.WriteAllTextAsync(settingsPath, output);

        _logger.LogInformation("Settings saved to {Path}", settingsPath);
    }

    /// <summary>
    /// Encrypts an existing unencrypted SQLite database using SQLCipher.
    /// Creates an encrypted copy, then replaces the original.
    /// </summary>
    public async Task EncryptDatabaseAsync(string dbPath, string key)
    {
        if (!File.Exists(dbPath))
        {
            _logger.LogWarning("Database file not found at {Path}, skipping encryption", dbPath);
            return;
        }

        var tempPath = dbPath + ".encrypting";
        try
        {
            // Open the existing unencrypted database and export to an encrypted copy
            // using SQLCipher's ATTACH + sqlcipher_export approach.
            // The backup API does not support encrypted destinations.
            using var source = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await source.OpenAsync();

            // Attach an encrypted database and copy schema + data into it
            using (var cmd = source.CreateCommand())
            {
                cmd.CommandText = $"ATTACH DATABASE '{tempPath.Replace("'", "''")}' AS encrypted KEY '{key.Replace("'", "''")}';";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var cmd = source.CreateCommand())
            {
                cmd.CommandText = "SELECT sqlcipher_export('encrypted');";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var cmd = source.CreateCommand())
            {
                cmd.CommandText = "DETACH DATABASE encrypted;";
                await cmd.ExecuteNonQueryAsync();
            }

            source.Close();

            // Replace the original with the encrypted version
            var backupPath = dbPath + ".unencrypted.bak";
            File.Move(dbPath, backupPath, overwrite: true);
            File.Move(tempPath, dbPath);

            // Remove WAL/SHM journal files from the unencrypted DB
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);

            // Delete the unencrypted backup
            File.Delete(backupPath);

            _logger.LogInformation("Database encrypted successfully at {Path}", dbPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt database at {Path}", dbPath);

            // Clean up temp file on failure
            if (File.Exists(tempPath))
                try { File.Delete(tempPath); } catch { }

            throw;
        }
    }
}
