// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost.DurableAgents.Utilities;

/// <summary>
/// Extension methods for <see cref="ChatOptions"/> to support conversation ID extraction.
/// </summary>
public static class ConversationIdExtensions
{
    private const string ConversationIdProperty = "ConversationId";

    /// <summary>
    /// Gets the conversation ID from the <see cref="ChatOptions"/>.
    /// </summary>
    /// <param name="options">The chat options.</param>
    /// <returns>The conversation ID if set; otherwise, <see langword="null"/>.</returns>
    public static string? GetConversationId(this ChatOptions? options)
    {
        if (options?.AdditionalProperties is not null &&
            options.AdditionalProperties.TryGetValue(ConversationIdProperty, out object? value) &&
            value is string conversationId)
        {
            return conversationId;
        }

        return null;
    }

    /// <summary>
    /// Sets the conversation ID on the <see cref="ChatOptions"/>.
    /// </summary>
    /// <param name="options">The chat options.</param>
    /// <param name="conversationId">The conversation ID to set.</param>
    public static void SetConversationId(this ChatOptions options, string? conversationId)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (conversationId is not null)
        {
            options.AdditionalProperties ??= [];
            options.AdditionalProperties[ConversationIdProperty] = conversationId;
        }
        else
        {
            options.AdditionalProperties?.Remove(ConversationIdProperty);
        }
    }
}
