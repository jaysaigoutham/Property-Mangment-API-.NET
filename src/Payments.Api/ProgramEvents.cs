using BuildingBlocks.Events;

namespace Payments.Api;

public static class ProgramEvents
{
    private const string Producer = "payments-api";

    public static OutboxMessage CheckoutCreated(PaymentCheckout checkout) =>
        OutboxMessage.Create(
            KafkaTopics.PaymentCheckoutCreated,
            KafkaTopics.PaymentCheckoutCreated,
            Producer,
            new PaymentCheckoutCreatedEvent(
                checkout.Id,
                checkout.UserId,
                checkout.ListingId,
                checkout.AdPackageId,
                checkout.Provider,
                checkout.FinalAmount,
                checkout.Currency),
            checkout.Id.ToString());

    public static OutboxMessage PaymentCompleted(PaymentCheckout checkout) =>
        OutboxMessage.Create(
            KafkaTopics.PaymentCompleted,
            KafkaTopics.PaymentCompleted,
            Producer,
            new PaymentCompletedEvent(
                checkout.Id,
                checkout.UserId,
                checkout.ListingId,
                checkout.AdPackageId,
                checkout.FinalAmount,
                checkout.Currency,
                checkout.ProviderReference),
            checkout.Id.ToString());

    public static OutboxMessage PaymentFailed(PaymentCheckout checkout, string reason) =>
        OutboxMessage.Create(
            KafkaTopics.PaymentFailed,
            KafkaTopics.PaymentFailed,
            Producer,
            new PaymentFailedEvent(
                checkout.Id,
                checkout.UserId,
                checkout.ListingId,
                checkout.AdPackageId,
                reason,
                checkout.ProviderReference),
            checkout.Id.ToString());

    public static OutboxMessage EntitlementCreated(AdEntitlement entitlement) =>
        OutboxMessage.Create(
            KafkaTopics.AdEntitlementCreated,
            KafkaTopics.AdEntitlementCreated,
            Producer,
            new AdEntitlementCreatedEvent(
                entitlement.Id,
                entitlement.UserId,
                entitlement.ListingId,
                entitlement.PaymentCheckoutId,
                entitlement.AdPackageId,
                entitlement.ExpiresAtUtc),
            entitlement.ListingId.ToString());

    public static OutboxMessage PromoCodeCreated(PromoCode promoCode) =>
        PromoCodeChanged(KafkaTopics.PromoCodeCreated, promoCode);

    public static OutboxMessage PromoCodeUpdated(PromoCode promoCode) =>
        PromoCodeChanged(KafkaTopics.PromoCodeUpdated, promoCode);

    public static OutboxMessage PromoCodeDeleted(PromoCode promoCode) =>
        PromoCodeChanged(KafkaTopics.PromoCodeDeleted, promoCode);

    private static OutboxMessage PromoCodeChanged(string topic, PromoCode promoCode) =>
        OutboxMessage.Create(
            topic,
            topic,
            Producer,
            new PromoCodeChangedEvent(promoCode.Id, promoCode.Code),
            promoCode.Id.ToString());
}
