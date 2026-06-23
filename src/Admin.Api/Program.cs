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
builder.Services.AddHttpClient("payments", (sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(configuration["Services:Payments"] ?? "http://payments-api:8080");
});

var app = builder.Build();

app.UseMarketplaceServiceDefaults();

var admin = app.MapGroup("/admin").WithTags("Admin").RequireAuthorization("AdminOnly");

admin.MapGet("/summary", async (IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
{
    var services = new[] { "identity", "listings", "engagement", "notifications", "payments" };
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

admin.MapGet("/payments/ad-packages", (HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("payments"), HttpMethod.Get, "/admin/payments/ad-packages", cancellationToken));

admin.MapPost("/payments/ad-packages", (HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("payments"), HttpMethod.Post, "/admin/payments/ad-packages", cancellationToken));

admin.MapPut("/payments/ad-packages/{packageId:guid}", (Guid packageId, HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("payments"), HttpMethod.Put, $"/admin/payments/ad-packages/{packageId}", cancellationToken));

admin.MapDelete("/payments/ad-packages/{packageId:guid}", (Guid packageId, HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("payments"), HttpMethod.Delete, $"/admin/payments/ad-packages/{packageId}", cancellationToken));

admin.MapGet("/payments/promo-codes", (HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("payments"), HttpMethod.Get, "/admin/payments/promo-codes", cancellationToken));

admin.MapPost("/payments/promo-codes", (HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("payments"), HttpMethod.Post, "/admin/payments/promo-codes", cancellationToken));

admin.MapPut("/payments/promo-codes/{promoCodeId:guid}", (Guid promoCodeId, HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("payments"), HttpMethod.Put, $"/admin/payments/promo-codes/{promoCodeId}", cancellationToken));

admin.MapDelete("/payments/promo-codes/{promoCodeId:guid}", (Guid promoCodeId, HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("payments"), HttpMethod.Delete, $"/admin/payments/promo-codes/{promoCodeId}", cancellationToken));

admin.MapGet("/payments/checkouts", (HttpContext context, IHttpClientFactory factory, CancellationToken cancellationToken) =>
    ForwardAsync(context, factory.CreateClient("payments"), HttpMethod.Get, "/admin/payments/checkouts", cancellationToken));

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

    if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        request.Content = new StreamContent(context.Request.Body);

        if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
        {
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
        }
    }

    using var response = await client.SendAsync(request, cancellationToken);
    var body = await response.Content.ReadAsStringAsync(cancellationToken);
    var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

    return Results.Content(body, contentType, statusCode: (int)response.StatusCode);
}

public partial class Program;
