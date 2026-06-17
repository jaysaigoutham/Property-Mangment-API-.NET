using System.Security.Claims;
using BuildingBlocks.Auth;
using BuildingBlocks.Events;
using BuildingBlocks.ServiceDefaults;
using Identity.Api;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddMarketplaceServiceDefaults("identity-api")
    .AddPostgresDb<IdentityDbContext>("identity")
    .AddKafkaOutbox<IdentityDbContext>("identity-api")
    .AddJwtTokens();

builder.Services.AddScoped<PasswordHasher<UserAccount>>();

var app = builder.Build();

app.UseMarketplaceServiceDefaults();

var auth = app.MapGroup("/auth").WithTags("Auth");

auth.MapPost("/register", async (
    RegisterRequest request,
    IdentityDbContext db,
    PasswordHasher<UserAccount> passwordHasher,
    IJwtTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var normalizedEmail = request.Email.Trim().ToUpperInvariant();
    var role = AppRoles.Normalize(string.IsNullOrWhiteSpace(request.Role) ? AppRoles.Buyer : request.Role);

    if (!AppRoles.IsKnown(role))
    {
        return Results.BadRequest(new { error = "Role must be buyer, agent, or admin." });
    }

    if (request.Password.Length < 8)
    {
        return Results.BadRequest(new { error = "Password must contain at least 8 characters." });
    }

    if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken))
    {
        return Results.Conflict(new { error = "A user with this email already exists." });
    }

    var user = new UserAccount
    {
        Email = request.Email.Trim(),
        NormalizedEmail = normalizedEmail,
        DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Email.Trim() : request.DisplayName.Trim(),
        Role = role
    };

    user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
    var token = tokens.Create(user.Id, user.Email, user.Role, user.DisplayName);
    user.RefreshTokenHash = tokens.HashRefreshToken(token.RefreshToken);
    user.RefreshTokenExpiresAtUtc = token.RefreshTokenExpiresAtUtc;

    db.Users.Add(user);
    db.OutboxMessages.Add(OutboxMessage.Create(
        KafkaTopics.UserRegistered,
        KafkaTopics.UserRegistered,
        "identity-api",
        new UserRegisteredEvent(user.Id, user.Email, user.Role, user.DisplayName),
        user.Id.ToString()));

    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/users/{user.Id}", ToAuthResponse(user, token));
}).AllowAnonymous();

auth.MapPost("/login", async (
    LoginRequest request,
    IdentityDbContext db,
    PasswordHasher<UserAccount> passwordHasher,
    IJwtTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var normalizedEmail = request.Email.Trim().ToUpperInvariant();
    var user = await db.Users.SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

    if (result == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    var token = tokens.Create(user.Id, user.Email, user.Role, user.DisplayName);
    user.RefreshTokenHash = tokens.HashRefreshToken(token.RefreshToken);
    user.RefreshTokenExpiresAtUtc = token.RefreshTokenExpiresAtUtc;
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(ToAuthResponse(user, token));
}).AllowAnonymous();

auth.MapPost("/refresh", async (
    RefreshTokenRequest request,
    IdentityDbContext db,
    IJwtTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var user = await db.Users.FindAsync([request.UserId], cancellationToken);

    if (user?.RefreshTokenHash is null || user.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
    {
        return Results.Unauthorized();
    }

    if (!tokens.VerifyRefreshToken(request.RefreshToken, user.RefreshTokenHash))
    {
        return Results.Unauthorized();
    }

    var token = tokens.Create(user.Id, user.Email, user.Role, user.DisplayName);
    user.RefreshTokenHash = tokens.HashRefreshToken(token.RefreshToken);
    user.RefreshTokenExpiresAtUtc = token.RefreshTokenExpiresAtUtc;
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(ToAuthResponse(user, token));
}).AllowAnonymous();

auth.MapGet("/profile", async (ClaimsPrincipal user, IdentityDbContext db, CancellationToken cancellationToken) =>
{
    var currentUser = await db.Users.FindAsync([user.GetUserId()], cancellationToken);
    return currentUser is null ? Results.NotFound() : Results.Ok(ToUserResponse(currentUser));
}).RequireAuthorization();

app.MapGet("/users", async (IdentityDbContext db, CancellationToken cancellationToken) =>
{
    var users = await db.Users
        .OrderByDescending(user => user.CreatedAtUtc)
        .Select(user => new UserResponse(user.Id, user.Email, user.DisplayName, user.Role, user.CreatedAtUtc))
        .ToListAsync(cancellationToken);

    return Results.Ok(users);
}).RequireAuthorization("AdminOnly").WithTags("Admin");

app.Run();

static AuthResponse ToAuthResponse(UserAccount user, TokenResponse token) =>
    new(user.Id, user.Email, user.DisplayName, user.Role, token.AccessToken, token.ExpiresAtUtc, token.RefreshToken, token.RefreshTokenExpiresAtUtc);

static UserResponse ToUserResponse(UserAccount user) =>
    new(user.Id, user.Email, user.DisplayName, user.Role, user.CreatedAtUtc);

public partial class Program;
