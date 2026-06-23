using System.Data;
using System.Security.Claims;
using System.Text.Json;
using BuildingBlocks.Auth;
using BuildingBlocks.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Payments.Api;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddMarketplaceServiceDefaults("payments-api")
    .AddPostgresDb<PaymentsDbContext>("payments")
    .AddKafkaOutbox<PaymentsDbContext>("payments-api");

builder.Services.Configure<StripePaymentOptions>(builder.Configuration.GetSection(StripePaymentOptions.SectionName));
builder.Services.Configure<PayHerePaymentOptions>(builder.Configuration.GetSection(PayHerePaymentOptions.SectionName));
builder.Services.AddScoped<PaymentPricingService>();
builder.Services.AddSingleton<IPaymentProvider, StripeCheckoutProvider>();
builder.Services.AddSingleton<IPaymentProvider, PayHereCheckoutProvider>();
builder.Services.AddSingleton<PaymentProviderRegistry>();
builder.Services.AddHostedService<PaymentSeedData>();
builder.Services.AddHostedService<PromoCleanupService>();

var app = builder.Build();

app.UseMarketplaceServiceDefaults();

var payments = app.MapGroup("/payments").WithTags("Payments");

payments.MapGet("/ad-packages", async (PaymentsDbContext db, CancellationToken cancellationToken) =>
{
    var packages = await db.AdPackages
        .AsNoTracking()
        .Where(package => package.IsActive)
        .OrderBy(package => package.DisplayOrder)
        .ThenBy(package => package.Name)
        .Select(package => package.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(packages);
}).AllowAnonymous();

payments.MapPost("/checkouts/preview", async Task<IResult> (
    CheckoutPreviewRequest request,
    ClaimsPrincipal user,
    PaymentPricingService pricing,
    CancellationToken cancellationToken) =>
{
    if (request.ListingId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Listing id is required." });
    }

    var preview = await pricing.PreviewAsync(user.GetUserId(), request.AdPackageId, request.PromoCode, cancellationToken);

    if (!preview.IsValid || preview.Package is null)
    {
        return Results.BadRequest(new { error = preview.Error });
    }

    return Results.Ok(new PaymentPreviewResponse(
        preview.Package.Id,
        preview.Package.Name,
        preview.OriginalAmount,
        preview.DiscountAmount,
        preview.FinalAmount,
        preview.Currency,
        preview.PromoCode?.Code,
        preview.PromoCode is null ? null : "Promo code applied."));
}).RequireAuthorization("AgentOrAdmin");

payments.MapPost("/checkouts", async Task<IResult> (
    CreateCheckoutRequest request,
    ClaimsPrincipal user,
    PaymentsDbContext db,
    PaymentPricingService pricing,
    PaymentProviderRegistry providers,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    if (request.ListingId == Guid.Empty)
    {
        return Results.BadRequest(new { error = "Listing id is required." });
    }

    if (!Uri.TryCreate(request.SuccessUrl, UriKind.Absolute, out _) || !Uri.TryCreate(request.CancelUrl, UriKind.Absolute, out _))
    {
        return Results.BadRequest(new { error = "Success and cancel URLs must be absolute URLs." });
    }

    var userId = user.GetUserId();
    PaymentPricingResult preview;
    PaymentCheckout checkout;
    PromoReservation? reservation = null;
    var now = timeProvider.GetUtcNow();

    await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken))
    {
        preview = await pricing.PreviewAsync(userId, request.AdPackageId, request.PromoCode, cancellationToken);

        if (!preview.IsValid || preview.Package is null)
        {
            return Results.BadRequest(new { error = preview.Error });
        }

        checkout = new PaymentCheckout
        {
            UserId = userId,
            ListingId = request.ListingId,
            AdPackageId = preview.Package.Id,
            PromoCodeId = preview.PromoCode?.Id,
            Provider = request.Provider,
            OriginalAmount = preview.OriginalAmount,
            DiscountAmount = preview.DiscountAmount,
            FinalAmount = preview.FinalAmount,
            Currency = preview.Currency,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(30)
        };

        if (preview.PromoCode is not null)
        {
            reservation = new PromoReservation
            {
                PromoCodeId = preview.PromoCode.Id,
                UserId = userId,
                DiscountAmount = preview.DiscountAmount,
                Currency = preview.Currency,
                ReservedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(15)
            };

            checkout.PromoReservationId = reservation.Id;
            db.PromoReservations.Add(reservation);
        }

        db.PaymentCheckouts.Add(checkout);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    if (checkout.FinalAmount <= 0)
    {
        checkout.ProviderReference = $"internal:{checkout.Id:N}";
        checkout.Status = PaymentCheckoutStatus.Completed;
        checkout.CompletedAtUtc = now;

        if (reservation is not null)
        {
            reservation.Status = PromoReservationStatus.Completed;
            reservation.CompletedAtUtc = now;
        }

        var entitlement = await CreateEntitlementAsync(db, checkout, preview.Package.DurationDays, now, cancellationToken);
        db.OutboxMessages.Add(ProgramEvents.CheckoutCreated(checkout));
        db.OutboxMessages.Add(ProgramEvents.PaymentCompleted(checkout));

        if (entitlement is not null)
        {
            db.OutboxMessages.Add(ProgramEvents.EntitlementCreated(entitlement));
        }

        await db.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/payments/checkouts/{checkout.Id}",
            new ProviderCheckoutResponse(checkout.ToResponse(), null, null));
    }

    ProviderCheckoutResult providerResult;

    try
    {
        providerResult = await providers
            .Get(request.Provider)
            .CreateCheckoutAsync(checkout, preview.Package, request.SuccessUrl, request.CancelUrl, cancellationToken);
    }
    catch (Exception ex) when (ex is InvalidOperationException or Stripe.StripeException)
    {
        checkout.Status = PaymentCheckoutStatus.Failed;
        checkout.FailureReason = ex.Message;

        if (reservation is not null)
        {
            reservation.Status = PromoReservationStatus.Released;
        }

        db.OutboxMessages.Add(ProgramEvents.PaymentFailed(checkout, ex.Message));
        await db.SaveChangesAsync(cancellationToken);
        return Results.BadRequest(new { error = ex.Message });
    }

    checkout.ProviderReference = providerResult.ProviderReference;
    checkout.ProviderCheckoutUrl = providerResult.CheckoutUrl;
    db.OutboxMessages.Add(ProgramEvents.CheckoutCreated(checkout));
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created(
        $"/payments/checkouts/{checkout.Id}",
        new ProviderCheckoutResponse(checkout.ToResponse(), providerResult.CheckoutUrl, providerResult.FormFields));
}).RequireAuthorization("AgentOrAdmin");

payments.MapGet("/checkouts/{id:guid}", async Task<IResult> (
    Guid id,
    ClaimsPrincipal user,
    PaymentsDbContext db,
    CancellationToken cancellationToken) =>
{
    var checkout = await db.PaymentCheckouts
        .AsNoTracking()
        .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

    if (checkout is null)
    {
        return Results.NotFound();
    }

    if (checkout.UserId != user.GetUserId() && !user.IsAdmin())
    {
        return Results.Forbid();
    }

    return Results.Ok(checkout.ToResponse());
}).RequireAuthorization();

payments.MapGet("/entitlements/listings/{listingId:guid}/active", async (
    Guid listingId,
    ClaimsPrincipal user,
    PaymentsDbContext db,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    var now = timeProvider.GetUtcNow();
    var userId = user.GetUserId();
    var query = db.AdEntitlements
        .AsNoTracking()
        .Where(entitlement => entitlement.ListingId == listingId
            && entitlement.IsActive
            && entitlement.ExpiresAtUtc > now);

    if (!user.IsAdmin())
    {
        query = query.Where(entitlement => entitlement.UserId == userId);
    }

    var active = await query
        .OrderByDescending(entitlement => entitlement.ExpiresAtUtc)
        .FirstOrDefaultAsync(cancellationToken);

    return Results.Ok(new ActiveEntitlementResponse(active is not null, active?.Id, active?.ExpiresAtUtc));
}).RequireAuthorization("AgentOrAdmin");

payments.MapPost("/webhooks/stripe", async (
    HttpRequest request,
    PaymentsDbContext db,
    PaymentProviderRegistry providers,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
    await ProcessWebhookAsync(PaymentProvider.Stripe, request, db, providers, timeProvider, cancellationToken)).AllowAnonymous();

payments.MapPost("/webhooks/payhere", async (
    HttpRequest request,
    PaymentsDbContext db,
    PaymentProviderRegistry providers,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
    await ProcessWebhookAsync(PaymentProvider.PayHere, request, db, providers, timeProvider, cancellationToken)).AllowAnonymous();

var admin = app.MapGroup("/admin/payments").WithTags("Admin Payments").RequireAuthorization("AdminOnly");

admin.MapGet("/ad-packages", async (PaymentsDbContext db, CancellationToken cancellationToken) =>
{
    var packages = await db.AdPackages
        .AsNoTracking()
        .OrderBy(package => package.DisplayOrder)
        .ThenBy(package => package.Name)
        .Select(package => package.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(packages);
});

admin.MapPost("/ad-packages", async Task<IResult> (
    AdminAdPackageRequest request,
    PaymentsDbContext db,
    CancellationToken cancellationToken) =>
{
    var validationError = ValidateAdPackage(request);

    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    var now = DateTimeOffset.UtcNow;
    var package = new AdPackage
    {
        Name = request.Name.Trim(),
        Description = request.Description?.Trim() ?? string.Empty,
        Price = PaymentPricingService.RoundMoney(request.Price),
        Currency = PaymentPricingService.NormalizeCurrency(request.Currency),
        DurationDays = request.DurationDays,
        IsActive = request.IsActive,
        DisplayOrder = request.DisplayOrder,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    db.AdPackages.Add(package);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Created($"/admin/payments/ad-packages/{package.Id}", package.ToResponse());
});

admin.MapPut("/ad-packages/{id:guid}", async Task<IResult> (
    Guid id,
    AdminAdPackageRequest request,
    PaymentsDbContext db,
    CancellationToken cancellationToken) =>
{
    var package = await db.AdPackages.FindAsync([id], cancellationToken);

    if (package is null)
    {
        return Results.NotFound();
    }

    var validationError = ValidateAdPackage(request);

    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    package.Name = request.Name.Trim();
    package.Description = request.Description?.Trim() ?? string.Empty;
    package.Price = PaymentPricingService.RoundMoney(request.Price);
    package.Currency = PaymentPricingService.NormalizeCurrency(request.Currency);
    package.DurationDays = request.DurationDays;
    package.IsActive = request.IsActive;
    package.DisplayOrder = request.DisplayOrder;
    package.UpdatedAtUtc = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(package.ToResponse());
});

admin.MapDelete("/ad-packages/{id:guid}", async Task<IResult> (
    Guid id,
    PaymentsDbContext db,
    CancellationToken cancellationToken) =>
{
    var package = await db.AdPackages.FindAsync([id], cancellationToken);

    if (package is null)
    {
        return Results.NotFound();
    }

    package.IsActive = false;
    package.UpdatedAtUtc = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

admin.MapGet("/promo-codes", async (PaymentsDbContext db, CancellationToken cancellationToken) =>
{
    var promoCodes = await db.PromoCodes
        .AsNoTracking()
        .OrderByDescending(code => code.CreatedAtUtc)
        .Select(code => code.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(promoCodes);
});

admin.MapPost("/promo-codes", async Task<IResult> (
    AdminPromoCodeRequest request,
    PaymentsDbContext db,
    CancellationToken cancellationToken) =>
{
    var validationError = ValidatePromoCode(request);

    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    var code = PaymentPricingService.NormalizeCode(request.Code);

    if (await db.PromoCodes.AnyAsync(candidate => candidate.Code == code, cancellationToken))
    {
        return Results.Conflict(new { error = "Promo code already exists." });
    }

    var now = DateTimeOffset.UtcNow;
    var promoCode = new PromoCode
    {
        Code = code,
        Description = request.Description?.Trim() ?? string.Empty,
        DiscountType = request.DiscountType,
        PercentOff = request.DiscountType == PromoDiscountType.Percent ? request.PercentOff : null,
        AmountOff = request.DiscountType == PromoDiscountType.FixedAmount ? PaymentPricingService.RoundMoney(request.AmountOff.GetValueOrDefault()) : null,
        Currency = string.IsNullOrWhiteSpace(request.Currency) ? null : PaymentPricingService.NormalizeCurrency(request.Currency),
        MaxRedemptions = request.MaxRedemptions,
        PerUserLimit = request.PerUserLimit,
        ValidFromUtc = request.ValidFromUtc,
        ExpiresAtUtc = request.ExpiresAtUtc,
        AutoDeleteAtUtc = request.AutoDeleteAtUtc,
        IsActive = request.IsActive,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    db.PromoCodes.Add(promoCode);
    db.OutboxMessages.Add(ProgramEvents.PromoCodeCreated(promoCode));
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/admin/payments/promo-codes/{promoCode.Id}", promoCode.ToResponse());
});

admin.MapPut("/promo-codes/{id:guid}", async Task<IResult> (
    Guid id,
    AdminPromoCodeRequest request,
    PaymentsDbContext db,
    CancellationToken cancellationToken) =>
{
    var promoCode = await db.PromoCodes.FindAsync([id], cancellationToken);

    if (promoCode is null)
    {
        return Results.NotFound();
    }

    var validationError = ValidatePromoCode(request);

    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    var normalizedCode = PaymentPricingService.NormalizeCode(request.Code);
    var codeExists = await db.PromoCodes.AnyAsync(
        candidate => candidate.Id != id && candidate.Code == normalizedCode,
        cancellationToken);

    if (codeExists)
    {
        return Results.Conflict(new { error = "Promo code already exists." });
    }

    promoCode.Code = normalizedCode;
    promoCode.Description = request.Description?.Trim() ?? string.Empty;
    promoCode.DiscountType = request.DiscountType;
    promoCode.PercentOff = request.DiscountType == PromoDiscountType.Percent ? request.PercentOff : null;
    promoCode.AmountOff = request.DiscountType == PromoDiscountType.FixedAmount ? PaymentPricingService.RoundMoney(request.AmountOff.GetValueOrDefault()) : null;
    promoCode.Currency = string.IsNullOrWhiteSpace(request.Currency) ? null : PaymentPricingService.NormalizeCurrency(request.Currency);
    promoCode.MaxRedemptions = request.MaxRedemptions;
    promoCode.PerUserLimit = request.PerUserLimit;
    promoCode.ValidFromUtc = request.ValidFromUtc;
    promoCode.ExpiresAtUtc = request.ExpiresAtUtc;
    promoCode.AutoDeleteAtUtc = request.AutoDeleteAtUtc;
    promoCode.IsActive = request.IsActive;
    promoCode.DeletedAtUtc = request.IsActive ? null : promoCode.DeletedAtUtc;
    promoCode.UpdatedAtUtc = DateTimeOffset.UtcNow;
    db.OutboxMessages.Add(ProgramEvents.PromoCodeUpdated(promoCode));
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(promoCode.ToResponse());
});

admin.MapDelete("/promo-codes/{id:guid}", async Task<IResult> (
    Guid id,
    PaymentsDbContext db,
    CancellationToken cancellationToken) =>
{
    var promoCode = await db.PromoCodes.FindAsync([id], cancellationToken);

    if (promoCode is null)
    {
        return Results.NotFound();
    }

    promoCode.IsActive = false;
    promoCode.DeletedAtUtc ??= DateTimeOffset.UtcNow;
    promoCode.UpdatedAtUtc = DateTimeOffset.UtcNow;
    db.OutboxMessages.Add(ProgramEvents.PromoCodeDeleted(promoCode));
    await db.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
});

admin.MapGet("/checkouts", async (PaymentsDbContext db, CancellationToken cancellationToken) =>
{
    var checkouts = await db.PaymentCheckouts
        .AsNoTracking()
        .OrderByDescending(checkout => checkout.CreatedAtUtc)
        .Take(200)
        .Select(checkout => checkout.ToResponse())
        .ToListAsync(cancellationToken);

    return Results.Ok(checkouts);
});

app.Run();

static async Task<IResult> ProcessWebhookAsync(
    PaymentProvider provider,
    HttpRequest request,
    PaymentsDbContext db,
    PaymentProviderRegistry providers,
    TimeProvider timeProvider,
    CancellationToken cancellationToken)
{
    ProviderWebhookResult webhook;

    try
    {
        webhook = await providers.Get(provider).NormalizeWebhookAsync(request, cancellationToken);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (Exception ex) when (ex is InvalidOperationException or Stripe.StripeException or KeyNotFoundException or JsonException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    if (await db.PaymentWebhookEvents.AnyAsync(
        candidate => candidate.Provider == webhook.Provider && candidate.ProviderEventId == webhook.ProviderEventId,
        cancellationToken))
    {
        return Results.Ok(new { received = true, duplicate = true });
    }

    var now = timeProvider.GetUtcNow();
    var webhookEvent = new PaymentWebhookEvent
    {
        Provider = webhook.Provider,
        ProviderEventId = webhook.ProviderEventId,
        EventType = webhook.EventType,
        ProviderReference = webhook.ProviderReference,
        Payload = webhook.RawPayload,
        ReceivedAtUtc = now,
        ProcessedAtUtc = now
    };

    db.PaymentWebhookEvents.Add(webhookEvent);

    var checkout = await db.PaymentCheckouts
        .Include(candidate => candidate.PromoReservation)
        .Include(candidate => candidate.AdPackage)
        .SingleOrDefaultAsync(candidate =>
            candidate.Provider == webhook.Provider && candidate.ProviderReference == webhook.ProviderReference,
            cancellationToken);

    if (checkout is null)
    {
        await db.SaveChangesAsync(cancellationToken);
        return Results.Accepted($"/payments/webhooks/{provider.ToString().ToLowerInvariant()}", new { received = true, matched = false });
    }

    if (checkout.Status is PaymentCheckoutStatus.Completed or PaymentCheckoutStatus.Failed or PaymentCheckoutStatus.Canceled)
    {
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { received = true, alreadyProcessed = true });
    }

    if (webhook.IsSuccessful && !WebhookAmountMatches(checkout, webhook))
    {
        checkout.Status = PaymentCheckoutStatus.Failed;
        checkout.FailureReason = "Webhook amount or currency did not match the checkout.";

        if (checkout.PromoReservation is not null)
        {
            checkout.PromoReservation.Status = PromoReservationStatus.Released;
        }

        db.OutboxMessages.Add(ProgramEvents.PaymentFailed(checkout, checkout.FailureReason));
        await db.SaveChangesAsync(cancellationToken);
        return Results.BadRequest(new { error = checkout.FailureReason });
    }

    if (webhook.IsSuccessful)
    {
        checkout.Status = PaymentCheckoutStatus.Completed;
        checkout.CompletedAtUtc = now;
        checkout.FailureReason = null;

        if (checkout.PromoReservation is not null)
        {
            checkout.PromoReservation.Status = PromoReservationStatus.Completed;
            checkout.PromoReservation.CompletedAtUtc = now;
        }

        var durationDays = checkout.AdPackage?.DurationDays ?? 30;
        var entitlement = await CreateEntitlementAsync(db, checkout, durationDays, now, cancellationToken);
        db.OutboxMessages.Add(ProgramEvents.PaymentCompleted(checkout));

        if (entitlement is not null)
        {
            db.OutboxMessages.Add(ProgramEvents.EntitlementCreated(entitlement));
        }
    }
    else
    {
        checkout.Status = PaymentCheckoutStatus.Failed;
        checkout.FailureReason = webhook.FailureReason ?? "Provider reported payment failure.";

        if (checkout.PromoReservation is not null)
        {
            checkout.PromoReservation.Status = PromoReservationStatus.Released;
        }

        db.OutboxMessages.Add(ProgramEvents.PaymentFailed(checkout, checkout.FailureReason));
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { received = true, matched = true });
}

static bool WebhookAmountMatches(PaymentCheckout checkout, ProviderWebhookResult webhook)
{
    if (!webhook.Amount.HasValue || string.IsNullOrWhiteSpace(webhook.Currency))
    {
        return false;
    }

    return PaymentPricingService.RoundMoney(webhook.Amount.Value) == PaymentPricingService.RoundMoney(checkout.FinalAmount)
        && string.Equals(webhook.Currency, checkout.Currency, StringComparison.OrdinalIgnoreCase);
}

static async Task<AdEntitlement?> CreateEntitlementAsync(
    PaymentsDbContext db,
    PaymentCheckout checkout,
    int durationDays,
    DateTimeOffset now,
    CancellationToken cancellationToken)
{
    if (checkout.Purpose != PaymentPurpose.ListingAd || !checkout.ListingId.HasValue)
    {
        return null;
    }

    if (await db.AdEntitlements.AnyAsync(
        entitlement => entitlement.PaymentCheckoutId == checkout.Id,
        cancellationToken))
    {
        return null;
    }

    var entitlement = new AdEntitlement
    {
        UserId = checkout.UserId,
        ListingId = checkout.ListingId.Value,
        PaymentCheckoutId = checkout.Id,
        AdPackageId = checkout.AdPackageId,
        StartsAtUtc = now,
        ExpiresAtUtc = now.AddDays(Math.Max(1, durationDays)),
        IsActive = true
    };

    db.AdEntitlements.Add(entitlement);
    return entitlement;
}

static string? ValidateAdPackage(AdminAdPackageRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120)
    {
        return "Name is required and must be 120 characters or fewer.";
    }

    if (request.Price < 0)
    {
        return "Price must be zero or greater.";
    }

    if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Trim().Length != 3)
    {
        return "Currency must be a three-letter ISO currency code.";
    }

    if (request.DurationDays <= 0)
    {
        return "Duration days must be greater than zero.";
    }

    return null;
}

static string? ValidatePromoCode(AdminPromoCodeRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 64)
    {
        return "Promo code is required and must be 64 characters or fewer.";
    }

    if (request.DiscountType == PromoDiscountType.Percent
        && (!request.PercentOff.HasValue || request.PercentOff <= 0 || request.PercentOff > 100))
    {
        return "Percent promo codes require a percent value between 0 and 100.";
    }

    if (request.DiscountType == PromoDiscountType.FixedAmount
        && (!request.AmountOff.HasValue || request.AmountOff <= 0))
    {
        return "Fixed amount promo codes require an amount greater than zero.";
    }

    if (!string.IsNullOrWhiteSpace(request.Currency) && request.Currency.Trim().Length != 3)
    {
        return "Currency must be a three-letter ISO currency code when supplied.";
    }

    if (request.MaxRedemptions.HasValue && request.MaxRedemptions <= 0)
    {
        return "Max redemptions must be greater than zero when supplied.";
    }

    if (request.PerUserLimit < 0)
    {
        return "Per-user limit cannot be negative.";
    }

    if (request.ExpiresAtUtc <= request.ValidFromUtc)
    {
        return "Expiry date must be after the valid-from date.";
    }

    if (request.AutoDeleteAtUtc.HasValue && request.AutoDeleteAtUtc < request.ExpiresAtUtc)
    {
        return "Auto-delete date must be on or after the expiry date.";
    }

    return null;
}

public partial class Program;
