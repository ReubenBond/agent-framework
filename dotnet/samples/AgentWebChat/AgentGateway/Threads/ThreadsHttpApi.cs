// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;

namespace AgentGateway.Threads;

/// <summary>
/// Thread information model.
/// </summary>
public record ThreadInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "thread";

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// Request to create a thread.
/// </summary>
public record CreateThreadRequest
{
    [JsonPropertyName("agent_id")]
    public required string AgentId { get; init; }
}

/// <summary>
/// List of threads response.
/// </summary>
public record ThreadListResponse
{
    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "list";

    [JsonPropertyName("data")]
    public required List<ThreadData> Data { get; init; }
}

/// <summary>
/// Thread data in list response.
/// </summary>
public record ThreadData
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "thread";

    [JsonPropertyName("agent_id")]
    public required string AgentId { get; init; }
}

/// <summary>
/// Thread deletion response.
/// </summary>
public record ThreadDeletionResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "thread.deleted";

    [JsonPropertyName("deleted")]
    public required bool Deleted { get; init; }
}

/// <summary>
/// Thread messages response.
/// </summary>
public record ThreadMessagesResponse
{
    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "list";

    [JsonPropertyName("data")]
    public required List<ConversationItem> Data { get; init; }

    [JsonPropertyName("thread_id")]
    public required string ThreadId { get; init; }
}

/// <summary>
/// Minimal API endpoints for thread management.
/// Compatible with the Python DevUI frontend.
/// </summary>
public static class ThreadsHttpApi
{
    // In-memory thread storage (replace with persistent storage in production)
    private static readonly Dictionary<string, (string ThreadId, string AgentId, long CreatedAt)> s_threads = new();
    private static readonly Dictionary<string, List<ConversationItem>> s_threadMessages = new();
    private static readonly object s_lock = new();

    public static IEndpointRouteBuilder MapThreadsApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/threads")
            .WithTags("Threads")
            .WithOpenApi();

        // Create thread
        group.MapPost("", CreateThreadAsync)
            .WithName("CreateThread")
            .WithSummary("Create a new thread for an agent");

        // List threads for an agent
        group.MapGet("", ListThreadsAsync)
            .WithName("ListThreads")
            .WithSummary("List threads for an agent");

        // Get thread information
        group.MapGet("{threadId}", GetThreadAsync)
            .WithName("GetThread")
            .WithSummary("Get thread information");

        // Delete thread
        group.MapDelete("{threadId}", DeleteThreadAsync)
            .WithName("DeleteThread")
            .WithSummary("Delete a thread");

        // Get thread messages
        group.MapGet("{threadId}/messages", GetThreadMessagesAsync)
            .WithName("GetThreadMessages")
            .WithSummary("Get messages from a thread");

        return endpoints;
    }

    private static Task<IResult> CreateThreadAsync(
        CreateThreadRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var threadId = $"thread_{Guid.NewGuid():N}";
            var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            lock (s_lock)
            {
                s_threads[threadId] = (threadId, request.AgentId, createdAt);
                s_threadMessages[threadId] = new List<ConversationItem>();
            }

            var response = new ThreadInfo
            {
                Id = threadId,
                ObjectType = "thread",
                CreatedAt = createdAt,
                Metadata = new Dictionary<string, string>
                {
                    ["agent_id"] = request.AgentId
                }
            };

            return Task.FromResult(Results.Ok(response));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error creating thread"));
        }
    }

    private static Task<IResult> ListThreadsAsync(
        string agent_id,
        CancellationToken cancellationToken)
    {
        try
        {
            List<ThreadData> threads;
            lock (s_lock)
            {
                threads = s_threads
                    .Where(kvp => kvp.Value.AgentId.Equals(agent_id, StringComparison.OrdinalIgnoreCase))
                    .Select(kvp => new ThreadData
                    {
                        Id = kvp.Value.ThreadId,
                        ObjectType = "thread",
                        AgentId = kvp.Value.AgentId
                    })
                    .ToList();
            }

            var response = new ThreadListResponse
            {
                ObjectType = "list",
                Data = threads
            };

            return Task.FromResult(Results.Ok(response));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error listing threads"));
        }
    }

    private static Task<IResult> GetThreadAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        try
        {
            lock (s_lock)
            {
                if (!s_threads.TryGetValue(threadId, out var thread))
                {
                    return Task.FromResult(Results.NotFound(new { error = new { message = "Thread not found", type = "invalid_request_error" } }));
                }

                var response = new ThreadInfo
                {
                    Id = thread.ThreadId,
                    ObjectType = "thread",
                    CreatedAt = thread.CreatedAt,
                    Metadata = new Dictionary<string, string>
                    {
                        ["agent_id"] = thread.AgentId
                    }
                };

                return Task.FromResult(Results.Ok(response));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error getting thread"));
        }
    }

    private static Task<IResult> DeleteThreadAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        try
        {
            lock (s_lock)
            {
                if (!s_threads.Remove(threadId))
                {
                    return Task.FromResult(Results.NotFound(new { error = new { message = "Thread not found", type = "invalid_request_error" } }));
                }

                s_threadMessages.Remove(threadId);
            }

            var response = new ThreadDeletionResponse
            {
                Id = threadId,
                ObjectType = "thread.deleted",
                Deleted = true
            };

            return Task.FromResult(Results.Ok(response));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error deleting thread"));
        }
    }

    private static Task<IResult> GetThreadMessagesAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        try
        {
            lock (s_lock)
            {
                if (!s_threads.ContainsKey(threadId))
                {
                    return Task.FromResult(Results.NotFound(new { error = new { message = "Thread not found", type = "invalid_request_error" } }));
                }

                var messages = s_threadMessages.TryGetValue(threadId, out var msgs) ? msgs : new List<ConversationItem>();

                var response = new ThreadMessagesResponse
                {
                    ObjectType = "list",
                    Data = messages,
                    ThreadId = threadId
                };

                return Task.FromResult(Results.Ok(response));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error getting thread messages"));
        }
    }
}
