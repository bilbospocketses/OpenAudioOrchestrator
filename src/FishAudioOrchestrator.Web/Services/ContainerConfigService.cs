using Docker.DotNet.Models;
using FishAudioOrchestrator.Web.Data;
using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FishAudioOrchestrator.Web.Services;

public class ContainerConfigService : IContainerConfigService
{
    private readonly IConfiguration _config;
    private readonly AppDbContext _context;

    public ContainerConfigService(IConfiguration config, AppDbContext context)
    {
        _config = config;
        _context = context;
    }

    public CreateContainerParameters BuildCreateParams(ModelProfile profile)
    {
        var dataRoot = _config["FishOrchestrator:DataRoot"]!;

        var env = new List<string>
        {
            "COMPILE=0"
        };

        if (profile.EnableHalf)
        {
            env.Add(@"FISH_API_SERVER_ARGS=[""--half""]");
        }

        if (!string.IsNullOrWhiteSpace(profile.CudaAllocConf))
        {
            env.Add($"PYTORCH_CUDA_ALLOC_CONF={profile.CudaAllocConf}");
        }

        return new CreateContainerParameters
        {
            Image = profile.ImageTag,
            Name = $"fish-orch-{profile.Name}",
            Env = env,
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                ["8080/tcp"] = default
            },
            HostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["8080/tcp"] = new List<PortBinding>
                    {
                        new PortBinding { HostIP = "0.0.0.0", HostPort = profile.HostPort.ToString() }
                    }
                },
                Binds = new List<string>
                {
                    $@"{profile.CheckpointPath}:/app/checkpoints",
                    $@"{Path.Combine(dataRoot, "References")}:/app/references",
                    $@"{Path.Combine(dataRoot, "Output")}:/app/output"
                },
                DeviceRequests = new List<DeviceRequest>
                {
                    new DeviceRequest
                    {
                        Count = -1,
                        Driver = "nvidia",
                        Capabilities = new List<IList<string>>
                        {
                            new List<string> { "gpu" }
                        }
                    }
                }
            }
        };
    }

    public async Task<int> AllocatePortAsync()
    {
        var start = int.Parse(_config["FishOrchestrator:PortRange:Start"]!);
        var end = int.Parse(_config["FishOrchestrator:PortRange:End"]!);

        var usedPorts = await _context.ModelProfiles
            .Select(m => m.HostPort)
            .ToListAsync();

        for (int port = start; port <= end; port++)
        {
            if (!usedPorts.Contains(port))
                return port;
        }

        throw new InvalidOperationException($"No available ports in range {start}-{end}");
    }
}
