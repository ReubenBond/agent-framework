// Copyright (c) Microsoft. All rights reserved.

using AgentWebChat.AgentHost.DurableAgents.Utilities;
using Microsoft.Agents.AI;
using StackExchange.Redis;

namespace AgentWebChat.AgentHost.DurableAgents;

/// <summary>
/// Extension methods for configuring Redis-backed chat message storage.
/// </summary>
public static class RedisChatMessageStoreExtensions
{
    /// <summary>
    /// Configures the agent to use Redis-backed chat message storage.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="keyPrefix">Optional prefix for Redis keys to namespace the messages. Defaults to "chat:store:".</param>
    /// <param name="maxMessages">Maximum number of messages to store per conversation. Defaults to 1000.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// This extension method configures a <see cref="ChatMessageStore"/> factory that creates
    /// <see cref="RedisChatMessageStore"/> instances for each conversation thread.
    /// It expects <see cref="IConnectionMultiplexer"/> to be already registered by .NET Aspire's Redis integration.
    ///
    /// The factory uses the thread ID as the conversation key to uniquely identify each conversation's messages.
    ///
    /// In your AppHost Program.cs, add Redis like this:
    /// <code>
    /// var redis = builder.AddRedis("redis");
    /// var agentHost = builder.AddProject&lt;Projects.AgentWebChat_AgentHost&gt;("agenthost")
    ///     .WithReference(redis);
    /// </code>
    ///
    /// In your AgentHost Program.cs, register Redis client and configure the message store:
    /// <code>
    /// builder.AddRedisClient("redis");
    /// builder.Services.AddRedisChatMessageStore();
    /// </code>
    /// </remarks>
    public static IServiceCollection AddRedisChatMessageStore(
        this IServiceCollection services,
        string? keyPrefix = null,
        int maxMessages = 1000)
    {
        services.AddSingleton<Func<string, ChatMessageStore>>(sp =>
        {
            IConnectionMultiplexer redis = sp.GetRequiredService<IConnectionMultiplexer>();

            return (conversationKey) => new RedisChatMessageStore(
                redis,
                conversationKey,
                keyPrefix,
                maxMessages);
        });

        return services;
    }
}
