using FishAudioOrchestrator.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FishAudioOrchestrator.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ModelProfile> ModelProfiles => Set<ModelProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModelProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CheckpointPath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ImageTag).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);
        });
    }
}
