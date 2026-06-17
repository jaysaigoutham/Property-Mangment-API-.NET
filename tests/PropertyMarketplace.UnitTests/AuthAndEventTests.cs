using System.Text.Json;
using BuildingBlocks.Auth;
using BuildingBlocks.Events;
using BuildingBlocks.Kafka;
using Listings.Api;
using Microsoft.Extensions.Options;

namespace PropertyMarketplace.UnitTests;

public sealed class AuthAndEventTests
{
    [Fact]
    public void Known_roles_are_normalized()
    {
        Assert.Equal(AppRoles.Agent, AppRoles.Normalize(" Agent "));
        Assert.True(AppRoles.IsKnown("ADMIN"));
        Assert.False(AppRoles.IsKnown("tenant"));
    }

    [Fact]
    public void Refresh_tokens_are_hashed_and_verified()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            SigningKey = "unit-test-signing-key-that-is-long-enough-32",
            Issuer = "tests",
            Audience = "tests"
        }), TimeProvider.System);

        var refreshToken = service.CreateRefreshToken();
        var hash = service.HashRefreshToken(refreshToken);

        Assert.NotEqual(refreshToken, hash);
        Assert.True(service.VerifyRefreshToken(refreshToken, hash));
        Assert.False(service.VerifyRefreshToken(refreshToken + "x", hash));
    }

    [Fact]
    public void Outbox_message_wraps_payload_in_event_envelope()
    {
        var message = OutboxMessage.Create(
            KafkaTopics.ListingCreated,
            KafkaTopics.ListingCreated,
            "unit-tests",
            new { ListingId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") },
            "listing-key");

        using var document = JsonDocument.Parse(message.Payload);

        Assert.Equal(KafkaTopics.ListingCreated, message.Topic);
        Assert.Equal("listing-key", message.Key);
        Assert.Equal(KafkaTopics.ListingCreated, document.RootElement.GetProperty("Type").GetString());
        Assert.Equal("unit-tests", document.RootElement.GetProperty("Producer").GetString());
        Assert.True(document.RootElement.TryGetProperty("Payload", out _));
    }
}

public sealed class ListingValidationTests
{
    [Fact]
    public void Listing_validation_accepts_a_complete_listing()
    {
        var request = new CreateListingRequest(
            "Modern apartment",
            "Close to transit and schools.",
            "Colombo",
            "Western",
            "Sri Lanka",
            "Main Street",
            250000m,
            3,
            2,
            120,
            PropertyType.Apartment,
            ["pool", "parking"]);

        Assert.Null(ListingValidation.Validate(request));
    }

    [Fact]
    public void Listing_validation_rejects_invalid_price_and_missing_location()
    {
        var request = new CreateListingRequest(
            "Bad listing",
            "No useful data",
            "",
            "",
            "",
            "",
            0m,
            2,
            1,
            100,
            PropertyType.House,
            []);

        Assert.NotNull(ListingValidation.Validate(request));
    }
}
