namespace BuildingBlocks.Caching;

public static class CacheKeys
{
    public const string ListingSearchPrefix = "listings:search:";
    public const string ListingDetailPrefix = "listings:detail:";
    public const string AgentProfilePrefix = "agents:profile:";

    public static string ListingDetail(Guid listingId) => $"{ListingDetailPrefix}{listingId:N}";
}
