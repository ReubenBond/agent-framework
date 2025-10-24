// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Conversations;

/// <summary>
/// In-memory implementation of conversation storage for testing and development.
/// This implementation is thread-safe but data is not persisted across application restarts.
/// </summary>
public sealed class InMemoryConversationStorage : IConversationStorage
{
    private readonly ConcurrentDictionary<string, ConversationState> _conversations = new();

    /// <inheritdoc />
    public Task<Conversation> CreateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        var state = new ConversationState(conversation);
        if (this._conversations.TryAdd(conversation.Id, state))
        {
            return Task.FromResult(conversation);
        }

        throw new InvalidOperationException($"Conversation with ID '{conversation.Id}' already exists.");
    }

    /// <inheritdoc />
    public Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (this._conversations.TryGetValue(conversationId, out var state))
        {
            return Task.FromResult<Conversation?>(state.Conversation);
        }
        return Task.FromResult<Conversation?>(null);
    }

    /// <inheritdoc />
    public Task<Conversation?> UpdateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        if (this._conversations.TryGetValue(conversation.Id, out var state))
        {
            state.UpdateConversation(conversation);
            return Task.FromResult<Conversation?>(conversation);
        }

        return Task.FromResult<Conversation?>(null);
    }

    /// <inheritdoc />
    public Task<bool> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(this._conversations.TryRemove(conversationId, out _));
    }

    /// <inheritdoc />
    public Task<ItemResource> AddItemAsync(string conversationId, ItemResource item, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(conversationId, nameof(conversationId));

        if (!this._conversations.TryGetValue(conversationId, out ConversationState? state))
        {
            throw new InvalidOperationException($"Conversation '{conversationId}' not found.");
        }

        state.AddItem(item);
        return Task.FromResult(item);
    }

    /// <inheritdoc />
    public Task<ItemResource?> GetItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        if (this._conversations.TryGetValue(conversationId, out ConversationState? state))
        {
            return Task.FromResult(state.GetItem(itemId));
        }

        return Task.FromResult<ItemResource?>(null);
    }

    /// <inheritdoc />
    public Task<ListResponse<ItemResource>> ListItemsAsync(
        string conversationId,
        int limit = 20,
        SortOrder order = SortOrder.Descending,
        string? after = null,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        if (!this._conversations.TryGetValue(conversationId, out ConversationState? state))
        {
            throw new InvalidOperationException($"Conversation '{conversationId}' not found.");
        }

        var allItems = state.GetAllItems();

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

    /// <inheritdoc />
    public Task<bool> DeleteItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        if (this._conversations.TryGetValue(conversationId, out ConversationState? state))
        {
            return Task.FromResult(state.RemoveItem(itemId));
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Encapsulates per-conversation state including items storage and synchronization.
    /// </summary>
    private sealed class ConversationState
    {
#if NET9_0_OR_GREATER
        private readonly OrderedDictionary<string, ItemResource> _items = [];
        private readonly object _lock = new();
        private Conversation _conversation;

        public ConversationState(Conversation conversation)
        {
            this._conversation = conversation;
        }

        public Conversation Conversation
        {
            get
            {
                lock (this._lock)
                {
                    return this._conversation;
                }
            }
        }

        public void UpdateConversation(Conversation conversation)
        {
            lock (this._lock)
            {
                this._conversation = conversation;
            }
        }

        public void AddItem(ItemResource item)
        {
            lock (this._lock)
            {
                if (!this._items.TryAdd(item.Id, item))
                {
                    throw new InvalidOperationException($"Item with ID '{item.Id}' already exists.");
                }
            }
        }

        public ItemResource? GetItem(string itemId)
        {
            lock (this._lock)
            {
                this._items.TryGetValue(itemId, out var item);
                return item;
            }
        }

        public List<ItemResource> GetAllItems()
        {
            lock (this._lock)
            {
                return this._items.Values.ToList();
            }
        }

        public bool RemoveItem(string itemId)
        {
            lock (this._lock)
            {
                return this._items.Remove(itemId);
            }
        }
#else
        private readonly List<ItemResource> _items = [];
        private readonly object _lock = new();
        private Conversation _conversation;

        public ConversationState(Conversation conversation)
        {
            this._conversation = conversation;
        }

        public Conversation Conversation
        {
            get
            {
                lock (this._lock)
                {
                    return this._conversation;
                }
            }
        }

        public void UpdateConversation(Conversation conversation)
        {
            lock (this._lock)
            {
                this._conversation = conversation;
            }
        }

        public void AddItem(ItemResource item)
        {
            lock (this._lock)
            {
                if (this._items.Any(i => i.Id == item.Id))
                {
                    throw new InvalidOperationException($"Item with ID '{item.Id}' already exists.");
                }
                this._items.Add(item);
            }
        }

        public ItemResource? GetItem(string itemId)
        {
            lock (this._lock)
            {
                return this._items.FirstOrDefault(i => i.Id == itemId);
            }
        }

        public List<ItemResource> GetAllItems()
        {
            lock (this._lock)
            {
                return this._items.ToList();
            }
        }

        public bool RemoveItem(string itemId)
        {
            lock (this._lock)
            {
                var item = this._items.FirstOrDefault(i => i.Id == itemId);
                if (item != null)
                {
                    this._items.Remove(item);
                    return true;
                }
                return false;
            }
        }
#endif
    }
}
