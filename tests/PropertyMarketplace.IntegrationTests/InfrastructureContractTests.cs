using DotNet.Testcontainers.Builders;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace PropertyMarketplace.IntegrationTests;

public sealed class InfrastructureContractTests
{
    [Fact]
    public void Docker_compose_declares_required_production_infrastructure()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "docker-compose.yml"));

        Assert.Contains("postgres:17-alpine", compose);
        Assert.Contains("redis:7-alpine", compose);
        Assert.Contains("apache/kafka:4.0.0", compose);
        Assert.Contains("minio/minio", compose);
        Assert.Contains("KAFKA_PROCESS_ROLES: broker,controller", compose);
    }

    [Theory]
    [InlineData("user.registered")]
    [InlineData("listing.created")]
    [InlineData("listing.updated")]
    [InlineData("listing.approved")]
    [InlineData("listing.rejected")]
    [InlineData("inquiry.created")]
    [InlineData("review.created")]
    [InlineData("favorite.created")]
    [InlineData("notification.requested")]
    public void Docker_compose_creates_required_kafka_topics(string topic)
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "docker-compose.yml"));

        Assert.Contains($"--topic {topic}", compose);
    }

    [Fact]
    public void Testcontainers_modules_are_available_for_real_integration_tests()
    {
        Assert.Equal("Testcontainers.PostgreSql.PostgreSqlBuilder", typeof(PostgreSqlBuilder).FullName);
        Assert.Equal("Testcontainers.Redis.RedisBuilder", typeof(RedisBuilder).FullName);
        Assert.Equal("Testcontainers.Kafka.KafkaBuilder", typeof(KafkaBuilder).FullName);
        Assert.Equal("DotNet.Testcontainers.Builders.ContainerBuilder", typeof(ContainerBuilder).FullName);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
