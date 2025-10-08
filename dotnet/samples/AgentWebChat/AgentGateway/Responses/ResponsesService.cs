// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace AgentGateway.Responses;

/// <summary>
/// Service for handling OpenAI Responses API operations using Orleans grains.
/// </summary>
public sealed class ResponsesService
{
    private readonly IGrainFactory _grainFactory;

    public ResponsesService(IGrainFactory grainFactory)
    {
        ArgumentNullException.ThrowIfNull(grainFactory);
        this._grainFactory = grainFactory;
    }

    /// <summary>
    /// Creates a model response for the given input using ChatClientAgent.
    /// </summary>
    public async Task<Response> CreateResponseAsync(
        CreateResponse request,
        CancellationToken cancellationToken = default)
    {
        var responseId = $"resp_{Guid.NewGuid():N}";
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);
        return await grain.CreateAsync(request, cancellationToken);
    }

    /// <summary>
    /// Creates a streaming model response for the given input using ChatClientAgent.
    /// </summary>
    public async IAsyncEnumerable<StreamingResponseEvent> CreateResponseStreamingAsync(
        CreateResponse request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var responseId = $"resp_{Guid.NewGuid():N}";
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);

        await foreach (var update in grain.CreateStreamingAsync(request, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Retrieves a response by ID.
    /// </summary>
    public async Task<Response?> GetResponseAsync(string responseId, CancellationToken cancellationToken = default)
    {
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);
        return await grain.GetAsync();
    }

    /// <summary>
    /// Retrieves a response by ID in streaming mode, yielding events as they become available.
    /// </summary>
    /// <param name="responseId">The ID of the response to retrieve.</param>
    /// <param name="startingAfter">The sequence number after which to start streaming. If null, starts from the beginning.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of streaming updates.</returns>
    public async IAsyncEnumerable<StreamingResponseEvent> GetResponseStreamingAsync(
        string responseId,
        int? startingAfter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);

        await foreach (var update in grain.GetStreamingAsync(startingAfter, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Lists the input items for a response.
    /// </summary>
    public async Task<ListResponse<ItemResource>> ListResponseInputItemsAsync(
        string responseId,
        int limit,
        string order,
        string? after,
        string? before,
        CancellationToken cancellationToken = default)
    {
        var grain = this._grainFactory.GetGrain<IResponseGrain>(responseId);
        return await grain.ListInputItemsAsync(limit, order, after, before);
    }
}
