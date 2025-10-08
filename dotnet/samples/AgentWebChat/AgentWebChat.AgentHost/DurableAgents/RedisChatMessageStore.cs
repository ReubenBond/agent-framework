// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using StackExchange.Redis;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// A Redis-backed implementation of <see cref="ChatMessageStore"/> for durable message storage.
/// </summary>
/// <remarks>
/// This implementation stores chat messages in Redis, allowing them to persist across agent invocations
/// and application restarts. Each store instance is associated with a specific conversation identified
/// by a unique key (typically the ResponseId).
/// </remarks>
public sealed class RedisChatMessageStore : ChatMessageStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _conversationKey;
    private readonly string _keyPrefix;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly int _maxMessages;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisChatMessageStore"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="conversationKey">The unique key identifying this conversation (e.g., ResponseId).</param>
    /// <param name="keyPrefix">Optional prefix for Redis keys to namespace the messages. Defaults to "chat:store:".</param>
    /// <param name="maxMessages">Maximum number of messages to store. Older messages are automatically removed. Defaults to 1000.</param>
    /// <param name="jsonOptions">Optional JSON serialization options.</param>
    public RedisChatMessageStore(
        IConnectionMultiplexer redis,
        string conversationKey,
        string? keyPrefix = null,
        int maxMessages = 1000,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationKey);

        this._redis = redis;
        this._conversationKey = conversationKey;
        this._keyPrefix = keyPrefix ?? "chat:store:";
        this._maxMessages = maxMessages;
        this._jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc/>
    public override async Task<IEnumerable<ChatMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        IDatabase db = this._redis.GetDatabase();
        string key = this.GetRedisKey();

        // Get all messages from the Redis list (stored in chronological order)
        RedisValue[] values = await db.ListRangeAsync(key).ConfigureAwait(false);

        if (values.Length == 0)
        {
            return [];
        }

        List<ChatMessage> messages = new(values.Length);
        foreach (RedisValue value in values)
        {
            if (!value.IsNullOrEmpty)
            {
                try
                {
                    ChatMessage? message = JsonSerializer.Deserialize<ChatMessage>(value.ToString(), this._jsonOptions);
                    if (message is not null)
                    {
                        messages.Add(message);
                    }
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"Failed to deserialize chat message from Redis for key '{this._conversationKey}'.", ex);
                }
            }
        }

        return messages;
    }

    /// <inheritdoc/>
    public override async Task AddMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        IDatabase db = this._redis.GetDatabase();
        string key = this.GetRedisKey();

        // Serialize and add messages to the Redis list
        foreach (ChatMessage message in messages)
        {
            string messageJson = JsonSerializer.Serialize(message, this._jsonOptions);
            await db.ListRightPushAsync(key, messageJson).ConfigureAwait(false);
        }

        // Trim the list to keep only the most recent messages
        long currentLength = await db.ListLengthAsync(key).ConfigureAwait(false);
        if (currentLength > this._maxMessages)
        {
            long removeCount = currentLength - this._maxMessages;
            await db.ListTrimAsync(key, removeCount, -1).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        StoreState state = new()
        {
            ConversationKey = this._conversationKey,
            KeyPrefix = this._keyPrefix,
            MaxMessages = this._maxMessages
        };

        return JsonSerializer.SerializeToElement(state, jsonSerializerOptions ?? this._jsonOptions);
    }

    /// <summary>
    /// Deletes all messages for this conversation from Redis.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>True if the messages were deleted; otherwise, false.</returns>
    public async Task<bool> DeleteAsync(CancellationToken cancellationToken = default)
    {
        IDatabase db = this._redis.GetDatabase();
        string key = this.GetRedisKey();
        return await db.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets an expiration time on the stored messages.
    /// </summary>
    /// <param name="expiration">The time-to-live for the messages.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    public async Task SetExpirationAsync(TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        IDatabase db = this._redis.GetDatabase();
        string key = this.GetRedisKey();
        await db.KeyExpireAsync(key, expiration).ConfigureAwait(false);
    }

    private string GetRedisKey() => $"{this._keyPrefix}{this._conversationKey}";

    internal sealed class StoreState
    {
        public string ConversationKey { get; set; } = string.Empty;
        public string KeyPrefix { get; set; } = string.Empty;
        public int MaxMessages { get; set; }
    }
}
