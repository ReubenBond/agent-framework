// Copyright (c) Microsoft. All rights reserved.

using StackExchange.Redis;

namespace AgentWebChat.AgentHost.DurableAgents;

/// <summary>
/// A Redis-backed implementation of <see cref="IMemoStorage"/> for durable key-value storage.
/// </summary>
/// <remarks>
/// This implementation uses Redis for durable storage with ETag-based optimistic concurrency control.
/// Memos are serialized to JSON and stored in Redis hashes with the ETag stored as a separate hash field.
/// This implementation is designed to work with .NET Aspire's Redis integration.
/// </remarks>
public sealed class RedisMemoStorage : IMemoStorage
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisMemoStorage"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer injected by .NET Aspire.</param>
    /// <param name="keyPrefix">Optional prefix for Redis keys to namespace the memos. Defaults to "memo:".</param>
    public RedisMemoStorage(
        IConnectionMultiplexer redis,
        string? keyPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(redis);

        this._redis = redis;
        this._keyPrefix = keyPrefix ?? "memo:";
    }

    /// <inheritdoc/>
    public async Task<Memo> GetMemoAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        IDatabase db = this._redis.GetDatabase();
        string redisKey = this.GetRedisKey(key);

        HashEntry[] entries = await db.HashGetAllAsync(redisKey).ConfigureAwait(false);

        if (entries.Length == 0)
        {
            return new Memo();
        }

        string? eTag = null;
        Dictionary<string, string> data = new();

        foreach (HashEntry entry in entries)
        {
            string fieldName = entry.Name.ToString();
            if (fieldName == "__etag")
            {
                eTag = entry.Value.ToString();
            }
            else
            {
                data[fieldName] = entry.Value.ToString();
            }
        }

        return new Memo(data, eTag);
    }

    /// <inheritdoc/>
    public async Task<Memo> SetMemoAsync(string key, Memo memo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(memo);

        IDatabase db = this._redis.GetDatabase();
        string redisKey = this.GetRedisKey(key);

        // Generate new ETag
        string newETag = Guid.NewGuid().ToString("N");

        // Prepare hash entries
        List<HashEntry> entries = new(memo.Count + 1)
        {
            new HashEntry("__etag", newETag)
        };

        foreach (KeyValuePair<string, string> kvp in memo)
        {
            entries.Add(new HashEntry(kvp.Key, kvp.Value));
        }

        // Use a Lua script for atomic compare-and-swap operation
        const string LuaScript = @"
            local key = KEYS[1]
            local expected_etag = ARGV[1]
            local new_etag = ARGV[2]

            local current_etag = redis.call('HGET', key, '__etag')

            -- Check if this is a new memo
            if current_etag == false then
                if expected_etag ~= '' then
                    return redis.error_reply('ETag mismatch: expected null for new memo but received ' .. expected_etag)
                end
            else
                -- Verify ETag matches
                if current_etag ~= expected_etag then
                    return redis.error_reply('ETag mismatch: expected ' .. expected_etag .. ' but found ' .. current_etag)
                end
            end

            -- Delete existing hash and set new values atomically
            redis.call('DEL', key)

            -- Set all hash fields from ARGV starting at index 3
            for i = 3, #ARGV, 2 do
                redis.call('HSET', key, ARGV[i], ARGV[i + 1])
            end

            return new_etag
        ";

        try
        {
            // Prepare arguments: expected ETag, new ETag, then field-value pairs
            List<RedisValue> args = new(entries.Count * 2 + 2)
            {
                memo.ETag ?? string.Empty,
                newETag
            };

            foreach (HashEntry entry in entries)
            {
                args.Add(entry.Name);
                args.Add(entry.Value);
            }

            RedisResult result = await db.ScriptEvaluateAsync(
                LuaScript,
                new RedisKey[] { redisKey },
                args.ToArray()
            ).ConfigureAwait(false);

            return new Memo(memo, result.ToString());
        }
        catch (RedisServerException ex) when (ex.Message.Contains("ETag mismatch"))
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Deletes the memo for the specified key.
    /// </summary>
    /// <param name="key">The key identifying the memo to delete.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>True if the memo was deleted; otherwise, false.</returns>
    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        IDatabase db = this._redis.GetDatabase();
        string redisKey = this.GetRedisKey(key);

        return await db.KeyDeleteAsync(redisKey).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets an expiration time on the memo.
    /// </summary>
    /// <param name="key">The key identifying the memo.</param>
    /// <param name="expiration">The time-to-live for the memo.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    public async Task SetExpirationAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        IDatabase db = this._redis.GetDatabase();
        string redisKey = this.GetRedisKey(key);

        await db.KeyExpireAsync(redisKey, expiration).ConfigureAwait(false);
    }

    private string GetRedisKey(string key) => $"{this._keyPrefix}{key}";
}
