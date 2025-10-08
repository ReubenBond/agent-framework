// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.AI;
using StackExchange.Redis;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// A Redis-backed implementation of <see cref="IChatMessagePersistence"/> for production use.
/// </summary>
/// <remarks>
/// This implementation uses Redis for durable storage with ETag-based optimistic concurrency control.
/// Messages are serialized to JSON and stored in Redis strings with the ETag stored separately.
/// This implementation is designed to work with .NET Aspire's Redis integration.
/// </remarks>
public sealed class RedisChatMessagePersistence : IChatMessagePersistence
{
    private readonly IConnectionMultiplexer _redis;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _keyPrefix;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisChatMessagePersistence"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer injected by .NET Aspire.</param>
    /// <param name="keyPrefix">Optional prefix for Redis keys to namespace the messages. Defaults to "chat:messages:".</param>
    /// <param name="jsonOptions">Optional JSON serialization options. If null, uses default options.</param>
    public RedisChatMessagePersistence(
        IConnectionMultiplexer redis,
        string? keyPrefix = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentNullException.ThrowIfNull(redis);

        this._redis = redis;
        this._keyPrefix = keyPrefix ?? "chat:messages:";
        this._jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc/>
    public async Task<(IList<ChatMessage> Messages, string? ETag)> FetchMessagesAsync(string persistenceKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceKey);

        IDatabase db = this._redis.GetDatabase();
        string messagesKey = this.GetMessagesKey(persistenceKey);
        string eTagKey = this.GetETagKey(persistenceKey);

        // Fetch both messages and ETag in a batch
        RedisValue messagesJson = await db.StringGetAsync(messagesKey).ConfigureAwait(false);
        RedisValue eTagValue = await db.StringGetAsync(eTagKey).ConfigureAwait(false);

        if (messagesJson.IsNullOrEmpty)
        {
            return (Array.Empty<ChatMessage>(), null);
        }

        try
        {
            List<ChatMessage>? messages = JsonSerializer.Deserialize<List<ChatMessage>>(messagesJson.ToString(), this._jsonOptions);
            string? eTag = eTagValue.HasValue ? eTagValue.ToString() : null;

            return (messages ?? [], eTag);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to deserialize chat messages for key '{persistenceKey}'.", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<string> AppendMessagesAsync(string persistenceKey, IEnumerable<ChatMessage> messages, string? eTag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceKey);
        ArgumentNullException.ThrowIfNull(messages);

        IDatabase db = this._redis.GetDatabase();
        string messagesKey = this.GetMessagesKey(persistenceKey);
        string eTagKey = this.GetETagKey(persistenceKey);

        // Generate new ETag
        string newETag = Guid.NewGuid().ToString("N");

        // Serialize messages
        List<ChatMessage> messagesList = new(messages);
        string messagesJson = JsonSerializer.Serialize(messagesList, this._jsonOptions);

        // Use a Lua script for atomic compare-and-swap operation
        const string LuaScript = @"
            local messages_key = KEYS[1]
            local etag_key = KEYS[2]
            local expected_etag = ARGV[1]
            local new_etag = ARGV[2]
            local messages_json = ARGV[3]

            local current_etag = redis.call('GET', etag_key)

            -- Check if this is a new conversation
            if current_etag == false then
                if expected_etag ~= '' then
                    return redis.error_reply('ETag mismatch: expected null for new conversation but received ' .. expected_etag)
                end
            else
                -- Verify ETag matches
                if current_etag ~= expected_etag then
                    return redis.error_reply('ETag mismatch: expected ' .. expected_etag .. ' but found ' .. current_etag)
                end
            end

            -- Update both messages and ETag
            redis.call('SET', messages_key, messages_json)
            redis.call('SET', etag_key, new_etag)

            return new_etag
        ";

        try
        {
            RedisResult result = await db.ScriptEvaluateAsync(
                LuaScript,
                new RedisKey[] { messagesKey, eTagKey },
                new RedisValue[] { eTag ?? string.Empty, newETag, messagesJson }
            ).ConfigureAwait(false);

            return result.ToString()!;
        }
        catch (RedisServerException ex) when (ex.Message.Contains("ETag mismatch"))
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Deletes the persisted messages and ETag for the specified key.
    /// </summary>
    /// <param name="persistenceKey">The key identifying the conversation to delete.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>True if the messages were deleted; otherwise, false.</returns>
    public async Task<bool> DeleteAsync(string persistenceKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceKey);

        IDatabase db = this._redis.GetDatabase();
        string messagesKey = this.GetMessagesKey(persistenceKey);
        string eTagKey = this.GetETagKey(persistenceKey);

        long deletedCount = await db.KeyDeleteAsync(new RedisKey[] { messagesKey, eTagKey }).ConfigureAwait(false);
        return deletedCount > 0;
    }

    /// <summary>
    /// Sets an expiration time on the persisted messages.
    /// </summary>
    /// <param name="persistenceKey">The key identifying the conversation.</param>
    /// <param name="expiration">The time-to-live for the messages.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    public async Task SetExpirationAsync(string persistenceKey, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceKey);

        IDatabase db = this._redis.GetDatabase();
        string messagesKey = this.GetMessagesKey(persistenceKey);
        string eTagKey = this.GetETagKey(persistenceKey);

        await Task.WhenAll(
            db.KeyExpireAsync(messagesKey, expiration),
            db.KeyExpireAsync(eTagKey, expiration)
        ).ConfigureAwait(false);
    }

    private string GetMessagesKey(string persistenceKey) => $"{this._keyPrefix}{persistenceKey}:messages";

    private string GetETagKey(string persistenceKey) => $"{this._keyPrefix}{persistenceKey}:etag";
}
