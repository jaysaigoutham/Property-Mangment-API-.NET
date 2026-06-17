using System.Text;
using System.Text.Json.Serialization;
using BuildingBlocks.Auth;
using BuildingBlocks.Events;
using BuildingBlocks.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace BuildingBlocks.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddMarketplaceServiceDefaults(
        this WebApplicationBuilder builder,
        string serviceName,
        bool enableAuth = true)
    {
        builder.Configuration.AddEnvironmentVariables();
        builder.Host.UseSerilog((context, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service", serviceName)
                .WriteTo.Console();
        });

        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
        builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.SectionName));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddHealthChecks();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("marketplace", policy =>
                policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("fixed", limiter =>
            {
                limiter.PermitLimit = 120;
                limiter.Window = TimeSpan.FromMinutes(1);
            });
        });

        var redis = builder.Configuration.GetConnectionString("redis")
            ?? builder.Configuration["Redis:ConnectionString"];

        if (string.IsNullOrWhiteSpace(redis))
        {
            builder.Services.AddDistributedMemoryCache();
        }
        else
        {
            builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redis);
        }

        if (enableAuth)
        {
            var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwt.Issuer,
                        ValidateAudience = true,
                        ValidAudience = jwt.Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(1)
                    };
                });

            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("AgentOrAdmin", policy => policy.RequireRole(AppRoles.Agent, AppRoles.Admin));
                options.AddPolicy("AdminOnly", policy => policy.RequireRole(AppRoles.Admin));
                options.AddPolicy("BuyerOrAgent", policy => policy.RequireRole(AppRoles.Buyer, AppRoles.Agent, AppRoles.Admin));
            });
        }

        return builder;
    }

    public static WebApplication UseMarketplaceServiceDefaults(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseSerilogRequestLogging();
        app.UseCors("marketplace");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHealthChecks("/health/live");
        app.MapGet("/health/ready", () => Results.Ok(new { status = "ready", time = DateTimeOffset.UtcNow }));

        return app;
    }

    public static WebApplicationBuilder AddPostgresDb<TContext>(
        this WebApplicationBuilder builder,
        string connectionName)
        where TContext : DbContext
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionName)
            ?? builder.Configuration.GetConnectionString("postgres")
            ?? throw new InvalidOperationException($"Missing PostgreSQL connection string '{connectionName}'.");

        builder.Services.AddDbContext<TContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddHostedService<DatabaseInitializer<TContext>>();
        return builder;
    }

    public static WebApplicationBuilder AddKafkaOutbox<TContext>(
        this WebApplicationBuilder builder,
        string serviceName)
        where TContext : DbContext, IOutboxDbContext
    {
        builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.SectionName));
        builder.Services.PostConfigure<KafkaOptions>(options => options.ClientId = serviceName);
        builder.Services.AddSingleton<IKafkaProducer, KafkaProducer>();
        builder.Services.AddHostedService<OutboxPublisherService<TContext>>();
        return builder;
    }

    public static WebApplicationBuilder AddJwtTokens(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
        return builder;
    }
}
