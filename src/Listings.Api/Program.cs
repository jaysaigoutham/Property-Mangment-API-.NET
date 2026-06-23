using System.Security.Claims;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Auth;
using BuildingBlocks.Caching;
using BuildingBlocks.Events;
using BuildingBlocks.ServiceDefaults;
using Listings.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddMarketplaceServiceDefaults("listings-api")
    .AddPostgresDb<ListingsDbContext>("listings")
    .AddKafkaOutbox<ListingsDbContext>("listings-api");

builder.Services.AddHttpClient("payments", (sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(configuration["Services:Payments"] ?? "http://payments-api:8080");
});

var app = builder.Build();

app.UseMarketplaceServiceDefaults();

var group = app.MapGroup("/listings").WithTags("Listings");

group.MapGet("/", async (
    string? q,
    string? city,
    string? country,
    PropertyType? propertyType,
    decimal? minPrice,
    decimal? maxPrice,
    int? bedrooms,
    int page,
    int pageSize,
    ListingsDbContext db,
    IDistributedCache cache,
    CancellationToken cancellationToken) =>
{
    page = page <= 0 ? 1 : page;
    pageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
    var cacheKey = $"{CacheKeys.ListingSearchPrefix}{JsonSerializer.Serialize(new { q, city, country, propertyType, minPrice, maxPrice, bedrooms, page, pageSize })}";
    var cached = await cache.GetStringAsync(cacheKey, cancellationToken);

    if (cached is not null)
    {
        return Results.Ok(JsonSerializer.Deserialize<List<ListingResponse>>(cached));
    }

    var query = db.Listings.AsNoTracking().Where(listing => listing.Status == ListingStatus.Approved);

    if (!string.IsNullOrWhiteSpace(city))
    {
        query = query.Where(listing => EF.Functions.ILike(listing.City, $"%{city.Trim()}%"));
    }

    if (!string.IsNullOrWhiteSpace(country))
    {
        query = query.Where(listing => EF.Functions.ILike(listing.Country, $"%{country.Trim()}%"));
    }

    if (propertyType.HasValue)
    {
        query = query.Where(listing => listing.PropertyType == propertyType);
    }

    if (minPrice.HasValue)
    {
        query = query.Where(listing => listing.Price >= minPrice);
    }

    if (maxPrice.HasValue)
    {
        query = query.Where(listing => listing.Price <= maxPrice);
    }

    if (bedrooms.HasValue)
    {
        query = query.Where(listing => listing.Bedrooms >= bedrooms);
    }

    if (!string.IsNullOrWhiteSpace(q))
    {
        var search = $"%{q.Trim()}%";
        query = query.Where(listing =>
            EF.Functions.ILike(listing.Title, search)
            || EF.Functions.ILike(listing.Description, search)
            || EF.Functions.ILike(listing.City, search));
    }

    var listings = await query
        .OrderByDescending(listing => listing.CreatedAtUtc)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(listing => listing.ToResponse())
        .ToListAsync(cancellationToken);

    await cache.SetStringAsync(
        cacheKey,
        JsonSerializer.Serialize(listings),
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(45) },
        cancellationToken);

    return Results.Ok(listings);
}).AllowAnonymous();

group.MapGet("/{id:guid}", async (Guid id, ListingsDbContext db, IDistributedCache cache, CancellationToken cancellationToken) =>
{
    var cacheKey = CacheKeys.ListingDetail(id);
    var cached = await cache.GetStringAsync(cacheKey, cancellationToken);

    if (cached is not null)
    {
        return Results.Ok(JsonSerializer.Deserialize<ListingResponse>(cached));
    }

    var listing = await db.Listings.AsNoTracking().SingleOrDefaultAsync(listing => listing.Id == id, cancellationToken);

    if (listing is null || listing.Status != ListingStatus.Approved)
    {
        return Results.NotFound();
    }

    var response = listing.ToResponse();
    await cache.SetStringAsync(
        cacheKey,
        JsonSerializer.Serialize(response),
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
        cancellationToken);

    return Results.Ok(response);
}).AllowAnonymous();

group.MapGet("/mine", async (ClaimsPrincipal user, ListingsDbContext db, CancellationToken cancellationToken) =>
{
    var listings = await db.Listings
        .AsNoTracking()
        .Where(listing => listing.OwnerId == user.GetUserId())
        .OrderByDescending(listing => listing.CreatedAtUtc)
        .Select(listing => listing.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(listings);
}).RequireAuthorization("AgentOrAdmin");

group.MapPost("/", async Task<IResult> (
    CreateListingRequest request,
    ClaimsPrincipal user,
    ListingsDbContext db,
    CancellationToken cancellationToken) =>
{
    var validationError = ListingValidation.Validate(request);

    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    var listing = new Listing
    {
        OwnerId = user.GetUserId(),
        Title = request.Title.Trim(),
        Description = request.Description.Trim(),
        City = request.City.Trim(),
        State = request.State.Trim(),
        Country = request.Country.Trim(),
        AddressLine = request.AddressLine.Trim(),
        Price = request.Price,
        Bedrooms = request.Bedrooms,
        Bathrooms = request.Bathrooms,
        AreaSqm = request.AreaSqm,
        PropertyType = request.PropertyType,
        AmenitiesCsv = ListingMapping.JoinAmenities(request.Amenities)
    };

    db.Listings.Add(listing);
    db.OutboxMessages.Add(CreateOutbox(KafkaTopics.ListingCreated, listing));
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/listings/{listing.Id}", listing.ToResponse());
}).RequireAuthorization("AgentOrAdmin");

group.MapPut("/{id:guid}", async Task<IResult> (
    Guid id,
    UpdateListingRequest request,
    ClaimsPrincipal user,
    ListingsDbContext db,
    IDistributedCache cache,
    CancellationToken cancellationToken) =>
{
    var listing = await db.Listings.FindAsync([id], cancellationToken);

    if (listing is null)
    {
        return Results.NotFound();
    }

    if (listing.OwnerId != user.GetUserId() && !user.IsAdmin())
    {
        return Results.Forbid();
    }

    var validationError = ListingValidation.Validate(request);

    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    listing.Title = request.Title.Trim();
    listing.Description = request.Description.Trim();
    listing.City = request.City.Trim();
    listing.State = request.State.Trim();
    listing.Country = request.Country.Trim();
    listing.AddressLine = request.AddressLine.Trim();
    listing.Price = request.Price;
    listing.Bedrooms = request.Bedrooms;
    listing.Bathrooms = request.Bathrooms;
    listing.AreaSqm = request.AreaSqm;
    listing.PropertyType = request.PropertyType;
    listing.AmenitiesCsv = ListingMapping.JoinAmenities(request.Amenities);
    listing.UpdatedAtUtc = DateTimeOffset.UtcNow;

    db.OutboxMessages.Add(CreateOutbox(KafkaTopics.ListingUpdated, listing));
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(CacheKeys.ListingDetail(id), cancellationToken);

    return Results.Ok(listing.ToResponse());
}).RequireAuthorization("AgentOrAdmin");

group.MapPost("/{id:guid}/submit", async Task<IResult> (
    Guid id,
    ClaimsPrincipal user,
    HttpContext context,
    ListingsDbContext db,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    var listing = await db.Listings.FindAsync([id], cancellationToken);

    if (listing is null)
    {
        return Results.NotFound();
    }

    if (listing.OwnerId != user.GetUserId() && !user.IsAdmin())
    {
        return Results.Forbid();
    }

    if (!await HasActiveListingAdAsync(id, context, httpClientFactory, cancellationToken))
    {
        return Results.Json(
            new { error = "Complete a paid listing ad checkout before submitting this listing for approval." },
            statusCode: StatusCodes.Status402PaymentRequired);
    }

    listing.Status = ListingStatus.PendingApproval;
    listing.UpdatedAtUtc = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(listing.ToResponse());
}).RequireAuthorization("AgentOrAdmin");

group.MapPost("/{id:guid}/approve", async Task<IResult> (
    Guid id,
    ListingsDbContext db,
    IDistributedCache cache,
    CancellationToken cancellationToken) =>
{
    var listing = await db.Listings.FindAsync([id], cancellationToken);

    if (listing is null)
    {
        return Results.NotFound();
    }

    listing.Status = ListingStatus.Approved;
    listing.UpdatedAtUtc = DateTimeOffset.UtcNow;
    db.OutboxMessages.Add(CreateOutbox(KafkaTopics.ListingApproved, listing));
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(CacheKeys.ListingDetail(id), cancellationToken);

    return Results.Ok(listing.ToResponse());
}).RequireAuthorization("AdminOnly");

group.MapPost("/{id:guid}/reject", async Task<IResult> (
    Guid id,
    ListingsDbContext db,
    IDistributedCache cache,
    CancellationToken cancellationToken) =>
{
    var listing = await db.Listings.FindAsync([id], cancellationToken);

    if (listing is null)
    {
        return Results.NotFound();
    }

    listing.Status = ListingStatus.Rejected;
    listing.UpdatedAtUtc = DateTimeOffset.UtcNow;
    db.OutboxMessages.Add(CreateOutbox(KafkaTopics.ListingRejected, listing));
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(CacheKeys.ListingDetail(id), cancellationToken);

    return Results.Ok(listing.ToResponse());
}).RequireAuthorization("AdminOnly");

group.MapDelete("/{id:guid}", async Task<IResult> (
    Guid id,
    ClaimsPrincipal user,
    ListingsDbContext db,
    IDistributedCache cache,
    CancellationToken cancellationToken) =>
{
    var listing = await db.Listings.FindAsync([id], cancellationToken);

    if (listing is null)
    {
        return Results.NotFound();
    }

    if (listing.OwnerId != user.GetUserId() && !user.IsAdmin())
    {
        return Results.Forbid();
    }

    db.Listings.Remove(listing);
    await db.SaveChangesAsync(cancellationToken);
    await cache.RemoveAsync(CacheKeys.ListingDetail(id), cancellationToken);

    return Results.NoContent();
}).RequireAuthorization("AgentOrAdmin");

app.Run();

static OutboxMessage CreateOutbox(string topic, Listing listing) =>
    OutboxMessage.Create(
        topic,
        topic,
        "listings-api",
        new ListingChangedEvent(listing.Id, listing.OwnerId, listing.Status, listing.Title, listing.City, listing.Price),
        listing.Id.ToString());

static async Task<bool> HasActiveListingAdAsync(
    Guid listingId,
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken)
{
    var client = httpClientFactory.CreateClient("payments");
    using var request = new HttpRequestMessage(HttpMethod.Get, $"/payments/entitlements/listings/{listingId}/active");

    if (context.Request.Headers.Authorization.Count > 0)
    {
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(context.Request.Headers.Authorization.ToString());
    }

    var response = await client.SendAsync(request, cancellationToken);

    if (!response.IsSuccessStatusCode)
    {
        return false;
    }

    var entitlement = await response.Content.ReadFromJsonAsync<ActivePaymentEntitlement>(cancellationToken: cancellationToken);
    return entitlement?.IsActive == true;
}

sealed record ActivePaymentEntitlement(bool IsActive, Guid? EntitlementId, DateTimeOffset? ExpiresAtUtc);

public partial class Program;
