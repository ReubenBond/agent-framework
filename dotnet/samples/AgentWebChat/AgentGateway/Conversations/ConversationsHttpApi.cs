// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.AspNetCore.Mvc;

namespace AgentGateway.Conversations;

/// <summary>
/// Minimal API endpoints for OpenAI Conversations API.
/// </summary>
public static class ConversationsHttpApi
{
    /// <summary>
    /// Converts an ItemParam to an ItemResource by adding server-generated fields.
    /// </summary>
    private static ItemResource ToItemResource(this ItemParam param)
    {
        string generatedId = $"msg_{Guid.NewGuid():N}";

        return param switch
        {
            ResponsesUserMessageItemParam userMessageParam => new ResponsesUserMessageItemResource
            {
                Id = generatedId,
                Content = (IList<ItemContent>)userMessageParam.Content,
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            ResponsesSystemMessageItemParam systemMessageParam => new ResponsesSystemMessageItemResource
            {
                Id = generatedId,
                Content = (IList<ItemContent>)systemMessageParam.Content,
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            ResponsesAssistantMessageItemParam assistantMessageParam => new ResponsesAssistantMessageItemResource
            {
                Id = generatedId,
                Content = (IList<ItemContent>)assistantMessageParam.Content,
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            ResponsesDeveloperMessageItemParam developerMessageParam => new ResponsesDeveloperMessageItemResource
            {
                Id = generatedId,
                Content = (IList<ItemContent>)developerMessageParam.Content,
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            FunctionToolCallItemParam functionCallParam => new FunctionToolCallItemResource
            {
                Id = generatedId,
                Name = functionCallParam.Name,
                CallId = functionCallParam.CallId,
                Arguments = functionCallParam.Arguments,
                Status = FunctionToolCallItemResourceStatus.Completed
            },
            FunctionToolCallOutputItemParam functionOutputParam => new FunctionToolCallOutputItemResource
            {
                Id = generatedId,
                CallId = functionOutputParam.CallId,
                Output = functionOutputParam.Output
            },
            _ => throw new NotSupportedException($"ItemParam type {param.GetType().Name} is not supported")
        };
    }

    public static IEndpointConventionBuilder MapConversations(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/conversations")
            .WithTags("Conversations")
            .WithOpenApi();

        // Conversation endpoints
        // Non-standard extension: List conversations by agent ID
        group.MapGet("", ListConversationsByAgentAsync)
            .WithName("ListConversationsByAgent")
            .WithSummary("List conversations for a specific agent (non-standard extension)");

        group.MapPost("", CreateConversationAsync)
            .WithName("CreateConversation")
            .WithSummary("Create a new conversation");

        group.MapGet("{conversationId}", GetConversationAsync)
            .WithName("GetConversation")
            .WithSummary("Retrieve a conversation by ID");

        group.MapPost("{conversationId}", UpdateConversationAsync)
            .WithName("UpdateConversation")
            .WithSummary("Update a conversation's metadata or title");

        group.MapDelete("{conversationId}", DeleteConversationAsync)
            .WithName("DeleteConversation")
            .WithSummary("Delete a conversation and all its messages");

        // Item endpoints
        group.MapPost("{conversationId}/items", CreateItemsAsync)
            .WithName("CreateItems")
            .WithSummary("Add items to a conversation");

        group.MapGet("{conversationId}/items", ListItemsAsync)
            .WithName("ListItems")
            .WithSummary("List items in a conversation");

        group.MapGet("{conversationId}/items/{itemId}", GetItemAsync)
            .WithName("GetItem")
            .WithSummary("Retrieve a specific item");

        group.MapDelete("{conversationId}/items/{itemId}", DeleteItemAsync)
            .WithName("DeleteItem")
            .WithSummary("Delete a specific item");

        return group;
    }

    // Non-standard extension: List conversations by agent ID
    private static async Task<IResult> ListConversationsByAgentAsync(
        [FromQuery] string? agent_id,
        [FromServices] IAgentConversationIndex? conversationIndex,
        [FromServices] IConversationStorage storage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(agent_id))
        {
            return Results.BadRequest(new { error = new { message = "agent_id query parameter is required.", type = "invalid_request_error" } });
        }

        // Return empty list if conversation index is not registered
        if (conversationIndex == null)
        {
            return Results.Ok(new { @object = "list", data = Array.Empty<Conversation>(), has_more = false });
        }

        var conversationIds = await conversationIndex.GetConversationIdsAsync(agent_id, cancellationToken);

        // Fetch full conversation objects
        var conversations = new List<Conversation>();
        foreach (var conversationId in conversationIds)
        {
            var conversation = await storage.GetConversationAsync(conversationId, cancellationToken);
            if (conversation is not null)
            {
                conversations.Add(conversation);
            }
        }

        return Results.Ok(new { @object = "list", data = conversations, has_more = false });
    }

    private static async Task<IResult> CreateConversationAsync(
        [FromBody] CreateConversationRequest request,
        [FromServices] IConversationStorage storage,
        [FromServices] IAgentConversationIndex? conversationIndex,
        CancellationToken cancellationToken)
    {
        var metadata = request.Metadata ?? [];
        var conversation = new Conversation
        {
            Id = $"conv_{Guid.NewGuid():N}",
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Metadata = metadata
        };

        var created = await storage.CreateConversationAsync(conversation, cancellationToken);

        // Add initial items if provided
        if (request.Items is { Length: > 0 })
        {
            foreach (var item in request.Items)
            {
                var itemResource = item.ToItemResource();
                await storage.AddItemAsync(created.Id, itemResource, cancellationToken);
            }
        }

        // Add to conversation index if available and agent_id is provided in metadata
        if (conversationIndex != null && created.Metadata.TryGetValue("agent_id", out var agentId) && !string.IsNullOrEmpty(agentId))
        {
            await conversationIndex.AddConversationAsync(agentId, created.Id, cancellationToken);
        }

        return Results.Ok(created);
    }

    private static async Task<IResult> GetConversationAsync(
        string conversationId,
        [FromServices] IConversationStorage storage,
        CancellationToken cancellationToken)
    {
        var conversation = await storage.GetConversationAsync(conversationId, cancellationToken);
        return conversation is not null
            ? Results.Ok(conversation)
            : Results.NotFound(new { error = new { message = $"Conversation '{conversationId}' not found.", type = "invalid_request_error" } });
    }

    private static async Task<IResult> UpdateConversationAsync(
        string conversationId,
        [FromBody] UpdateConversationRequest request,
        [FromServices] IConversationStorage storage,
        CancellationToken cancellationToken)
    {
        var existing = await storage.GetConversationAsync(conversationId, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound(new { error = new { message = $"Conversation '{conversationId}' not found.", type = "invalid_request_error" } });
        }

        var updated = existing with
        {
            Metadata = request.Metadata
        };

        var result = await storage.UpdateConversationAsync(updated, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteConversationAsync(
        string conversationId,
        [FromServices] IConversationStorage storage,
        [FromServices] IAgentConversationIndex? conversationIndex,
        CancellationToken cancellationToken)
    {
        // Get conversation first to retrieve agent_id for index removal
        var conversation = await storage.GetConversationAsync(conversationId, cancellationToken);

        var deleted = await storage.DeleteConversationAsync(conversationId, cancellationToken);
        if (!deleted)
        {
            return Results.NotFound(new { error = new { message = $"Conversation '{conversationId}' not found.", type = "invalid_request_error" } });
        }

        // Remove from conversation index if available and agent_id was present in metadata
        if (conversationIndex != null && conversation?.Metadata.TryGetValue("agent_id", out var agentId) == true && !string.IsNullOrEmpty(agentId))
        {
            await conversationIndex.RemoveConversationAsync(agentId, conversationId, cancellationToken);
        }

        return Results.Ok(new DeleteResponse
        {
            Id = conversationId,
            Object = "conversation.deleted",
            Deleted = true
        });
    }

    private static async Task<IResult> CreateItemsAsync(
        string conversationId,
        [FromBody] CreateItemsRequest request,
        [FromQuery] string[]? include,
        [FromServices] IConversationStorage storage,
        CancellationToken cancellationToken)
    {
        var conversation = await storage.GetConversationAsync(conversationId, cancellationToken);
        if (conversation is null)
        {
            return Results.NotFound(new { error = new { message = $"Conversation '{conversationId}' not found.", type = "invalid_request_error" } });
        }

        var createdItems = new List<ItemResource>();
        foreach (var item in request.Items)
        {
            var itemResource = item.ToItemResource();
            var created = await storage.AddItemAsync(conversationId, itemResource, cancellationToken);
            createdItems.Add(created);
        }

        return Results.Ok(new { data = createdItems });
    }

    private static async Task<IResult> ListItemsAsync(
        string conversationId,
        [FromQuery] int limit = 20,
        [FromQuery] string order = "desc",
        [FromQuery] string? after = null,
        [FromQuery] string[]? include = null,
        [FromServices] IConversationStorage storage = null!,
        CancellationToken cancellationToken = default)
    {
        var conversation = await storage.GetConversationAsync(conversationId, cancellationToken);
        if (conversation is null)
        {
            return Results.NotFound(new { error = new { message = $"Conversation '{conversationId}' not found.", type = "invalid_request_error" } });
        }

        var result = await storage.ListItemsAsync(conversationId, limit, ParseOrder(order), after, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetItemAsync(
        string conversationId,
        string itemId,
        [FromQuery] string[]? include,
        [FromServices] IConversationStorage storage,
        CancellationToken cancellationToken)
    {
        var item = await storage.GetItemAsync(conversationId, itemId, cancellationToken);
        return item is not null
            ? Results.Ok(item)
            : Results.NotFound(new { error = new { message = $"Item '{itemId}' not found in conversation '{conversationId}'.", type = "invalid_request_error" } });
    }

    private static async Task<IResult> DeleteItemAsync(
        string conversationId,
        string itemId,
        [FromServices] IConversationStorage storage,
        CancellationToken cancellationToken)
    {
        var deleted = await storage.DeleteItemAsync(conversationId, itemId, cancellationToken);
        if (!deleted)
        {
            return Results.NotFound(new { error = new { message = $"Item '{itemId}' not found in conversation '{conversationId}'.", type = "invalid_request_error" } });
        }

        return Results.Ok(new DeleteResponse
        {
            Id = itemId,
            Object = "conversation.item.deleted",
            Deleted = true
        });
    }

    private static SortOrder ParseOrder(string order)
    {
        return string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase) ? SortOrder.Ascending : SortOrder.Descending;
    }
}
