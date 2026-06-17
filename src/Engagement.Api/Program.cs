using System.Security.Claims;
using System.Text.Json;
using BuildingBlocks.Auth;
using BuildingBlocks.Events;
using BuildingBlocks.ServiceDefaults;
using Engagement.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddMarketplaceServiceDefaults("engagement-api")
    .AddPostgresDb<EngagementDbContext>("engagement")
    .AddKafkaOutbox<EngagementDbContext>("engagement-api");

var app = builder.Build();

app.UseMarketplaceServiceDefaults();

var favorites = app.MapGroup("/favorites").WithTags("Favorites").RequireAuthorization("BuyerOrAgent");

favorites.MapPost("/", async Task<IResult> (
    FavoriteRequest request,
    ClaimsPrincipal user,
    EngagementDbContext db,
    CancellationToken cancellationToken) =>
{
    if (request.ListingId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "ListingId is required." });
    }

    var userId = user.GetUserId();
    var existing = await db.Favorites.SingleOrDefaultAsync(
        favorite => favorite.UserId == userId && favorite.ListingId == request.ListingId,
        cancellationToken);

    if (existing is not null)
    {
        return Results.Ok(existing.ToResponse());
    }

    var favorite = new Favorite { UserId = userId, ListingId = request.ListingId };
    db.Favorites.Add(favorite);
    db.OutboxMessages.Add(OutboxMessage.Create(
        KafkaTopics.FavoriteCreated,
        KafkaTopics.FavoriteCreated,
        "engagement-api",
        new FavoriteCreatedEvent(favorite.Id, favorite.UserId, favorite.ListingId),
        favorite.ListingId.ToString()));
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/favorites/{favorite.Id}", favorite.ToResponse());
});

favorites.MapGet("/", async (ClaimsPrincipal user, EngagementDbContext db, CancellationToken cancellationToken) =>
{
    var userId = user.GetUserId();
    var response = await db.Favorites
        .AsNoTracking()
        .Where(favorite => favorite.UserId == userId)
        .OrderByDescending(favorite => favorite.CreatedAtUtc)
        .Select(favorite => favorite.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(response);
});

favorites.MapDelete("/{listingId:guid}", async Task<IResult> (
    Guid listingId,
    ClaimsPrincipal user,
    EngagementDbContext db,
    CancellationToken cancellationToken) =>
{
    var userId = user.GetUserId();
    var favorite = await db.Favorites.SingleOrDefaultAsync(
        favorite => favorite.UserId == userId && favorite.ListingId == listingId,
        cancellationToken);

    if (favorite is null)
    {
        return Results.NotFound();
    }

    db.Favorites.Remove(favorite);
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

var savedSearches = app.MapGroup("/saved-searches").WithTags("Saved Searches").RequireAuthorization("BuyerOrAgent");

savedSearches.MapPost("/", async Task<IResult> (
    SavedSearchRequest request,
    ClaimsPrincipal user,
    EngagementDbContext db,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "Saved search name is required." });
    }

    var search = new SavedSearch
    {
        UserId = user.GetUserId(),
        Name = request.Name.Trim(),
        QueryJson = JsonSerializer.Serialize(request.Filters)
    };

    db.SavedSearches.Add(search);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/saved-searches/{search.Id}", search.ToResponse());
});

savedSearches.MapGet("/", async (ClaimsPrincipal user, EngagementDbContext db, CancellationToken cancellationToken) =>
{
    var userId = user.GetUserId();
    var searches = await db.SavedSearches
        .AsNoTracking()
        .Where(search => search.UserId == userId)
        .OrderByDescending(search => search.CreatedAtUtc)
        .Select(search => search.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(searches);
});

var inquiries = app.MapGroup("/inquiries").WithTags("Inquiries").RequireAuthorization("BuyerOrAgent");

inquiries.MapPost("/", async Task<IResult> (
    InquiryRequest request,
    ClaimsPrincipal user,
    EngagementDbContext db,
    CancellationToken cancellationToken) =>
{
    if (request.ListingId == Guid.Empty || string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "ListingId and message are required." });
    }

    var inquiry = new Inquiry
    {
        ListingId = request.ListingId,
        BuyerId = user.GetUserId(),
        Message = request.Message.Trim()
    };

    db.Inquiries.Add(inquiry);
    db.OutboxMessages.Add(OutboxMessage.Create(
        KafkaTopics.InquiryCreated,
        KafkaTopics.InquiryCreated,
        "engagement-api",
        new InquiryCreatedEvent(inquiry.Id, inquiry.BuyerId, inquiry.ListingId, inquiry.Message),
        inquiry.ListingId.ToString()));
    db.OutboxMessages.Add(OutboxMessage.Create(
        KafkaTopics.NotificationRequested,
        KafkaTopics.NotificationRequested,
        "engagement-api",
        new NotificationRequestedEvent("email", inquiry.BuyerId, "Inquiry received", "Your property inquiry has been recorded."),
        inquiry.BuyerId.ToString()));
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/inquiries/{inquiry.Id}", inquiry.ToResponse());
});

inquiries.MapGet("/mine", async (ClaimsPrincipal user, EngagementDbContext db, CancellationToken cancellationToken) =>
{
    var userId = user.GetUserId();
    var response = await db.Inquiries
        .AsNoTracking()
        .Where(inquiry => inquiry.BuyerId == userId)
        .OrderByDescending(inquiry => inquiry.CreatedAtUtc)
        .Select(inquiry => inquiry.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(response);
});

var reviews = app.MapGroup("/reviews").WithTags("Reviews");

reviews.MapPost("/", async Task<IResult> (
    ReviewRequest request,
    ClaimsPrincipal user,
    EngagementDbContext db,
    CancellationToken cancellationToken) =>
{
    if (request.Rating is < 1 or > 5)
    {
        return Results.BadRequest(new { error = "Rating must be between 1 and 5." });
    }

    var review = new Review
    {
        ListingId = request.ListingId,
        AgentId = request.AgentId,
        ReviewerId = user.GetUserId(),
        Rating = request.Rating,
        Comment = request.Comment.Trim()
    };

    db.Reviews.Add(review);
    db.OutboxMessages.Add(OutboxMessage.Create(
        KafkaTopics.ReviewCreated,
        KafkaTopics.ReviewCreated,
        "engagement-api",
        new ReviewCreatedEvent(review.Id, review.ReviewerId, review.AgentId, review.ListingId, review.Rating),
        review.AgentId.ToString()));
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/reviews/{review.Id}", review.ToResponse());
}).RequireAuthorization("BuyerOrAgent");

reviews.MapGet("/agents/{agentId:guid}", async (Guid agentId, EngagementDbContext db, CancellationToken cancellationToken) =>
{
    var response = await db.Reviews
        .AsNoTracking()
        .Where(review => review.AgentId == agentId && review.Status == ReviewStatus.Published)
        .OrderByDescending(review => review.CreatedAtUtc)
        .Select(review => review.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(response);
}).AllowAnonymous();

reviews.MapGet("/pending", async (EngagementDbContext db, CancellationToken cancellationToken) =>
{
    var response = await db.Reviews
        .AsNoTracking()
        .Where(review => review.Status == ReviewStatus.Pending)
        .OrderBy(review => review.CreatedAtUtc)
        .Select(review => review.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(response);
}).RequireAuthorization("AdminOnly");

reviews.MapPost("/{id:guid}/publish", async Task<IResult> (Guid id, EngagementDbContext db, CancellationToken cancellationToken) =>
{
    var review = await db.Reviews.FindAsync([id], cancellationToken);

    if (review is null)
    {
        return Results.NotFound();
    }

    review.Status = ReviewStatus.Published;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(review.ToResponse());
}).RequireAuthorization("AdminOnly");

reviews.MapPost("/{id:guid}/reject", async Task<IResult> (Guid id, EngagementDbContext db, CancellationToken cancellationToken) =>
{
    var review = await db.Reviews.FindAsync([id], cancellationToken);

    if (review is null)
    {
        return Results.NotFound();
    }

    review.Status = ReviewStatus.Rejected;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(review.ToResponse());
}).RequireAuthorization("AdminOnly");

var agents = app.MapGroup("/agents").WithTags("Agents");

agents.MapPut("/profile", async (
    AgentProfileRequest request,
    ClaimsPrincipal user,
    EngagementDbContext db,
    CancellationToken cancellationToken) =>
{
    var userId = user.GetUserId();
    var profile = await db.AgentProfiles.FindAsync([userId], cancellationToken);

    if (profile is null)
    {
        profile = new AgentProfile { UserId = userId };
        db.AgentProfiles.Add(profile);
    }

    profile.DisplayName = request.DisplayName.Trim();
    profile.AgencyName = request.AgencyName.Trim();
    profile.Phone = request.Phone.Trim();
    profile.LicenseNumber = request.LicenseNumber.Trim();
    profile.Bio = request.Bio.Trim();
    profile.UpdatedAtUtc = DateTimeOffset.UtcNow;

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(profile.ToResponse());
}).RequireAuthorization("AgentOrAdmin");

agents.MapGet("/{agentId:guid}", async Task<IResult> (Guid agentId, EngagementDbContext db, CancellationToken cancellationToken) =>
{
    var profile = await db.AgentProfiles.AsNoTracking().SingleOrDefaultAsync(profile => profile.UserId == agentId, cancellationToken);
    return profile is null ? Results.NotFound() : Results.Ok(profile.ToResponse());
}).AllowAnonymous();

app.Run();

public partial class Program;
