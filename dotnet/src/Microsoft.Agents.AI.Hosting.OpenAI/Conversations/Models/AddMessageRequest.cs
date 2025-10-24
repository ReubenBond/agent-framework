// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;

/// <summary>
/// Request to create items in a conversation.
/// </summary>
internal sealed record CreateItemsRequest
{
    /// <summary>
    /// The items to add to the conversation. You may add up to 20 items at a time.
    /// Items should be ItemParam objects (messages without IDs, function call outputs, etc.).
    /// The server will assign IDs when creating the items.
    /// </summary>
    [JsonPropertyName("items")]
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Matches OpenAI API specification")]
    public required ItemParam[] Items { get; init; }
}
