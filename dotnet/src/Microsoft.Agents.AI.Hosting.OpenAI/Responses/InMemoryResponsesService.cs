// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;

/// <summary>
/// In-memory implementation of responses service for testing and development.
/// This implementation is thread-safe but data is not persisted across application restarts.
/// </summary>
internal sealed class InMemoryResponsesService : IResponsesService
{
    private readonly IResponseExecutor _executor;
    private readonly ConcurrentDictionary<string, ResponseState> _responses = new();

    private sealed class ResponseState
    {
        public Response? Response { get; set; }
        public CreateResponse? Request { get; set; }
        public List<StreamingResponseEvent> StreamingUpdates { get; } = [];
        public TaskCompletionSource<bool>? CompletionSource { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }
        public SemaphoreSlim UpdateSignal { get; } = new SemaphoreSlim(0);
        public bool IsTerminal => this.Response?.IsTerminal ?? false;
    }

    public InMemoryResponsesService(IResponseExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        this._executor = executor;
    }

    public async Task<Response> CreateResponseAsync(
        CreateResponse request,
        CancellationToken cancellationToken = default)
    {
        this.ValidateRequest(request);

        if (request.Stream == true)
        {
            throw new InvalidOperationException("Cannot create a streaming response using CreateResponseAsync. Use CreateResponseStreamingAsync instead.");
        }

        var responseId = $"resp_{Guid.NewGuid():N}";
        var state = this.InitializeResponse(responseId, request);

        // For background responses, start execution and return immediately
        if (request.Background == true)
        {
            _ = this.ExecuteResponseAsync(responseId, state, CancellationToken.None);
            return state.Response!;
        }

        // For non-background responses, wait for completion
        _ = this.ExecuteResponseAsync(responseId, state, cancellationToken);

        await state.CompletionSource!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return state.Response!;
    }

    public async IAsyncEnumerable<StreamingResponseEvent> CreateResponseStreamingAsync(
        CreateResponse request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this.ValidateRequest(request);

        if (request.Stream == false)
        {
            throw new InvalidOperationException("Cannot create a non-streaming response using CreateResponseStreamingAsync. Use CreateResponseAsync instead.");
        }

        var responseId = $"resp_{Guid.NewGuid():N}";
        var state = this.InitializeResponse(responseId, request);

        // Start execution
        _ = this.ExecuteResponseAsync(responseId, state, CancellationToken.None);

        // Stream updates as they become available
        var streamedCount = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Yield any new updates
            lock (state.StreamingUpdates)
            {
                while (streamedCount < state.StreamingUpdates.Count)
                {
                    yield return state.StreamingUpdates[streamedCount];
                    streamedCount++;
                }
            }

            // Check if we're done
            if (state.IsTerminal)
            {
                break;
            }

            // Wait for the next update to be signaled
            await state.UpdateSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<Response?> GetResponseAsync(string responseId, CancellationToken cancellationToken = default)
    {
        this._responses.TryGetValue(responseId, out ResponseState? state);
        return Task.FromResult(state?.Response);
    }

    public async IAsyncEnumerable<StreamingResponseEvent> GetResponseStreamingAsync(
        string responseId,
        int? startingAfter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!this._responses.TryGetValue(responseId, out ResponseState? state))
        {
            yield break;
        }

        // Stream existing updates starting from the specified position
        var streamedCount = startingAfter ?? 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Yield any available updates from the current position
            lock (state.StreamingUpdates)
            {
                while (streamedCount < state.StreamingUpdates.Count)
                {
                    yield return state.StreamingUpdates[streamedCount];
                    streamedCount++;
                }
            }

            // Check if we're done
            if (state.IsTerminal)
            {
                break;
            }

            // Wait for the next update to be signaled
            await state.UpdateSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<Response> CancelResponseAsync(string responseId, CancellationToken cancellationToken = default)
    {
        if (!this._responses.TryGetValue(responseId, out ResponseState? state))
        {
            throw new InvalidOperationException($"Response '{responseId}' not found.");
        }

        if (state.Response?.Background != true)
        {
            throw new InvalidOperationException($"Only background responses can be cancelled. Response '{responseId}' was not created with background=true.");
        }

        if (state.IsTerminal)
        {
            throw new InvalidOperationException($"Response '{responseId}' is already in a terminal state and cannot be cancelled.");
        }

        // Cancel the execution
        state.CancellationTokenSource?.Cancel();

        // Update response status
        state.Response = state.Response! with
        {
            Status = ResponseStatus.Cancelled
        };

        // Emit cancelled event
        var sequenceNumber = state.StreamingUpdates.Count + 1;
        var cancelledEvent = new StreamingResponseCancelled
        {
            SequenceNumber = sequenceNumber,
            Response = state.Response
        };

        lock (state.StreamingUpdates)
        {
            state.StreamingUpdates.Add(cancelledEvent);
        }

        // Signal that a new update is available
        state.UpdateSignal.Release();

        state.CompletionSource?.TrySetResult(true);

        return Task.FromResult(state.Response);
    }

    public Task<bool> DeleteResponseAsync(string responseId, CancellationToken cancellationToken = default)
    {
        if (!this._responses.TryGetValue(responseId, out ResponseState? state))
        {
            return Task.FromResult(false);
        }

        // Cancel any ongoing execution
        state.CancellationTokenSource?.Cancel();

        // Remove the response
        var removed = this._responses.TryRemove(responseId, out _);
        return Task.FromResult(removed);
    }

    public Task<ListResponse<ItemResource>> ListResponseInputItemsAsync(
        string responseId,
        int limit = 20,
        string order = "desc",
        string? after = null,
        string? before = null,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        if (!this._responses.TryGetValue(responseId, out ResponseState? state))
        {
            throw new InvalidOperationException($"Response '{responseId}' not found.");
        }

        var itemResources = GetInputItems(responseId, state);

        // Apply ordering
        if (order == "desc")
        {
            itemResources.Reverse();
        }

        // Apply pagination
        var filtered = itemResources.AsEnumerable();

        if (!string.IsNullOrEmpty(after))
        {
            int afterIndex = itemResources.FindIndex(m => m.Id == after);
            if (afterIndex >= 0)
            {
                filtered = itemResources.Skip(afterIndex + 1);
            }
        }

        if (!string.IsNullOrEmpty(before))
        {
            int beforeIndex = itemResources.FindIndex(m => m.Id == before);
            if (beforeIndex >= 0)
            {
                filtered = filtered.Take(beforeIndex);
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

    private void ValidateRequest(CreateResponse request)
    {
        if (request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id) &&
            !string.IsNullOrEmpty(request.PreviousResponseId))
        {
            throw new InvalidOperationException("Mutually exclusive parameters: 'conversation' and 'previous_response_id'. Ensure you are only providing one of: 'previous_response_id' or 'conversation'.");
        }
    }

    private ResponseState InitializeResponse(string responseId, CreateResponse request)
    {
        var metadata = request.Metadata ?? [];

        // Store conversation ID if provided
        if (request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id))
        {
            metadata["conversationId"] = request.Conversation.Id;
            if (request.Conversation.Metadata is not null)
            {
                foreach (var kvp in request.Conversation.Metadata)
                {
                    metadata[$"conversation.{kvp.Key}"] = kvp.Value;
                }
            }
        }

        // Create initial response
        // Background responses always start as "queued", non-background as "in_progress"
        var initialStatus = request.Background is true ? ResponseStatus.Queued : ResponseStatus.InProgress;
        var response = new Response
        {
            Id = responseId,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = request.Model ?? "default",
            Status = initialStatus,
            Error = null,
            IncompleteDetails = null,
            Output = [],
            Instructions = request.Instructions,
            Usage = ResponseUsage.Zero,
            ParallelToolCalls = request.ParallelToolCalls ?? true,
            Tools = [],
            ToolChoice = default,
            Temperature = request.Temperature,
            TopP = request.TopP,
            Metadata = metadata,
            Conversation = request.Conversation,
        };

        var state = new ResponseState
        {
            Response = response,
            Request = request,
            CompletionSource = new TaskCompletionSource<bool>(),
            CancellationTokenSource = new CancellationTokenSource()
        };

        if (!this._responses.TryAdd(responseId, state))
        {
            throw new InvalidOperationException($"Response with ID '{responseId}' already exists.");
        }

        return state;
    }

    private async Task ExecuteResponseAsync(string responseId, ResponseState state, CancellationToken cancellationToken)
    {
        var request = state.Request!;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, state.CancellationTokenSource!.Token);

        try
        {
            // Create agent invocation context
            var randomSeed = responseId.GetHashCode();
            var context = new AgentInvocationContext(new IdGenerator(responseId: responseId, conversationId: state.Response?.Conversation?.Id, randomSeed: randomSeed));

            // Execute using the injected executor
            await foreach (var streamingEvent in this._executor.ExecuteAsync(context, request, linkedCts.Token).ConfigureAwait(false))
            {
                lock (state.StreamingUpdates)
                {
                    state.StreamingUpdates.Add(streamingEvent);
                }

                // Update the response object for events that contain it
                if (streamingEvent is IStreamingResponseEventWithResponse responseEvent)
                {
                    state.Response = responseEvent.Response;
                }

                // Signal that a new update is available
                state.UpdateSignal.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Update response status to cancelled
            state.Response = state.Response! with
            {
                Status = ResponseStatus.Cancelled
            };

            var sequenceNumber = state.StreamingUpdates.Count + 1;
            var cancelledEvent = new StreamingResponseCancelled
            {
                SequenceNumber = sequenceNumber,
                Response = state.Response
            };

            lock (state.StreamingUpdates)
            {
                state.StreamingUpdates.Add(cancelledEvent);
            }

            // Signal that a new update is available
            state.UpdateSignal.Release();
        }
        catch (Exception ex)
        {
            // Update response status to failed
            state.Response = state.Response! with
            {
                Status = ResponseStatus.Failed,
                Error = new ResponseError
                {
                    Code = "execution_error",
                    Message = ex.Message
                }
            };

            var sequenceNumber = state.StreamingUpdates.Count + 1;
            var failedEvent = new StreamingResponseFailed
            {
                SequenceNumber = sequenceNumber,
                Response = state.Response
            };

            lock (state.StreamingUpdates)
            {
                state.StreamingUpdates.Add(failedEvent);
            }

            // Signal that a new update is available
            state.UpdateSignal.Release();
        }
        finally
        {
            // Release the semaphore one final time to unblock any waiting consumers
            state.UpdateSignal.Release();
            state.CompletionSource?.TrySetResult(true);
            linkedCts.Dispose();
        }
    }

    private static List<ItemResource> GetInputItems(string responseId, ResponseState state)
    {
        var itemResources = new List<ItemResource>();
        if (state.Request is not null)
        {
            // Use a deterministic random seed. We add 1 to avoid clashing with the output message ids.
            var randomSeed = responseId.GetHashCode() + 1;
            var idGenerator = new IdGenerator(responseId: responseId, conversationId: state.Response?.Conversation?.Id, randomSeed: randomSeed);
            foreach (var inputMessage in state.Request.Input.GetInputMessages())
            {
                itemResources.AddRange(inputMessage.ToItemResource(idGenerator));
            }
        }

        return itemResources;
    }
}
