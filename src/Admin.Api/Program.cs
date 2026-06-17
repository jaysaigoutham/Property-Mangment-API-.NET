using BuildingBlocks.ServiceDefaults;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.AddMarketplaceServiceDefaults("admin-api");

builder.Services.AddHttpClient("identity", (sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(configuration["Services:Identity"] ?? "http://identity-api:8080");
});
builder.Services.AddHttpClient("listings", (sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(configuration["Services:Listings"] ?? "http://listings-api:8080");
});
builder.Services.AddHttpClient("engagement", (sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(configuration["Services:Engagement"] ?? "http://engagement-api:8080");
});
builder.Services.AddHttpClient("notifications", (sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(configuration["Services:Notifications"] ?? "http://notifications-api:8080");
});

var app = builder.Build();

app.UseMarketplaceServiceDefaults();

var admin = app.MapGroup("/admin").WithTags("Admin").RequireAuthorization("AdminOnly");

admin.MapGet("/summary", async (IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
{
    var services = new[] { "identity", "listings", "engagement", "notifications" };
    var results = new List<object>();

    foreach (var service in services)
    {
        var client = httpClientFactory.CreateClient(service);
        try
        {
            var response = await client.GetAsync("/health/ready", cancellationToken);
            results.Add(new { service, status = response.IsSuccessStatusCode ? "ready" : "unhealthy", code = (int)response.StatusCode });
        }
        catch (Exception ex)
        {
            results.Add(new { service, status = "unreachable", error = ex.Message });
        }
    }

    return Results.Ok(new { generatedAtUtc = DateTimeOffset.UtcNow, services = results });
});

admin.MapGet("/users", (HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("identity"), HttpMethod.Get, "/users", cancellationToken));

admin.MapPost("/listings/{listingId:guid}/approve", (Guid listingId, HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("listings"), HttpMethod.Post, $"/listings/{listingId}/approve", cancellationToken));

admin.MapPost("/listings/{listingId:guid}/reject", (Guid listingId, HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("listings"), HttpMethod.Post, $"/listings/{listingId}/reject", cancellationToken));

admin.MapGet("/reviews/pending", (HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("engagement"), HttpMethod.Get, "/reviews/pending", cancellationToken));

admin.MapPost("/reviews/{reviewId:guid}/publish", (Guid reviewId, HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("engagement"), HttpMethod.Post, $"/reviews/{reviewId}/publish", cancellationToken));

admin.MapPost("/reviews/{reviewId:guid}/reject", (Guid reviewId, HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("engagement"), HttpMethod.Post, $"/reviews/{reviewId}/reject", cancellationToken));

admin.MapGet("/notifications", (HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("notifications"), HttpMethod.Get, "/notifications", cancellationToken));

app.Run();

static async Task<IResult> ForwardAsync(
    HttpContext context,
    HttpClient client,
    HttpMethod method,
    string path,
    CancellationToken cancellationToken)
{
    using var request = new HttpRequestMessage(method, path);

    if (context.Request.Headers.Authorization.Count > 0)
    {
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(context.Request.Headers.Authorization.ToString());
    }

    using var response = await client.SendAsync(request, cancellationToken);
    var body = await response.Content.ReadAsStringAsync(cancellationToken);
    var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

    return Results.Content(body, contentType, statusCode: (int)response.StatusCode);
}

public partial class Program;
