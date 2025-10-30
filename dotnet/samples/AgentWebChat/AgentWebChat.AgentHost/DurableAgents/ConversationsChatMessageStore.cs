// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Converters;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// A Conversations API-backed implementation of <see cref="ChatMessageStore"/> for durable message storage.
/// </summary>
/// <remarks>
/// This implementation stores chat messages using the OpenAI Conversations API exposed by the AgentGateway.
/// Each store instance is associated with a specific conversation identified by a unique conversation ID.
/// Messages are stored as ItemResources in the conversation.
/// This class handles the conversion between ChatMessage and Conversations API types.
/// </remarks>
public sealed class ConversationsChatMessageStore : ChatMessageStore
{
    private readonly ConversationsApiClient _apiClient;
    private readonly string _conversationId;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationsChatMessageStore"/> class.
    /// </summary>
    /// <param name="apiClient">The Conversations API client.</param>
    /// <param name="conversationId">The unique conversation ID for this message store.</param>
    /// <param name="jsonOptions">Optional JSON serialization options.</param>
    public ConversationsChatMessageStore(
        ConversationsApiClient apiClient,
        string conversationId,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        this._apiClient = apiClient;
        this._conversationId = conversationId;
        this._jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc/>
    public override async Task<IEnumerable<ChatMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        // List all items in the conversation in ascending order (oldest first)
        IReadOnlyList<ItemResource> items = await this._apiClient
            .ListItemsAsync(this._conversationId, "asc", 100, cancellationToken)
            .ConfigureAwait(false);

        if (items.Count == 0)
        {
            return Array.Empty<ChatMessage>();
        }

        List<ChatMessage> messages = new(items.Count);

        // Convert ItemResources to ChatMessages
        foreach (ItemResource item in items)
        {
            // Only convert message items
            if (item is ResponsesMessageItemResource)
            {
                ChatMessage message = item.ToChatMessage(this._jsonOptions);
                messages.Add(message);
            }
        }

        return messages;
    }

    /// <inheritdoc/>
    public override async Task AddMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        List<ChatMessage> messageList = messages.ToList();

        if (messageList.Count == 0)
        {
            return;
        }

        // Ensure the conversation exists - create it if it doesn't
        await this.EnsureConversationExistsAsync(cancellationToken).ConfigureAwait(false);

        // Convert ChatMessages to ItemParams for the CreateItemsRequest
        List<ItemParam> itemParams = new(messageList.Count);

        foreach (ChatMessage message in messageList)
        {
            ItemParam itemParam = ConvertChatMessageToItemParam(message);
            itemParams.Add(itemParam);
        }

        // Add the items to the conversation via the API client
        await this._apiClient
            .AddItemsAsync(this._conversationId, itemParams, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        StoreState state = new()
        {
            ConversationId = this._conversationId
        };

        return JsonSerializer.SerializeToElement(state, jsonSerializerOptions ?? this._jsonOptions);
    }

    /// <summary>
    /// Ensures the conversation exists, creating it if necessary.
    /// </summary>
    private async Task EnsureConversationExistsAsync(CancellationToken cancellationToken)
    {
        bool exists = await this._apiClient
            .ConversationExistsAsync(this._conversationId, cancellationToken)
            .ConfigureAwait(false);

        if (!exists)
        {
            await this._apiClient
                .CreateConversationAsync(this._conversationId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Converts a ChatMessage to an ItemParam for the Conversations API.
    /// </summary>
    private static ItemParam ConvertChatMessageToItemParam(ChatMessage message)
    {
        // Use the existing ItemContentConverter to convert AIContent to ItemContent
        List<ItemContent> contentList = new();

        foreach (AIContent aiContent in message.Contents)
        {
            if (ItemContentConverter.ToItemContent(aiContent) is { } itemContent)
            {
                contentList.Add(itemContent);
            }
        }

        // Create InputMessageContent from the converted items
        InputMessageContent content = contentList.Count > 0
            ? InputMessageContent.FromContents(contentList)
            : InputMessageContent.FromText(message.Text ?? string.Empty);

        // Create the appropriate message item based on role
        return message.Role.Value switch
        {
            "user" => new ResponsesUserMessageItemParam
            {
                Content = content
            },
            "assistant" => new ResponsesAssistantMessageItemParam
            {
                Content = content
            },
            "system" => new ResponsesSystemMessageItemParam
            {
                Content = content
            },
            "developer" => new ResponsesDeveloperMessageItemParam
            {
                Content = content
            },
            _ => new ResponsesUserMessageItemParam
            {
                Content = content
            }
        };
    }

    internal sealed class StoreState
    {
        public string ConversationId { get; set; } = string.Empty;
    }
}
