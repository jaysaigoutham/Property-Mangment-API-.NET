using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Payments.Api;

public sealed class StripePaymentOptions
{
    public const string SectionName = "Payments:Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}

public sealed class PayHerePaymentOptions
{
    public const string SectionName = "Payments:PayHere";

    public string MerchantId { get; set; } = string.Empty;
    public string MerchantSecret { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = "https://sandbox.payhere.lk/pay/checkout";
    public string NotifyUrl { get; set; } = string.Empty;
}

public sealed record ProviderCheckoutResult(
    string ProviderReference,
    string CheckoutUrl,
    IReadOnlyDictionary<string, string>? FormFields);

public sealed record ProviderWebhookResult(
    PaymentProvider Provider,
    string ProviderReference,
    string ProviderEventId,
    string EventType,
    bool IsSuccessful,
    decimal? Amount,
    string? Currency,
    string RawPayload,
    string? FailureReason);

public interface IPaymentProvider
{
    PaymentProvider Provider { get; }

    Task<ProviderCheckoutResult> CreateCheckoutAsync(
        PaymentCheckout checkout,
        AdPackage package,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken);

    Task<ProviderWebhookResult> NormalizeWebhookAsync(HttpRequest request, CancellationToken cancellationToken);
}

public sealed class PaymentProviderRegistry(IEnumerable<IPaymentProvider> providers)
{
    private readonly Dictionary<PaymentProvider, IPaymentProvider> _providers = providers.ToDictionary(provider => provider.Provider);

    public IPaymentProvider Get(PaymentProvider provider) =>
        _providers.TryGetValue(provider, out var implementation)
            ? implementation
            : throw new InvalidOperationException($"Payment provider '{provider}' is not registered.");
}

public sealed record PaymentPricingResult(
    bool IsValid,
    string? Error,
    AdPackage? Package,
    PromoCode? PromoCode,
    decimal OriginalAmount,
    decimal DiscountAmount,
    decimal FinalAmount,
    string Currency);

public sealed class PaymentPricingService(PaymentsDbContext db, TimeProvider timeProvider)
{
    public async Task<PaymentPricingResult> PreviewAsync(
        Guid userId,
        Guid adPackageId,
        string? promoCode,
        CancellationToken cancellationToken)
    {
        var currency = string.Empty;
        var package = await db.AdPackages
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == adPackageId && candidate.IsActive, cancellationToken);

        if (package is null)
        {
            return Invalid("Ad package was not found or is inactive.");
        }

        var originalAmount = RoundMoney(package.Price);
        currency = NormalizeCurrency(package.Currency);

        if (string.IsNullOrWhiteSpace(promoCode))
        {
            return new(true, null, package, null, originalAmount, 0, originalAmount, currency);
        }

        var normalizedCode = NormalizeCode(promoCode);
        var promo = await db.PromoCodes
            .AsNoTracking()
            .SingleOrDefaultAsync(code => code.Code == normalizedCode && code.DeletedAtUtc == null, cancellationToken);

        if (promo is null)
        {
            return Invalid("Promo code was not found.");
        }

        var now = timeProvider.GetUtcNow();

        if (!promo.IsActive || promo.ValidFromUtc > now || promo.ExpiresAtUtc <= now)
        {
            return Invalid("Promo code is not currently valid.");
        }

        if (!string.IsNullOrWhiteSpace(promo.Currency)
            && !string.Equals(NormalizeCurrency(promo.Currency), currency, StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("Promo code currency does not match the selected ad package.");
        }

        var completedRedemptions = await db.PromoReservations.CountAsync(
            reservation => reservation.PromoCodeId == promo.Id && reservation.Status == PromoReservationStatus.Completed,
            cancellationToken);
        var activeReservations = await db.PromoReservations.CountAsync(
            reservation => reservation.PromoCodeId == promo.Id
                && reservation.Status == PromoReservationStatus.Reserved
                && reservation.ExpiresAtUtc > now,
            cancellationToken);

        if (promo.MaxRedemptions.HasValue && completedRedemptions + activeReservations >= promo.MaxRedemptions)
        {
            return Invalid("Promo code has reached its redemption limit.");
        }

        var userCompletedRedemptions = await db.PromoReservations.CountAsync(
            reservation => reservation.PromoCodeId == promo.Id
                && reservation.UserId == userId
                && reservation.Status == PromoReservationStatus.Completed,
            cancellationToken);
        var userActiveReservations = await db.PromoReservations.CountAsync(
            reservation => reservation.PromoCodeId == promo.Id
                && reservation.UserId == userId
                && reservation.Status == PromoReservationStatus.Reserved
                && reservation.ExpiresAtUtc > now,
            cancellationToken);

        if (promo.PerUserLimit > 0 && userCompletedRedemptions + userActiveReservations >= promo.PerUserLimit)
        {
            return Invalid("Promo code has reached its per-user limit.");
        }

        var discount = CalculateDiscount(originalAmount, promo);
        return new(true, null, package, promo, originalAmount, discount, RoundMoney(originalAmount - discount), currency);

        PaymentPricingResult Invalid(string error) => new(false, error, package, null, 0, 0, 0, currency);
    }

    private static decimal CalculateDiscount(decimal originalAmount, PromoCode promo)
    {
        var discount = promo.DiscountType switch
        {
            PromoDiscountType.Percent => originalAmount * (promo.PercentOff.GetValueOrDefault() / 100m),
            PromoDiscountType.FixedAmount => promo.AmountOff.GetValueOrDefault(),
            _ => 0
        };

        return Math.Min(originalAmount, Math.Max(0, RoundMoney(discount)));
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static string NormalizeCurrency(string currency) => currency.Trim().ToUpperInvariant();

    public static decimal RoundMoney(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

public sealed class StripeCheckoutProvider(IOptions<StripePaymentOptions> options) : IPaymentProvider
{
    private readonly StripePaymentOptions _options = options.Value;

    public PaymentProvider Provider => PaymentProvider.Stripe;

    public async Task<ProviderCheckoutResult> CreateCheckoutAsync(
        PaymentCheckout checkout,
        AdPackage package,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException("Stripe secret key is not configured.");
        }

        var service = new SessionService(new StripeClient(_options.SecretKey));
        var session = await service.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = checkout.Id.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["checkoutId"] = checkout.Id.ToString(),
                ["listingId"] = checkout.ListingId?.ToString() ?? string.Empty,
                ["purpose"] = checkout.Purpose.ToString()
            },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = checkout.Currency.ToLowerInvariant(),
                        UnitAmount = ToMinorUnits(checkout.FinalAmount),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = package.Name,
                            Description = package.Description
                        }
                    }
                }
            ]
        }, cancellationToken: cancellationToken);

        return new ProviderCheckoutResult(session.Id, session.Url ?? string.Empty, null);
    }

    public async Task<ProviderWebhookResult> NormalizeWebhookAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            throw new InvalidOperationException("Stripe webhook secret is not configured.");
        }

        var signature = request.Headers["Stripe-Signature"].ToString();

        if (string.IsNullOrWhiteSpace(signature))
        {
            throw new UnauthorizedAccessException("Stripe signature header is missing.");
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var stripeEvent = EventUtility.ConstructEvent(payload, signature, _options.WebhookSecret);
        using var json = JsonDocument.Parse(payload);
        var session = json.RootElement.GetProperty("data").GetProperty("object");
        var providerReference = session.GetProperty("id").GetString() ?? string.Empty;
        var paymentStatus = session.TryGetProperty("payment_status", out var paymentStatusElement)
            ? paymentStatusElement.GetString()
            : null;
        var amount = session.TryGetProperty("amount_total", out var amountElement)
            ? FromMinorUnits(amountElement.GetInt64())
            : (decimal?)null;
        var currency = session.TryGetProperty("currency", out var currencyElement)
            ? currencyElement.GetString()?.ToUpperInvariant()
            : null;
        var successful = string.Equals(stripeEvent.Type, "checkout.session.completed", StringComparison.OrdinalIgnoreCase)
            && string.Equals(paymentStatus, "paid", StringComparison.OrdinalIgnoreCase);
        var failed = stripeEvent.Type.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || stripeEvent.Type.Contains("failed", StringComparison.OrdinalIgnoreCase);

        return new ProviderWebhookResult(
            Provider,
            providerReference,
            stripeEvent.Id,
            stripeEvent.Type,
            successful,
            amount,
            currency,
            payload,
            failed ? stripeEvent.Type : null);
    }

    private static long ToMinorUnits(decimal amount) => decimal.ToInt64(PaymentPricingService.RoundMoney(amount) * 100m);

    private static decimal FromMinorUnits(long amount) => PaymentPricingService.RoundMoney(amount / 100m);
}

public sealed class PayHereCheckoutProvider(IOptions<PayHerePaymentOptions> options) : IPaymentProvider
{
    private readonly PayHerePaymentOptions _options = options.Value;

    public PaymentProvider Provider => PaymentProvider.PayHere;

    public Task<ProviderCheckoutResult> CreateCheckoutAsync(
        PaymentCheckout checkout,
        AdPackage package,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.MerchantId) || string.IsNullOrWhiteSpace(_options.MerchantSecret))
        {
            throw new InvalidOperationException("PayHere merchant id and secret are not configured.");
        }

        var orderId = checkout.Id.ToString("N");
        var amount = FormatAmount(checkout.FinalAmount);
        var fields = new Dictionary<string, string>
        {
            ["merchant_id"] = _options.MerchantId,
            ["return_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["notify_url"] = _options.NotifyUrl,
            ["order_id"] = orderId,
            ["items"] = package.Name,
            ["currency"] = checkout.Currency,
            ["amount"] = amount,
            ["custom_1"] = checkout.ListingId?.ToString() ?? string.Empty,
            ["custom_2"] = checkout.Id.ToString(),
            ["hash"] = ComputeCheckoutHash(_options.MerchantId, orderId, amount, checkout.Currency, _options.MerchantSecret)
        };

        return Task.FromResult(new ProviderCheckoutResult(orderId, _options.CheckoutUrl, fields));
    }

    public async Task<ProviderWebhookResult> NormalizeWebhookAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.MerchantId) || string.IsNullOrWhiteSpace(_options.MerchantSecret))
        {
            throw new InvalidOperationException("PayHere merchant id and secret are not configured.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var merchantId = form["merchant_id"].ToString();
        var orderId = form["order_id"].ToString();
        var payHereAmount = form["payhere_amount"].ToString();
        var currency = form["payhere_currency"].ToString();
        var statusCode = form["status_code"].ToString();
        var receivedSignature = form["md5sig"].ToString();
        var paymentId = form["payment_id"].ToString();

        if (!string.Equals(merchantId, _options.MerchantId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("PayHere merchant id mismatch.");
        }

        var expectedSignature = ComputeNotificationHash(merchantId, orderId, payHereAmount, currency, statusCode, _options.MerchantSecret);

        if (!string.Equals(receivedSignature, expectedSignature, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Invalid PayHere notification signature.");
        }

        var rawPayload = JsonSerializer.Serialize(form.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()));
        var successful = statusCode == "2";
        var amount = decimal.TryParse(payHereAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedAmount)
            ? PaymentPricingService.RoundMoney(parsedAmount)
            : (decimal?)null;
        var eventId = string.IsNullOrWhiteSpace(paymentId)
            ? $"{orderId}:{statusCode}"
            : paymentId;

        return new ProviderWebhookResult(
            Provider,
            orderId,
            eventId,
            $"payhere.status.{statusCode}",
            successful,
            amount,
            currency.ToUpperInvariant(),
            rawPayload,
            successful ? null : $"PayHere status {statusCode}");
    }

    private static string FormatAmount(decimal amount) =>
        PaymentPricingService.RoundMoney(amount).ToString("0.00", CultureInfo.InvariantCulture);

    private static string ComputeCheckoutHash(string merchantId, string orderId, string amount, string currency, string secret) =>
        Md5Upper($"{merchantId}{orderId}{amount}{currency}{Md5Upper(secret)}");

    private static string ComputeNotificationHash(
        string merchantId,
        string orderId,
        string amount,
        string currency,
        string statusCode,
        string secret) =>
        Md5Upper($"{merchantId}{orderId}{amount}{currency}{statusCode}{Md5Upper(secret)}");

    private static string Md5Upper(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToUpperInvariant();
    }
}

public sealed class PaymentSeedData(IServiceScopeFactory scopeFactory, ILogger<PaymentSeedData> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        try
        {
            if (await db.AdPackages.AnyAsync(cancellationToken))
            {
                return;
            }

            db.AdPackages.AddRange(
                new AdPackage
                {
                    Name = "Standard Listing Ad",
                    Description = "Publishes one property listing for 30 days.",
                    Price = 25,
                    Currency = "USD",
                    DurationDays = 30,
                    DisplayOrder = 10
                },
                new AdPackage
                {
                    Name = "Featured Listing Ad",
                    Description = "Publishes one property listing for 60 days.",
                    Price = 60,
                    Currency = "USD",
                    DurationDays = 60,
                    DisplayOrder = 20
                });

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Payment seed data could not be created.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class PromoCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PromoCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupAsync(stoppingToken);
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var now = timeProvider.GetUtcNow();

            var expiredReservations = await db.PromoReservations
                .Where(reservation => reservation.Status == PromoReservationStatus.Reserved && reservation.ExpiresAtUtc <= now)
                .ToListAsync(cancellationToken);

            foreach (var reservation in expiredReservations)
            {
                reservation.Status = PromoReservationStatus.Released;
            }

            var codesToArchive = await db.PromoCodes
                .Where(code => code.DeletedAtUtc == null && code.AutoDeleteAtUtc <= now)
                .ToListAsync(cancellationToken);

            foreach (var code in codesToArchive)
            {
                code.IsActive = false;
                code.DeletedAtUtc = now;
                code.UpdatedAtUtc = now;
                db.OutboxMessages.Add(ProgramEvents.PromoCodeDeleted(code));
            }

            if (expiredReservations.Count > 0 || codesToArchive.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Expired payment cleanup failed.");
        }
    }
}
