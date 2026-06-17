using BuildingBlocks.Events;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api;

public sealed class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? RefreshTokenHash { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record RegisterRequest(string Email, string Password, string DisplayName, string Role);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshTokenRequest(Guid UserId, string RefreshToken);
public sealed record AuthResponse(Guid UserId, string Email, string DisplayName, string Role, string AccessToken, DateTimeOffset ExpiresAtUtc, string RefreshToken, DateTimeOffset RefreshTokenExpiresAtUtc);
public sealed record UserResponse(Guid Id, string Email, string DisplayName, string Role, DateTimeOffset CreatedAtUtc);
public sealed record UserRegisteredEvent(Guid UserId, string Email, string Role, string DisplayName);

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");

        modelBuilder.Entity<UserAccount>(builder =>
        {
            builder.ToTable("users");
            builder.HasKey(user => user.Id);
            builder.Property(user => user.Email).HasMaxLength(320).IsRequired();
            builder.Property(user => user.NormalizedEmail).HasMaxLength(320).IsRequired();
            builder.Property(user => user.DisplayName).HasMaxLength(160).IsRequired();
            builder.Property(user => user.Role).HasMaxLength(40).IsRequired();
            builder.Property(user => user.PasswordHash).IsRequired();
            builder.HasIndex(user => user.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>().ConfigureOutbox();
    }
}
