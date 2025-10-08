// Copyright (c) Microsoft. All rights reserved.

using StackExchange.Redis;

namespace AgentWebChat.AgentHost.DurableAgents;

/// <summary>
/// Extension methods for registering Redis-backed memo storage with .NET Aspire.
/// </summary>
public static class RedisMemoStorageExtensions
{
    /// <summary>
    /// Adds Redis-backed memo storage to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="keyPrefix">Optional prefix for Redis keys to namespace the memos. Defaults to "memo:".</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This extension method registers <see cref="RedisMemoStorage"/> as a singleton service.
    /// It expects <see cref="IConnectionMultiplexer"/> to be already registered by .NET Aspire's Redis integration.
    ///
    /// In your AppHost Program.cs, add Redis like this:
    /// <code>
    /// var redis = builder.AddRedis("redis");
    /// var agentHost = builder.AddProject&lt;Projects.AgentWebChat_AgentHost&gt;("agenthost")
    ///     .WithReference(redis);
    /// </code>
    ///
    /// In your AgentHost Program.cs, register the memo storage service:
    /// <code>
    /// builder.AddRedisClient("redis");
    /// builder.Services.AddRedisMemoStorage();
    /// </code>
    /// </remarks>
    public static IServiceCollection AddRedisMemoStorage(
        this IServiceCollection services,
        string? keyPrefix = null)
    {
        services.AddSingleton<IMemoStorage>(sp =>
        {
            IConnectionMultiplexer redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisMemoStorage(redis, keyPrefix);
        });

        return services;
    }
}
