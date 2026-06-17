using System.Security.Claims;
using BuildingBlocks.Auth;
using BuildingBlocks.ServiceDefaults;
using Media.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddMarketplaceServiceDefaults("media-api")
    .AddPostgresDb<MediaDbContext>("media")
    .AddKafkaOutbox<MediaDbContext>("media-api");

builder.Services.Configure<MinioStorageOptions>(builder.Configuration.GetSection(MinioStorageOptions.SectionName));
builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<MinioStorageOptions>>().Value;
    return new MinioClient()
        .WithEndpoint(options.Endpoint)
        .WithCredentials(options.AccessKey, options.SecretKey)
        .WithSSL(options.UseSsl)
        .Build();
});

var app = builder.Build();

app.UseMarketplaceServiceDefaults();

var group = app.MapGroup("/media").WithTags("Media");

group.MapPost("/upload-url", async Task<IResult> (
    UploadUrlRequest request,
    ClaimsPrincipal user,
    MediaDbContext db,
    IMinioClient minio,
    IOptions<MinioStorageOptions> options,
    CancellationToken cancellationToken) =>
{
    if (request.ListingId == Guid.Empty || string.IsNullOrWhiteSpace(request.FileName))
    {
        return Results.BadRequest(new { error = "ListingId and FileName are required." });
    }

    if (!request.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Only image content types are supported." });
    }

    var storage = options.Value;
    var extension = Path.GetExtension(request.FileName);
    var objectName = $"listings/{request.ListingId:N}/{Guid.NewGuid():N}{extension}";
    var uploadUrl = await minio.PresignedPutObjectAsync(new PresignedPutObjectArgs()
        .WithBucket(storage.Bucket)
        .WithObject(objectName)
        .WithExpiry(storage.UploadUrlExpirySeconds));

    var publicUrl = $"{storage.PublicBaseUrl.TrimEnd('/')}/{storage.Bucket}/{objectName}";
    var asset = new MediaAsset
    {
        ListingId = request.ListingId,
        UploadedByUserId = user.GetUserId(),
        Bucket = storage.Bucket,
        ObjectName = objectName,
        PublicUrl = publicUrl,
        ContentType = request.ContentType,
        SortOrder = request.SortOrder
    };

    db.MediaAssets.Add(asset);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/media/{asset.Id}", new UploadUrlResponse(
        asset.Id,
        storage.Bucket,
        objectName,
        uploadUrl,
        publicUrl,
        DateTimeOffset.UtcNow.AddSeconds(storage.UploadUrlExpirySeconds)));
}).RequireAuthorization("AgentOrAdmin");

group.MapGet("/listings/{listingId:guid}", async (Guid listingId, MediaDbContext db, CancellationToken cancellationToken) =>
{
    var media = await db.MediaAssets
        .AsNoTracking()
        .Where(asset => asset.ListingId == listingId)
        .OrderBy(asset => asset.SortOrder)
        .Select(asset => asset.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(media);
}).AllowAnonymous();

group.MapPatch("/{id:guid}/sort-order", async Task<IResult> (
    Guid id,
    ReorderMediaRequest request,
    ClaimsPrincipal user,
    MediaDbContext db,
    CancellationToken cancellationToken) =>
{
    var asset = await db.MediaAssets.FindAsync([id], cancellationToken);

    if (asset is null)
    {
        return Results.NotFound();
    }

    if (asset.UploadedByUserId != user.GetUserId() && !user.IsAdmin())
    {
        return Results.Forbid();
    }

    asset.SortOrder = request.SortOrder;
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(asset.ToResponse());
}).RequireAuthorization("AgentOrAdmin");

group.MapDelete("/{id:guid}", async Task<IResult> (
    Guid id,
    ClaimsPrincipal user,
    MediaDbContext db,
    CancellationToken cancellationToken) =>
{
    var asset = await db.MediaAssets.FindAsync([id], cancellationToken);

    if (asset is null)
    {
        return Results.NotFound();
    }

    if (asset.UploadedByUserId != user.GetUserId() && !user.IsAdmin())
    {
        return Results.Forbid();
    }

    db.MediaAssets.Remove(asset);
    await db.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
}).RequireAuthorization("AgentOrAdmin");

app.Run();

public partial class Program;
