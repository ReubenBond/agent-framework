// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace AgentGateway.Conversations;

/// <summary>
/// In-memory implementation of conversation storage for testing and development.
/// This implementation is thread-safe but data is not persisted across application restarts.
/// </summary>
public sealed class InMemoryConversationStorage : IConversationStorage
{
    private readonly ConcurrentDictionary<string, Conversation> _conversations = new();
    private readonly ConcurrentDictionary<string, OrderedDictionary<string, ItemResource>> _items = new();
    private readonly object _itemsLock = new();

    public Task<Conversation> CreateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        if (this._conversations.TryAdd(conversation.Id, conversation))
        {
            lock (this._itemsLock)
            {
                this._items[conversation.Id] = new OrderedDictionary<string, ItemResource>();
            }
            return Task.FromResult(conversation);
        }

        throw new InvalidOperationException($"Conversation with ID '{conversation.Id}' already exists.");
    }

    public Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        this._conversations.TryGetValue(conversationId, out var conversation);
        return Task.FromResult(conversation);
    }

    public Task<Conversation?> UpdateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        if (this._conversations.ContainsKey(conversation.Id))
        {
            this._conversations[conversation.Id] = conversation;
            return Task.FromResult<Conversation?>(conversation);
        }

        return Task.FromResult<Conversation?>(null);
    }

    public Task<bool> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var removed = this._conversations.TryRemove(conversationId, out _);
        if (removed)
        {
            lock (this._itemsLock)
            {
                this._items.TryRemove(conversationId, out _);
            }
        }
        return Task.FromResult(removed);
    }

    public Task<ItemResource> AddItemAsync(string conversationId, ItemResource item, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationId, nameof(conversationId));

        lock (this._itemsLock)
        {
            if (!this._items.TryGetValue(conversationId, out var conversationItems))
            {
                throw new InvalidOperationException($"Conversation '{conversationId}' not found.");
            }

            if (!conversationItems.TryAdd(item.Id, item))
            {
                throw new InvalidOperationException($"Item with ID '{item.Id}' already exists in conversation '{conversationId}'.");
            }
        }

        return Task.FromResult(item);
    }

    public Task<ItemResource?> GetItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        lock (this._itemsLock)
        {
            if (this._items.TryGetValue(conversationId, out var conversationItems) &&
                conversationItems.TryGetValue(itemId, out var item))
            {
                return Task.FromResult<ItemResource?>(item);
            }
        }

        return Task.FromResult<ItemResource?>(null);
    }

    public Task<ListResponse<ItemResource>> ListItemsAsync(
        string conversationId,
        int limit = 20,
        SortOrder order = SortOrder.Descending,
        string? after = null,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        List<ItemResource> allItems;
        lock (this._itemsLock)
        {
            if (!this._items.TryGetValue(conversationId, out var conversationItems))
            {
                throw new InvalidOperationException($"Conversation '{conversationId}' not found.");
            }

            allItems = conversationItems.Values.ToList();
        }

        // OrderedDictionary maintains insertion order
        // For descending order, reverse the list
        if (order == SortOrder.Descending)
        {
            allItems.Reverse();
        }

        var filtered = allItems.AsEnumerable();

        if (!string.IsNullOrEmpty(after))
        {
            var afterIndex = allItems.FindIndex(m => m.Id == after);
            if (afterIndex >= 0)
            {
                filtered = allItems.Skip(afterIndex + 1);
            }
        }

        var result = filtered.Take(limit + 1).ToList();
        var hasMore = result.Count > limit;
        if (hasMore)
        {
            result = result.Take(limit).ToList();
        }

        return Task.FromResult(new ListResponse<ItemResource>
        {
            Data = result,
            FirstId = result.FirstOrDefault()?.Id,
            LastId = result.LastOrDefault()?.Id,
            HasMore = hasMore
        });
    }

    public Task<bool> DeleteItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        lock (this._itemsLock)
        {
            if (this._items.TryGetValue(conversationId, out var conversationItems))
            {
                return Task.FromResult(conversationItems.Remove(itemId));
            }
        }

        return Task.FromResult(false);
    }
}
