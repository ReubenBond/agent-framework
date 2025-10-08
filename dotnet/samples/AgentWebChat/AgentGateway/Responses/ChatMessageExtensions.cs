// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Extensions.AI;

namespace AgentGateway.Responses;

/// <summary>
/// Extension methods for converting chat messages to conversation items.
/// </summary>
internal static class ChatMessageExtensions
{
    /// <summary>
    /// Converts a list of chat messages to conversation items.
    /// </summary>
    /// <param name="messages">The chat messages to convert.</param>
    /// <param name="conversationId">Optional conversation ID to associate with the items.</param>
    /// <returns>A list of conversation items.</returns>
    public static List<ConversationItem> ToConversationItems(this IList<ChatMessage> messages, string? conversationId = null)
    {
        var items = new List<ConversationItem>(messages.Count);

        foreach (var message in messages)
        {
            items.Add(ConversationItem.FromChatMessage(message, conversationId));
        }

        return items;
    }
}
