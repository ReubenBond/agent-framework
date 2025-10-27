// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace AgentGateway.Responses;

/// <summary>
/// Interface for executing response generation.
/// Implementations can use local execution (ChatClientAgent) or forward to remote workers.
/// </summary>
public interface IResponseExecutor
{
    /// <summary>
    /// Executes a response generation request and returns streaming events.
    /// </summary>
    /// <param name="responseId">The unique identifier for the response.</param>
    /// <param name="conversationId">The conversation ID if applicable.</param>
    /// <param name="request">The create response request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of streaming response events.</returns>
    IAsyncEnumerable<StreamingResponseEvent> ExecuteAsync(
        string responseId,
        string? conversationId,
        CreateResponse request,
        CancellationToken cancellationToken = default);
}
