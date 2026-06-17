using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.ServiceDefaults;

public sealed class DatabaseInitializer<TContext>(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<DatabaseInitializer<TContext>> logger) : IHostedService
    where TContext : DbContext
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        try
        {
            var useMigrations = configuration.GetValue("Database:UseMigrations", false);

            if (useMigrations)
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
            else
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database initialization failed for {DbContext}", typeof(TContext).Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
