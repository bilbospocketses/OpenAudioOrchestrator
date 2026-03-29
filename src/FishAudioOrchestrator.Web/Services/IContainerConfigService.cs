using Docker.DotNet.Models;
using FishAudioOrchestrator.Web.Data.Entities;

namespace FishAudioOrchestrator.Web.Services;

public interface IContainerConfigService
{
    CreateContainerParameters BuildCreateParams(ModelProfile profile);
    Task<int> AllocatePortAsync();
}
