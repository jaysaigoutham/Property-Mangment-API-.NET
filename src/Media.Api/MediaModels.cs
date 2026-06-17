using BuildingBlocks.Events;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Media.Api;

public sealed class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string Bucket { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MinioStorageOptions
{
    public const string SectionName = "Minio";

    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public string Bucket { get; set; } = "property-images";
    public string PublicBaseUrl { get; set; } = "http://localhost:9000";
    public bool UseSsl { get; set; }
    public int UploadUrlExpirySeconds { get; set; } = 900;
}

public sealed record UploadUrlRequest(Guid ListingId, string FileName, string ContentType, int SortOrder);
public sealed record UploadUrlResponse(Guid MediaId, string Bucket, string ObjectName, string UploadUrl, string PublicUrl, DateTimeOffset ExpiresAtUtc);
public sealed record MediaAssetResponse(Guid Id, Guid ListingId, string PublicUrl, string ContentType, int SortOrder, DateTimeOffset CreatedAtUtc);
public sealed record ReorderMediaRequest(int SortOrder);

public sealed class MediaDbContext(DbContextOptions<MediaDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("media");

        modelBuilder.Entity<MediaAsset>(builder =>
        {
            builder.ToTable("media_assets");
            builder.HasKey(asset => asset.Id);
            builder.Property(asset => asset.Bucket).HasMaxLength(120).IsRequired();
            builder.Property(asset => asset.ObjectName).HasMaxLength(500).IsRequired();
            builder.Property(asset => asset.PublicUrl).HasMaxLength(1000).IsRequired();
            builder.Property(asset => asset.ContentType).HasMaxLength(120).IsRequired();
            builder.HasIndex(asset => new { asset.ListingId, asset.SortOrder });
        });

        modelBuilder.Entity<OutboxMessage>().ConfigureOutbox();
    }
}

public static class MediaMapping
{
    public static MediaAssetResponse ToResponse(this MediaAsset asset) =>
        new(asset.Id, asset.ListingId, asset.PublicUrl, asset.ContentType, asset.SortOrder, asset.CreatedAtUtc);
}
