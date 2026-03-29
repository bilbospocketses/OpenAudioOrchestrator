using Microsoft.AspNetCore.Identity;

namespace FishAudioOrchestrator.Web.Data.Entities;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public bool MustChangePassword { get; set; }
    public bool MustSetupTotp { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
