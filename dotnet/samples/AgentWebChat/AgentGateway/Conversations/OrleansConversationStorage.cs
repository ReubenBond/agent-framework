// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace AgentGateway.Conversations;

/// <summary>
/// Orleans-backed implementation of conversation storage.
/// This implementation provides persistent, distributed storage for conversations and messages.
/// </summary>
public sealed class OrleansConversationStorage(IGrainFactory grainFactory) : IConversationStorage
{
    public async Task<Conversation> CreateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversation.Id);
        return await grain.CreateAsync(conversation);
    }

    public async Task<Conversation?> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.GetAsync();
    }

    public async Task<Conversation?> UpdateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversation.Id);
        return await grain.UpdateAsync(conversation);
    }

    public async Task<bool> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.DeleteAsync();
    }

    public async Task<ItemResource> AddItemAsync(string conversationId, ItemResource item, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.AddItemAsync(item);
    }

    public async Task<ItemResource?> GetItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.GetItemAsync(itemId);
    }

    public async Task<ListResponse<ItemResource>> ListItemsAsync(
        string conversationId,
        int limit = 20,
        SortOrder order = SortOrder.Descending,
        string? after = null,
        CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.ListItemsAsync(limit, order, after);
    }

    public async Task<bool> DeleteItemAsync(string conversationId, string itemId, CancellationToken cancellationToken = default)
    {
        var grain = grainFactory.GetGrain<IConversationGrain>(conversationId);
        return await grain.DeleteItemAsync(itemId);
    }
}
