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
        NotificationRequested
    ];

    public static string DeadLetter(string topic) => $"{topic}.dlq";
}
