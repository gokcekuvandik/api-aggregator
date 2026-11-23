using DotNet.Testcontainers.Builders;
using StackExchange.Redis;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Aggregator.Infrastructure.IntegrationTests.RedisFixture))]

namespace Aggregator.Infrastructure.IntegrationTests;

/// <summary>
/// One Redis container for the whole run, on the image Aspire uses for scale-out.
/// Without Docker the tests are skipped locally but fail in CI, so the pipeline never passes them silently.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    public const string Image = "redis:8.10";

    private RedisContainer? container;
    private ConnectionMultiplexer? admin;
    private string? unavailableReason;

    public string ConnectionString => Started().GetConnectionString();

    public IServer Server => admin!.GetServers().Single();

    public async ValueTask InitializeAsync()
    {
        try
        {
            container = new RedisBuilder(Image).Build();
            await container.StartAsync();
            admin = await ConnectionMultiplexer.ConnectAsync($"{container.GetConnectionString()},allowAdmin=true");
        }
        catch (DockerUnavailableException exception)
        {
            unavailableReason = exception.Message;
        }
    }

    /// <summary>Skips the calling test when Docker is missing outside CI, and empties Redis so each test starts clean.</summary>
    public async Task ResetAsync()
    {
        if (unavailableReason is not null)
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"CI must run the Redis tests, but Docker is unavailable: {unavailableReason}");
            }

            Assert.Skip($"Docker is unavailable: {unavailableReason}");
        }

        await Server.FlushAllDatabasesAsync();
    }

    /// <summary>Runs <paramref name="action"/> while the container is paused: connections stay open but Redis never answers.</summary>
    public async Task<T> WhilePausedAsync<T>(Func<Task<T>> action)
    {
        await Started().PauseAsync();
        try
        {
            return await action();
        }
        finally
        {
            await Started().UnpauseAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (admin is not null)
        {
            await admin.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    private RedisContainer Started() => container ?? throw new InvalidOperationException("Call ResetAsync first; Redis did not start.");
}
