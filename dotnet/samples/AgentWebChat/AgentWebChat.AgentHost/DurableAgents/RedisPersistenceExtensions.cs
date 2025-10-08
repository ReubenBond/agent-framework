// Copyright (c) Microsoft. All rights reserved.

using AgentWebChat.AgentHost.DurableAgents.Utilities;
using StackExchange.Redis;

namespace AgentWebChat.AgentHost.DurableAgents;

/// <summary>
/// Extension methods for registering Redis-backed chat message persistence with .NET Aspire.
/// </summary>
public static class RedisPersistenceExtensions
{
    /// <summary>
    /// Adds Redis-backed chat message persistence to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="keyPrefix">Optional prefix for Redis keys to namespace the messages. Defaults to "chat:messages:".</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This extension method registers <see cref="RedisChatMessagePersistence"/> as a singleton service.
    /// It expects <see cref="IConnectionMultiplexer"/> to be already registered by .NET Aspire's Redis integration.
    ///
    /// In your AppHost Program.cs, add Redis like this:
    /// <code>
    /// var redis = builder.AddRedis("redis");
    /// var agentHost = builder.AddProject&lt;Projects.AgentWebChat_AgentHost&gt;("agenthost")
    ///     .WithReference(redis);
    /// </code>
    ///
    /// In your AgentHost Program.cs, register the persistence service:
    /// <code>
    /// builder.AddRedisClient("redis");
    /// builder.Services.AddRedisChatMessagePersistence();
    /// </code>
    /// </remarks>
    public static IServiceCollection AddRedisChatMessagePersistence(
        this IServiceCollection services,
        string? keyPrefix = null)
    {
        services.AddSingleton<IChatMessagePersistence>(sp =>
        {
            IConnectionMultiplexer redis = sp.GetRequiredService<IConnectionMultiplexer>();
            return new RedisChatMessagePersistence(redis, keyPrefix);
        });

        return services;
    }
}
