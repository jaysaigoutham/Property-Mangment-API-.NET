using BuildingBlocks.Events;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Payments.Api;

public enum PaymentPurpose
{
    ListingAd = 0
}

public enum PaymentProvider
{
    Stripe = 0,
    PayHere = 1
}

public enum PaymentCheckoutStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Canceled = 3,
    Expired = 4
}

public enum PromoDiscountType
{
    Percent = 0,
    FixedAmount = 1
}

public enum PromoReservationStatus
{
    Reserved = 0,
    Completed = 1,
    Released = 2
}

public sealed class AdPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int DurationDays { get; set; } = 30;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PromoCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PromoDiscountType DiscountType { get; set; }
    public decimal? PercentOff { get; set; }
    public decimal? AmountOff { get; set; }
    public string? Currency { get; set; }
    public int? MaxRedemptions { get; set; }
    public int PerUserLimit { get; set; } = 1;
    public DateTimeOffset ValidFromUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddMonths(1);
    public DateTimeOffset? AutoDeleteAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PromoReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PromoCodeId { get; set; }
    public PromoCode? PromoCode { get; set; }
    public Guid UserId { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public PromoReservationStatus Status { get; set; } = PromoReservationStatus.Reserved;
    public DateTimeOffset ReservedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddMinutes(15);
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class PaymentCheckout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PaymentPurpose Purpose { get; set; } = PaymentPurpose.ListingAd;
    public PaymentProvider Provider { get; set; }
    public PaymentCheckoutStatus Status { get; set; } = PaymentCheckoutStatus.Pending;
    public Guid UserId { get; set; }
    public Guid? ListingId { get; set; }
    public Guid AdPackageId { get; set; }
    public AdPackage? AdPackage { get; set; }
    public Guid? PromoCodeId { get; set; }
    public PromoCode? PromoCode { get; set; }
    public Guid? PromoReservationId { get; set; }
    public PromoReservation? PromoReservation { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? ProviderReference { get; set; }
    public string? ProviderCheckoutUrl { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddMinutes(30);
}

public sealed class AdEntitlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ListingId { get; set; }
    public Guid PaymentCheckoutId { get; set; }
    public Guid AdPackageId { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class PaymentWebhookEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PaymentProvider Provider { get; set; }
    public string ProviderEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ProviderReference { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAtUtc { get; set; }
}

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<AdPackage> AdPackages => Set<AdPackage>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<PromoReservation> PromoReservations => Set<PromoReservation>();
    public DbSet<PaymentCheckout> PaymentCheckouts => Set<PaymentCheckout>();
    public DbSet<AdEntitlement> AdEntitlements => Set<AdEntitlement>();
    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");

        modelBuilder.Entity<AdPackage>(builder =>
        {
            builder.ToTable("ad_packages");
            builder.HasKey(package => package.Id);
            builder.Property(package => package.Name).HasMaxLength(120).IsRequired();
            builder.Property(package => package.Description).HasMaxLength(500);
            builder.Property(package => package.Price).HasPrecision(18, 2);
            builder.Property(package => package.Currency).HasMaxLength(3).IsRequired();
            builder.HasIndex(package => new { package.IsActive, package.DisplayOrder });
        });

        modelBuilder.Entity<PromoCode>(builder =>
        {
            builder.ToTable("promo_codes");
            builder.HasKey(code => code.Id);
            builder.Property(code => code.Code).HasMaxLength(64).IsRequired();
            builder.Property(code => code.Description).HasMaxLength(500);
            builder.Property(code => code.PercentOff).HasPrecision(5, 2);
            builder.Property(code => code.AmountOff).HasPrecision(18, 2);
            builder.Property(code => code.Currency).HasMaxLength(3);
            builder.HasIndex(code => code.Code).IsUnique();
            builder.HasIndex(code => new { code.IsActive, code.ExpiresAtUtc, code.AutoDeleteAtUtc });
        });

        modelBuilder.Entity<PromoReservation>(builder =>
        {
            builder.ToTable("promo_reservations");
            builder.HasKey(reservation => reservation.Id);
            builder.Property(reservation => reservation.DiscountAmount).HasPrecision(18, 2);
            builder.Property(reservation => reservation.Currency).HasMaxLength(3).IsRequired();
            builder.HasIndex(reservation => new { reservation.PromoCodeId, reservation.Status, reservation.ExpiresAtUtc });
            builder.HasIndex(reservation => new { reservation.UserId, reservation.PromoCodeId, reservation.Status });
        });

        modelBuilder.Entity<PaymentCheckout>(builder =>
        {
            builder.ToTable("payment_checkouts");
            builder.HasKey(checkout => checkout.Id);
            builder.Property(checkout => checkout.OriginalAmount).HasPrecision(18, 2);
            builder.Property(checkout => checkout.DiscountAmount).HasPrecision(18, 2);
            builder.Property(checkout => checkout.FinalAmount).HasPrecision(18, 2);
            builder.Property(checkout => checkout.Currency).HasMaxLength(3).IsRequired();
            builder.Property(checkout => checkout.ProviderReference).HasMaxLength(200);
            builder.Property(checkout => checkout.ProviderCheckoutUrl).HasMaxLength(1000);
            builder.Property(checkout => checkout.FailureReason).HasMaxLength(1000);
            builder.HasOne(checkout => checkout.PromoReservation)
                .WithMany()
                .HasForeignKey(checkout => checkout.PromoReservationId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(checkout => checkout.ProviderReference);
            builder.HasIndex(checkout => new { checkout.UserId, checkout.Status, checkout.CreatedAtUtc });
            builder.HasIndex(checkout => new { checkout.ListingId, checkout.Purpose, checkout.Status });
        });

        modelBuilder.Entity<AdEntitlement>(builder =>
        {
            builder.ToTable("ad_entitlements");
            builder.HasKey(entitlement => entitlement.Id);
            builder.HasIndex(entitlement => new { entitlement.ListingId, entitlement.UserId, entitlement.IsActive, entitlement.ExpiresAtUtc });
            builder.HasIndex(entitlement => entitlement.PaymentCheckoutId).IsUnique();
        });

        modelBuilder.Entity<PaymentWebhookEvent>(builder =>
        {
            builder.ToTable("payment_webhook_events");
            builder.HasKey(webhook => webhook.Id);
            builder.Property(webhook => webhook.ProviderEventId).HasMaxLength(220).IsRequired();
            builder.Property(webhook => webhook.EventType).HasMaxLength(200).IsRequired();
            builder.Property(webhook => webhook.ProviderReference).HasMaxLength(220).IsRequired();
            builder.Property(webhook => webhook.Payload).HasColumnType("jsonb").IsRequired();
            builder.HasIndex(webhook => new { webhook.Provider, webhook.ProviderEventId }).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>().ConfigureOutbox();
    }
}

public sealed record CheckoutPreviewRequest(Guid ListingId, Guid AdPackageId, string? PromoCode);

public sealed record CreateCheckoutRequest(
    Guid ListingId,
    Guid AdPackageId,
    PaymentProvider Provider,
    string? PromoCode,
    string SuccessUrl,
    string CancelUrl);

public sealed record PaymentPreviewResponse(
    Guid AdPackageId,
    string PackageName,
    decimal OriginalAmount,
    decimal DiscountAmount,
    decimal FinalAmount,
    string Currency,
    string? PromoCode,
    string? Message);

public sealed record PaymentCheckoutResponse(
    Guid Id,
    PaymentPurpose Purpose,
    PaymentProvider Provider,
    PaymentCheckoutStatus Status,
    Guid UserId,
    Guid? ListingId,
    Guid AdPackageId,
    Guid? PromoCodeId,
    decimal OriginalAmount,
    decimal DiscountAmount,
    decimal FinalAmount,
    string Currency,
    string? ProviderReference,
    string? CheckoutUrl,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record ProviderCheckoutResponse(
    PaymentCheckoutResponse Checkout,
    string? RedirectUrl,
    IReadOnlyDictionary<string, string>? FormFields);

public sealed record AdPackageResponse(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    int DurationDays,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PromoCodeResponse(
    Guid Id,
    string Code,
    string Description,
    PromoDiscountType DiscountType,
    decimal? PercentOff,
    decimal? AmountOff,
    string? Currency,
    int? MaxRedemptions,
    int PerUserLimit,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? AutoDeleteAtUtc,
    bool IsActive,
    DateTimeOffset? DeletedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AdminAdPackageRequest(
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    int DurationDays,
    bool IsActive,
    int DisplayOrder);

public sealed record AdminPromoCodeRequest(
    string Code,
    string? Description,
    PromoDiscountType DiscountType,
    decimal? PercentOff,
    decimal? AmountOff,
    string? Currency,
    int? MaxRedemptions,
    int PerUserLimit,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? AutoDeleteAtUtc,
    bool IsActive);

public sealed record ActiveEntitlementResponse(bool IsActive, Guid? EntitlementId, DateTimeOffset? ExpiresAtUtc);

public sealed record PaymentCheckoutCreatedEvent(
    Guid CheckoutId,
    Guid UserId,
    Guid? ListingId,
    Guid AdPackageId,
    PaymentProvider Provider,
    decimal FinalAmount,
    string Currency);

public sealed record PaymentCompletedEvent(
    Guid CheckoutId,
    Guid UserId,
    Guid? ListingId,
    Guid AdPackageId,
    decimal FinalAmount,
    string Currency,
    string? ProviderReference);

public sealed record PaymentFailedEvent(
    Guid CheckoutId,
    Guid UserId,
    Guid? ListingId,
    Guid AdPackageId,
    string Reason,
    string? ProviderReference);

public sealed record AdEntitlementCreatedEvent(
    Guid EntitlementId,
    Guid UserId,
    Guid ListingId,
    Guid CheckoutId,
    Guid AdPackageId,
    DateTimeOffset ExpiresAtUtc);

public sealed record PromoCodeChangedEvent(Guid PromoCodeId, string Code);

public static class PaymentMapping
{
    public static AdPackageResponse ToResponse(this AdPackage package) =>
        new(
            package.Id,
            package.Name,
            package.Description,
            package.Price,
            package.Currency,
            package.DurationDays,
            package.IsActive,
            package.DisplayOrder,
            package.CreatedAtUtc,
            package.UpdatedAtUtc);

    public static PromoCodeResponse ToResponse(this PromoCode promoCode) =>
        new(
            promoCode.Id,
            promoCode.Code,
            promoCode.Description,
            promoCode.DiscountType,
            promoCode.PercentOff,
            promoCode.AmountOff,
            promoCode.Currency,
            promoCode.MaxRedemptions,
            promoCode.PerUserLimit,
            promoCode.ValidFromUtc,
            promoCode.ExpiresAtUtc,
            promoCode.AutoDeleteAtUtc,
            promoCode.IsActive,
            promoCode.DeletedAtUtc,
            promoCode.CreatedAtUtc,
            promoCode.UpdatedAtUtc);

    public static PaymentCheckoutResponse ToResponse(this PaymentCheckout checkout) =>
        new(
            checkout.Id,
            checkout.Purpose,
            checkout.Provider,
            checkout.Status,
            checkout.UserId,
            checkout.ListingId,
            checkout.AdPackageId,
            checkout.PromoCodeId,
            checkout.OriginalAmount,
            checkout.DiscountAmount,
            checkout.FinalAmount,
            checkout.Currency,
            checkout.ProviderReference,
            checkout.ProviderCheckoutUrl,
            checkout.CreatedAtUtc,
            checkout.CompletedAtUtc,
            checkout.ExpiresAtUtc);
}
