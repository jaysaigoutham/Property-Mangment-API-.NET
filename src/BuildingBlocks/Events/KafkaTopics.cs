namespace BuildingBlocks.Events;

public static class KafkaTopics
{
    public const string UserRegistered = "user.registered";
    public const string ListingCreated = "listing.created";
    public const string ListingUpdated = "listing.updated";
    public const string ListingApproved = "listing.approved";
    public const string ListingRejected = "listing.rejected";
    public const string InquiryCreated = "inquiry.created";
    public const string ReviewCreated = "review.created";
    public const string FavoriteCreated = "favorite.created";
    public const string NotificationRequested = "notification.requested";
    public const string PaymentCheckoutCreated = "payment.checkout.created";
    public const string PaymentCompleted = "payment.completed";
    public const string PaymentFailed = "payment.failed";
    public const string PromoCodeCreated = "promo-code.created";
    public const string PromoCodeUpdated = "promo-code.updated";
    public const string PromoCodeDeleted = "promo-code.deleted";
    public const string AdEntitlementCreated = "ad.entitlement.created";

    public static readonly string[] All =
    [
        UserRegistered,
        ListingCreated,
        ListingUpdated,
        ListingApproved,
        ListingRejected,
        InquiryCreated,
        ReviewCreated,
        FavoriteCreated,
        NotificationRequested,
        PaymentCheckoutCreated,
        PaymentCompleted,
        PaymentFailed,
        PromoCodeCreated,
        PromoCodeUpdated,
        PromoCodeDeleted,
        AdEntitlementCreated
    ];

    public static string DeadLetter(string topic) => $"{topic}.dlq";
}
