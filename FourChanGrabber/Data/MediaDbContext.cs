using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FourChanGrabber.Data;

public class MediaDbContext : DbContext
{
    public MediaDbContext(DbContextOptions<MediaDbContext> options) : base(options)
    {
    }

    public DbSet<ImageSource> ImageSources => Set<ImageSource>();
    public DbSet<DownloadQueue> DownloadQueue => Set<DownloadQueue>();
    public DbSet<MediaData> MediaData => Set<MediaData>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ImageSource
        modelBuilder.Entity<ImageSource>(entity =>
        {
            entity.ToTable("ImageSource");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        // DownloadQueue
        modelBuilder.Entity<DownloadQueue>(entity =>
        {
            entity.ToTable("DownloadQueue");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TargetPath).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Status).IsRequired().HasDefaultValue(DownloadStatus.Pending);
            entity.Property(e => e.ErrorMessage).HasMaxLength(4000);

            entity.HasIndex(e => e.Status).HasDatabaseName("IX_DownloadQueue_Status");
        });

        // MediaData
        modelBuilder.Entity<MediaData>(entity =>
        {
            entity.ToTable("MediaData");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SavePath).IsRequired().HasMaxLength(1000);

            entity.HasIndex(e => e.ImageSourceId).HasDatabaseName("IX_MediaData_ImageSourceId");
        });
    }
}
