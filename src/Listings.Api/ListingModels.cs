using BuildingBlocks.Events;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Listings.Api;

public enum ListingStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3
}

public enum PropertyType
{
    House = 0,
    Apartment = 1,
    Condo = 2,
    Land = 3,
    Commercial = 4
}

public sealed class Listing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string AddressLine { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Bedrooms { get; set; }
    public decimal Bathrooms { get; set; }
    public int AreaSqm { get; set; }
    public PropertyType PropertyType { get; set; }
    public ListingStatus Status { get; set; } = ListingStatus.Draft;
    public string AmenitiesCsv { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record CreateListingRequest(
    string Title,
    string Description,
    string City,
    string State,
    string Country,
    string AddressLine,
    decimal Price,
    int Bedrooms,
    decimal Bathrooms,
    int AreaSqm,
    PropertyType PropertyType,
    string[] Amenities);

public sealed record UpdateListingRequest(
    string Title,
    string Description,
    string City,
    string State,
    string Country,
    string AddressLine,
    decimal Price,
    int Bedrooms,
    decimal Bathrooms,
    int AreaSqm,
    PropertyType PropertyType,
    string[] Amenities);

public sealed record ListingSearchQuery(
    string? Q,
    string? City,
    string? Country,
    PropertyType? PropertyType,
    decimal? MinPrice,
    decimal? MaxPrice,
    int? Bedrooms,
    int Page = 1,
    int PageSize = 20);

public sealed record ListingResponse(
    Guid Id,
    Guid OwnerId,
    string Title,
    string Description,
    string City,
    string State,
    string Country,
    decimal Price,
    int Bedrooms,
    decimal Bathrooms,
    int AreaSqm,
    PropertyType PropertyType,
    ListingStatus Status,
    string[] Amenities,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ListingChangedEvent(Guid ListingId, Guid OwnerId, ListingStatus Status, string Title, string City, decimal Price);

public sealed class ListingsDbContext(DbContextOptions<ListingsDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("listings");

        modelBuilder.Entity<Listing>(builder =>
        {
            builder.ToTable("listings");
            builder.HasKey(listing => listing.Id);
            builder.Property(listing => listing.Title).HasMaxLength(220).IsRequired();
            builder.Property(listing => listing.Description).HasMaxLength(4000).IsRequired();
            builder.Property(listing => listing.City).HasMaxLength(120).IsRequired();
            builder.Property(listing => listing.State).HasMaxLength(120);
            builder.Property(listing => listing.Country).HasMaxLength(120).IsRequired();
            builder.Property(listing => listing.AddressLine).HasMaxLength(300).IsRequired();
            builder.Property(listing => listing.Price).HasPrecision(18, 2);
            builder.Property(listing => listing.Bathrooms).HasPrecision(4, 1);
            builder.Property(listing => listing.AmenitiesCsv).HasMaxLength(2000);
            builder.HasIndex(listing => new { listing.Status, listing.City, listing.Price });
            builder.HasIndex(listing => new { listing.OwnerId, listing.Status });
        });

        modelBuilder.Entity<OutboxMessage>().ConfigureOutbox();
    }
}

public static class ListingMapping
{
    public static ListingResponse ToResponse(this Listing listing) =>
        new(
            listing.Id,
            listing.OwnerId,
            listing.Title,
            listing.Description,
            listing.City,
            listing.State,
            listing.Country,
            listing.Price,
            listing.Bedrooms,
            listing.Bathrooms,
            listing.AreaSqm,
            listing.PropertyType,
            listing.Status,
            SplitAmenities(listing.AmenitiesCsv),
            listing.CreatedAtUtc,
            listing.UpdatedAtUtc);

    public static string JoinAmenities(IEnumerable<string> amenities) =>
        string.Join(",", amenities.Select(amenity => amenity.Trim()).Where(amenity => amenity.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

    public static string[] SplitAmenities(string amenitiesCsv) =>
        amenitiesCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public static class ListingValidation
{
    public static string? Validate(CreateListingRequest request) =>
        ValidateCore(request.Title, request.Description, request.City, request.Country, request.Price, request.Bedrooms, request.Bathrooms, request.AreaSqm);

    public static string? Validate(UpdateListingRequest request) =>
        ValidateCore(request.Title, request.Description, request.City, request.Country, request.Price, request.Bedrooms, request.Bathrooms, request.AreaSqm);

    private static string? ValidateCore(string title, string description, string city, string country, decimal price, int bedrooms, decimal bathrooms, int areaSqm)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 220)
        {
            return "Title is required and must be 220 characters or fewer.";
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return "Description is required.";
        }

        if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(country))
        {
            return "City and country are required.";
        }

        if (price <= 0 || bedrooms < 0 || bathrooms < 0 || areaSqm <= 0)
        {
            return "Price, bedroom, bathroom, and area values must be valid positive numbers.";
        }

        return null;
    }
}
