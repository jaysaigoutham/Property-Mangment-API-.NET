using BuildingBlocks.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddMarketplaceServiceDefaults("gateway-api");
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseMarketplaceServiceDefaults();

app.MapGet("/", () => Results.Ok(new
{
    name = "Property Marketplace Gateway",
    routes = new[]
    {
        "/auth",
        "/listings",
        "/media",
        "/favorites",
        "/saved-searches",
        "/inquiries",
        "/reviews",
        "/agents",
        "/admin"
    }
})).AllowAnonymous();

app.MapReverseProxy();

app.Run();

public partial class Program;
