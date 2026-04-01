using FishAudioOrchestrator.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FishAudioOrchestrator.Tests.Services;

public class SetupServiceTests
{
    private static SetupService CreateService()
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());
        return new SetupService(envMock.Object, NullLogger<SetupService>.Instance);
    }

    // --- IsValidDomain ---

    [Theory]
    [InlineData("example.com")]
    [InlineData("sub.domain.co.uk")]
    [InlineData("a-b.com")]
    public void IsValidDomain_ReturnsTrueForValidDomains(string domain)
    {
        Assert.True(SetupService.IsValidDomain(domain));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("-bad.com")]
    [InlineData("no_underscores.com")]
    [InlineData(".leading-dot.com")]
    [InlineData("trailing-dot.")]
    [InlineData("spaces in domain")]
    [InlineData("localhost")]
    public void IsValidDomain_ReturnsFalseForInvalidDomains(string? domain)
    {
        Assert.False(SetupService.IsValidDomain(domain!));
    }

    // --- IsValidEmail ---

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("a@b.co")]
    public void IsValidEmail_ReturnsTrueForValidEmails(string email)
    {
        Assert.True(SetupService.IsValidEmail(email));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("no-at-sign")]
    [InlineData("@no-local")]
    [InlineData("no-domain@")]
    [InlineData("spaces @in.email")]
    public void IsValidEmail_ReturnsFalseForInvalidEmails(string? email)
    {
        Assert.False(SetupService.IsValidEmail(email!));
    }

    // --- IsValidPort ---

    [Theory]
    [InlineData(1024)]
    [InlineData(8080)]
    [InlineData(65535)]
    public void IsValidPort_ReturnsTrueForValidPorts(int port)
    {
        Assert.True(SetupService.IsValidPort(port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void IsValidPort_ReturnsFalseForInvalidPorts(int port)
    {
        Assert.False(SetupService.IsValidPort(port));
    }

    // --- IsModelPresent ---

    [Fact]
    public void IsModelPresent_ReturnsTrueWhenModelExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"setup-test-{Guid.NewGuid()}");
        var s2ProDir = Path.Combine(tempDir, "s2-pro");
        try
        {
            Directory.CreateDirectory(s2ProDir);
            File.WriteAllText(Path.Combine(s2ProDir, "model.bin"), "fake");

            var service = CreateService();
            Assert.True(service.IsModelPresent(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsModelPresent_ReturnsFalseWhenDirectoryMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"setup-test-{Guid.NewGuid()}");
        var service = CreateService();

        Assert.False(service.IsModelPresent(tempDir));
    }

    [Fact]
    public void IsModelPresent_ReturnsFalseWhenDirectoryEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"setup-test-{Guid.NewGuid()}");
        var s2ProDir = Path.Combine(tempDir, "s2-pro");
        try
        {
            Directory.CreateDirectory(s2ProDir);

            var service = CreateService();
            Assert.False(service.IsModelPresent(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // --- Default state ---

    [Fact]
    public void HasActiveDownloads_ReturnsFalse_WhenNoDownloadsStarted()
    {
        var service = CreateService();
        Assert.False(service.HasActiveDownloads);
    }

    [Fact]
    public void ModelDownloadCompleted_DefaultsFalse()
    {
        var service = CreateService();
        Assert.False(service.ModelDownloadCompleted);
    }

    [Fact]
    public void DockerPullCompleted_DefaultsFalse()
    {
        var service = CreateService();
        Assert.False(service.DockerPullCompleted);
    }
}
