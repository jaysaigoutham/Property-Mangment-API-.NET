using BuildingBlocks.Events;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Engagement.Api;

public enum InquiryStatus
{
    Open = 0,
    Responded = 1,
    Closed = 2
}

public enum ReviewStatus
{
    Pending = 0,
    Published = 1,
    Rejected = 2
}

public sealed class Favorite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ListingId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SavedSearch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string QueryJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Inquiry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid BuyerId { get; set; }
    public string Message { get; set; } = string.Empty;
    public InquiryStatus Status { get; set; } = InquiryStatus.Open;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListingId { get; set; }
    public Guid AgentId { get; set; }
    public Guid ReviewerId { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgentProfile
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string AgencyName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record FavoriteRequest(Guid ListingId);
public sealed record SavedSearchRequest(string Name, Dictionary<string, string?> Filters);
public sealed record InquiryRequest(Guid ListingId, string Message);
public sealed record ReviewRequest(Guid ListingId, Guid AgentId, int Rating, string Comment);
public sealed record AgentProfileRequest(string DisplayName, string AgencyName, string Phone, string LicenseNumber, string Bio);

public sealed record FavoriteResponse(Guid Id, Guid ListingId, DateTimeOffset CreatedAtUtc);
public sealed record SavedSearchResponse(Guid Id, string Name, string QueryJson, DateTimeOffset CreatedAtUtc);
public sealed record InquiryResponse(Guid Id, Guid ListingId, Guid BuyerId, string Message, InquiryStatus Status, DateTimeOffset CreatedAtUtc);
public sealed record ReviewResponse(Guid Id, Guid ListingId, Guid AgentId, Guid ReviewerId, int Rating, string Comment, ReviewStatus Status, DateTimeOffset CreatedAtUtc);
public sealed record AgentProfileResponse(Guid UserId, string DisplayName, string AgencyName, string Phone, string LicenseNumber, string Bio, DateTimeOffset UpdatedAtUtc);

public sealed record FavoriteCreatedEvent(Guid FavoriteId, Guid UserId, Guid ListingId);
public sealed record InquiryCreatedEvent(Guid InquiryId, Guid BuyerId, Guid ListingId, string Message);
public sealed record ReviewCreatedEvent(Guid ReviewId, Guid ReviewerId, Guid AgentId, Guid ListingId, int Rating);
public sealed record NotificationRequestedEvent(string Channel, Guid RecipientUserId, string Subject, string Body);

public sealed class EngagementDbContext(DbContextOptions<EngagementDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("engagement");

        modelBuilder.Entity<Favorite>(builder =>
        {
            builder.ToTable("favorites");
            builder.HasKey(favorite => favorite.Id);
            builder.HasIndex(favorite => new { favorite.UserId, favorite.ListingId }).IsUnique();
        });

        modelBuilder.Entity<SavedSearch>(builder =>
        {
            builder.ToTable("saved_searches");
            builder.HasKey(search => search.Id);
            builder.Property(search => search.Name).HasMaxLength(120).IsRequired();
            builder.Property(search => search.QueryJson).HasColumnType("jsonb").IsRequired();
            builder.HasIndex(search => search.UserId);
        });

        modelBuilder.Entity<Inquiry>(builder =>
        {
            builder.ToTable("inquiries");
            builder.HasKey(inquiry => inquiry.Id);
            builder.Property(inquiry => inquiry.Message).HasMaxLength(2000).IsRequired();
            builder.HasIndex(inquiry => new { inquiry.ListingId, inquiry.Status });
            builder.HasIndex(inquiry => inquiry.BuyerId);
        });

        modelBuilder.Entity<Review>(builder =>
        {
            builder.ToTable("reviews");
            builder.HasKey(review => review.Id);
            builder.Property(review => review.Comment).HasMaxLength(2000).IsRequired();
            builder.HasIndex(review => new { review.AgentId, review.Status });
            builder.HasIndex(review => review.ReviewerId);
        });

        modelBuilder.Entity<AgentProfile>(builder =>
        {
            builder.ToTable("agent_profiles");
            builder.HasKey(profile => profile.UserId);
            builder.Property(profile => profile.DisplayName).HasMaxLength(160).IsRequired();
            builder.Property(profile => profile.AgencyName).HasMaxLength(180);
            builder.Property(profile => profile.Phone).HasMaxLength(60);
            builder.Property(profile => profile.LicenseNumber).HasMaxLength(120);
            builder.Property(profile => profile.Bio).HasMaxLength(2000);
        });

        modelBuilder.Entity<OutboxMessage>().ConfigureOutbox();
    }
}

public static class EngagementMapping
{
    public static FavoriteResponse ToResponse(this Favorite favorite) =>
        new(favorite.Id, favorite.ListingId, favorite.CreatedAtUtc);

    public static SavedSearchResponse ToResponse(this SavedSearch search) =>
        new(search.Id, search.Name, search.QueryJson, search.CreatedAtUtc);

    public static InquiryResponse ToResponse(this Inquiry inquiry) =>
        new(inquiry.Id, inquiry.ListingId, inquiry.BuyerId, inquiry.Message, inquiry.Status, inquiry.CreatedAtUtc);

    public static ReviewResponse ToResponse(this Review review) =>
        new(review.Id, review.ListingId, review.AgentId, review.ReviewerId, review.Rating, review.Comment, review.Status, review.CreatedAtUtc);

    public static AgentProfileResponse ToResponse(this AgentProfile profile) =>
        new(profile.UserId, profile.DisplayName, profile.AgencyName, profile.Phone, profile.LicenseNumber, profile.Bio, profile.UpdatedAtUtc);
}
