namespace FishAudioOrchestrator.Web.Services;

public interface IAdminSeedService
{
    Task SeedIfConfiguredAsync();
}
