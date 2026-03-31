using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FishAudioOrchestrator.Web.Services;

public class SetupService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SetupService> _logger;
    private Process? _modelDownloadProcess;
    private Process? _dockerPullProcess;
    private readonly List<string> _modelDownloadOutput = new();
    private readonly List<string> _dockerPullOutput = new();
    private readonly object _lock = new();

    public bool IsModelDownloading => _modelDownloadProcess is not null && !_modelDownloadProcess.HasExited;
    public bool IsDockerPulling => _dockerPullProcess is not null && !_dockerPullProcess.HasExited;
    public bool ModelDownloadCompleted { get; private set; }
    public bool DockerPullCompleted { get; private set; }
    public string? ModelDownloadError { get; private set; }
    public string? DockerPullError { get; private set; }

    public SetupService(IWebHostEnvironment env, ILogger<SetupService> logger)
    {
        _env = env;
        _logger = logger;
    }

    // --- Pre-checks ---

    public async Task<(bool available, string? error)> CheckGitAsync()
    {
        try
        {
            var (exitCode, output) = await RunCommandAsync("git", "--version");
            if (exitCode != 0)
                return (false, "Git is not installed. Install it from PowerShell:\nwinget install Git.Git\nThen click Retry.");
            return (true, null);
        }
        catch
        {
            return (false, "Git is not installed or not in PATH. Install it from PowerShell:\nwinget install Git.Git\nThen click Retry.");
        }
    }

    public async Task<(bool available, string? error)> CheckGitLfsAsync()
    {
        try
        {
            var (exitCode, output) = await RunCommandAsync("git", "lfs version");
            if (exitCode != 0)
                return (false, "Git LFS is not installed. Run the following in PowerShell:\ngit lfs install\nThen click Retry.");
            return (true, null);
        }
        catch
        {
            return (false, "Git LFS is not installed. Run the following in PowerShell:\ngit lfs install\nThen click Retry.");
        }
    }

    public async Task<(bool available, string? error)> CheckDockerAsync()
    {
        try
        {
            var (exitCode, output) = await RunCommandAsync("docker", "version --format '{{.Server.Version}}'");
            if (exitCode != 0)
                return (false, "Docker is not responding. Ensure Docker Desktop is installed and running.");
            return (true, null);
        }
        catch
        {
            return (false, "Docker is not installed or not in PATH. Install Docker Desktop from docker.com and ensure it is running.");
        }
    }

    public async Task<bool> IsDockerImagePresentAsync(string imageTag)
    {
        try
        {
            var (exitCode, output) = await RunCommandAsync("docker", $"images -q {imageTag}");
            return exitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    public bool IsModelPresent(string checkpointsDir)
    {
        var s2ProPath = Path.Combine(checkpointsDir, "s2-pro");
        return Directory.Exists(s2ProPath) && Directory.GetFiles(s2ProPath).Length > 0;
    }

    // --- Background downloads ---

    public void StartModelDownload(string checkpointsDir, Action? onOutput = null)
    {
        if (IsModelDownloading) return;

        ModelDownloadCompleted = false;
        ModelDownloadError = null;
        lock (_lock) { _modelDownloadOutput.Clear(); }

        var targetPath = Path.Combine(checkpointsDir, "s2-pro");

        _modelDownloadProcess = StartBackgroundProcess(
            "git", $"clone https://huggingface.co/fishaudio/s2-pro \"{targetPath}\"",
            _modelDownloadOutput,
            onOutput,
            exitCode =>
            {
                if (exitCode == 0)
                    ModelDownloadCompleted = true;
                else
                    ModelDownloadError = "Model download failed. Check the output for details.";
            });
    }

    public void StartDockerPull(string imageTag, Action? onOutput = null)
    {
        if (IsDockerPulling) return;

        DockerPullCompleted = false;
        DockerPullError = null;
        lock (_lock) { _dockerPullOutput.Clear(); }

        _dockerPullProcess = StartBackgroundProcess(
            "docker", $"pull {imageTag}",
            _dockerPullOutput,
            onOutput,
            exitCode =>
            {
                if (exitCode == 0)
                    DockerPullCompleted = true;
                else
                    DockerPullError = "Docker image pull failed. Check the output for details.";
            });
    }

    public List<string> GetModelDownloadOutput()
    {
        lock (_lock) { return _modelDownloadOutput.ToList(); }
    }

    public List<string> GetDockerPullOutput()
    {
        lock (_lock) { return _dockerPullOutput.ToList(); }
    }

    public bool HasActiveDownloads => IsModelDownloading || IsDockerPulling;

    // --- Settings writer ---

    public async Task SaveSettingsAsync(
        string checkpointsDir,
        string referencesDir,
        string outputDir,
        int portStart,
        int portEnd,
        string? domain,
        string? email)
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

    // --- Validation helpers ---

    public static bool IsValidDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        return Regex.IsMatch(domain, @"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)+$");
    }

    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }

    public static bool IsValidPort(int port)
    {
        return port >= 1024 && port <= 65535;
    }

    // --- Internals ---

    private Process StartBackgroundProcess(
        string fileName, string arguments,
        List<string> outputBuffer,
        Action? onOutput,
        Action<int>? onExit)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_lock) { outputBuffer.Add(e.Data); }
            onOutput?.Invoke();
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_lock) { outputBuffer.Add(e.Data); }
            onOutput?.Invoke();
        };

        process.Exited += (_, _) =>
        {
            onExit?.Invoke(process.ExitCode);
            onOutput?.Invoke();
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static async Task<(int exitCode, string output)> RunCommandAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output.Trim());
    }
}
