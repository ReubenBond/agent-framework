// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Converters;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using AgentGateway.Conversations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentGateway.Responses;

/// <summary>
/// State for a response grain, containing the response metadata.
/// </summary>
[GenerateSerializer]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class ResponseState
{
    /// <summary>
    /// The response metadata.
    /// </summary>
    [Id(0)]
    public Response? Response { get; set; }

    /// <summary>
    /// The original request, stored for background execution and debugging.
    /// </summary>
    [Id(2)]
    public CreateResponse? Request { get; set; }

    /// <summary>
    /// The streaming updates collected during response generation.
    /// </summary>
    [Id(3)]
    public List<StreamingResponseEvent> StreamingUpdates { get; set; } = [];

    /// <summary>
    /// The last message ID in the conversation before execution started.
    /// Used for idempotent message appending.
    /// </summary>
    [Id(4)]
    public string? LastMessageIdBeforeExecution { get; set; }
}

/// <summary>
/// Grain interface for managing a single response.
/// </summary>
public interface IResponseGrain : IGrainWithStringKey
{
    /// <summary>
    /// Creates a new response and generates it using the ChatClientAgent.
    /// </summary>
    /// <param name="request">The request to create the response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created response.</returns>
    Task<Response> CreateAsync(CreateResponse request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a streaming response using the ChatClientAgent.
    /// </summary>
    /// <param name="request">The request to create the response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of streaming updates.</returns>
    IAsyncEnumerable<StreamingResponseEvent> CreateStreamingAsync(CreateResponse request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the response.
    /// </summary>
    /// <returns>The response if it exists, null otherwise.</returns>
    Task<Response?> GetAsync();

    /// <summary>
    /// Lists the input items for this response.
    /// </summary>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="order">Sort order.</param>
    /// <param name="after">Return items after this ID.</param>
    /// <param name="before">Return items before this ID.</param>
    /// <returns>A list response with items and pagination info.</returns>
    Task<ListResponse<ItemResource>> ListInputItemsAsync(int limit, string order, string? after, string? before);

    /// <summary>
    /// Gets the response and its full thread (input + output messages), waiting for completion if necessary.
    /// This is more efficient than calling GetAsync() followed by ListInputItemsAsync() when you need both.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the response and the full list of items (input + output), or null if the response doesn't exist.</returns>
    Task<(Response Response, List<ItemResource> Items)?> GetWithThreadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Orleans grain implementation for managing a response.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by Orleans framework")]
internal sealed class ResponseGrain(
    [PersistentState("state")] IPersistentState<ResponseState> responseState,
    IChatClient chatClient,
    ILogger<ResponseGrain> logger) : Grain, IResponseGrain, IRemindable, IDisposable
{
    private const string BackgroundExecutionReminderName = "BackgroundExecution";
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly AsyncManualResetEvent _streamingUpdatedEvent = new();
    private Task? _executionTask;

    private string ResponseId => this.GetPrimaryKeyString();

    private string? ConversationId => responseState.State.Response?.Conversation?.Id;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // If we have a pending request that's not completed, ensure reminder is registered and start execution
        if (responseState.State.Request is not null &&
            responseState.State.Response is { IsTerminal: false })
        {
            // Ensure reminder is registered for background execution
            await this.RegisterOrUpdateReminder(
                BackgroundExecutionReminderName,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(1));

            this._executionTask = this.RunAsync(this._shutdownCts.Token);
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await this._shutdownCts.CancelAsync();
        this._streamingUpdatedEvent.Cancel();

        if (this._executionTask is not null)
        {
            try
            {
                await this._executionTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }
    }

    public async Task<Response> CreateAsync(CreateResponse request, CancellationToken cancellationToken)
    {
        if (responseState.State.Response is not null)
        {
            throw new InvalidOperationException($"Response with ID '{this.ResponseId}' already exists.");
        }

        // Store the request and create initial response
        await this.InitializeResponseAsync(request, cancellationToken);
        Debug.Assert(responseState.State.Response is not null);

        // If background execution is requested, return immediately with queued status
        if (request.Background == true)
        {
            return responseState.State.Response;
        }

        // Start execution and wait for completion
        Debug.Assert(this._executionTask is null);
        this._executionTask = this.RunAsync(this._shutdownCts.Token);

        // Wait for completion by watching for terminal status
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (responseState.State.Response.IsTerminal)
            {
                return responseState.State.Response;
            }

            // Wait for the next update event
            await this._streamingUpdatedEvent.WaitAsync(cancellationToken);
        }
    }

    public async IAsyncEnumerable<StreamingResponseEvent> CreateStreamingAsync(
        CreateResponse request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (responseState.State.Response is not null)
        {
            throw new InvalidOperationException($"Response with ID '{this.ResponseId}' already exists.");
        }

        // Store the request and create initial response
        await this.InitializeResponseAsync(request, cancellationToken);
        Debug.Assert(responseState.State.Response is not null);

        // Start execution
        this._executionTask = this.RunAsync(this._shutdownCts.Token);

        // Stream updates as they become available
        var streamedCount = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Yield any new updates
            while (streamedCount < responseState.State.StreamingUpdates.Count)
            {
                yield return responseState.State.StreamingUpdates[streamedCount];
                streamedCount++;
            }

            // Check if we're done
            if (responseState.State.Response.IsTerminal)
            {
                break;
            }

            // Wait for more updates
            await this._streamingUpdatedEvent.WaitAsync(cancellationToken);
        }

        await this._executionTask.WaitAsync(cancellationToken);
    }

    public Task<Response?> GetAsync()
    {
        return Task.FromResult(responseState.State.Response);
    }

    public Task<ListResponse<ItemResource>> ListInputItemsAsync(int limit, string order, string? after, string? before)
    {
        if (responseState.State.Response is null)
        {
            // Return empty list if response doesn't exist yet
            return Task.FromResult(new ListResponse<ItemResource>
            {
                Data = [],
                FirstId = null,
                LastId = null,
                HasMore = false
            });
        }

        limit = Math.Clamp(limit, 1, 100);

        // Generate input items (messages) from the stored request
        var itemResources = new List<ItemResource>();
        if (responseState.State.Request is not null)
        {
            // Use a simple IdGenerator for generating IDs
            var idGenerator = new IdGenerator(this.ConversationId, this.ResponseId);

            foreach (var inputMessage in responseState.State.Request.Input.GetInputMessages())
            {
                itemResources.AddRange(inputMessage.ToItemResource(idGenerator));
            }
        }

        var limitedItems = itemResources.Take(limit).ToList();

        return Task.FromResult(new ListResponse<ItemResource>
        {
            Data = limitedItems,
            FirstId = limitedItems.Count > 0 ? limitedItems[0].Id : null,
            LastId = limitedItems.Count > 0 ? limitedItems[^1].Id : null,
            HasMore = itemResources.Count > limit
        });
    }

    public async Task<(Response Response, List<ItemResource> Items)?> GetWithThreadAsync(CancellationToken cancellationToken = default)
    {
        if (responseState.State.Response is null)
        {
            return null;
        }

        // Wait for completion if not in a terminal state
        while (!responseState.State.Response.IsTerminal)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await this._streamingUpdatedEvent.WaitAsync(cancellationToken);
        }

        var response = responseState.State.Response;
        var items = new List<ItemResource>();

        // Add input items
        if (responseState.State.Request is not null)
        {
            var idGenerator = new IdGenerator(this.ConversationId, this.ResponseId);

            foreach (var inputMessage in responseState.State.Request.Input.GetInputMessages())
            {
                items.AddRange(inputMessage.ToItemResource(idGenerator));
            }
        }

        // Add output items - they're already ItemResource
        items.AddRange(response.Output);

        return (response, items);
    }

    private async Task<(List<ChatMessage> Messages, string? LastMessageId, AgentThread? Thread)> GetThreadAsync(CreateResponse request, ChatClientAgent agent, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();
        string? lastMessageId = null;
        string? conversationId;

        if (request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id))
        {
            conversationId = request.Conversation.Id;
            lastMessageId = await LoadConversationAsync(messages, lastMessageId, conversationId);
        }
        else if (!string.IsNullOrEmpty(request.PreviousResponseId))
        {
            var previousGrain = this.GrainFactory.GetGrain<IResponseGrain>(request.PreviousResponseId);
            var previousResult = await previousGrain.GetWithThreadAsync(cancellationToken);

            if (previousResult is not null)
            {
                var (previousResponse, previousItems) = previousResult.Value;

                // Check if we have a conversation ID to load messages from
                conversationId = previousResponse.Conversation?.Id;
                if (conversationId is not null)
                {
                    lastMessageId = await LoadConversationAsync(messages, lastMessageId, conversationId);
                }
                else
                {
                    // Use the thread from the previous response directly
                    foreach (var item in previousItems)
                    {
                        messages.Add(item.ToChatMessage());
                    }
                    // Track the last message ID from the previous response's output
                    if (previousResponse.Output.Count > 0)
                    {
                        lastMessageId = previousResponse.Output[^1].Id;
                    }
                }
            }
        }

        // Add the new input messages
        foreach (var inputMessage in request.Input.GetInputMessages())
        {
            messages.Add(inputMessage.ToChatMessage());
        }

        var thread = agent.GetNewThread();
        return (messages, lastMessageId, thread);

        async Task<string?> LoadConversationAsync(List<ChatMessage> messages, string? lastMessageId, string conversationId)
        {
            // Get the conversation messages
            var conversationGrain = this.GrainFactory.GetGrain<IConversationGrain>(conversationId);

            // Use GetAllItemsAsync to stream all items
            await foreach (var itemResource in conversationGrain.GetAllItemsAsync(SortOrder.Ascending))
            {
                // Convert ItemResource to ChatMessage using extension method
                var chatMessage = itemResource.ToChatMessage();
                messages.Add(chatMessage);
                lastMessageId = itemResource.Id; // Track the last message ID
            }

            return lastMessageId;
        }
    }

    /// <summary>
    /// Initializes the response state with the request and creates the initial response object.
    /// </summary>
    private async Task InitializeResponseAsync(CreateResponse request, CancellationToken cancellationToken)
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
        var response = new Response
        {
            Id = this.ResponseId,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = request.Model ?? "default",
            Status = request.Background is true ? ResponseStatus.Queued : ResponseStatus.InProgress,
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

        // Store the request and response
        responseState.State.Request = request;
        responseState.State.Response = response;
        responseState.State.StreamingUpdates.Clear();

        // Emit response.created event
        var createdEvent = new StreamingResponseCreated
        {
            SequenceNumber = 1,
            Response = response
        };
        responseState.State.StreamingUpdates.Add(createdEvent);

        await responseState.WriteStateAsync(cancellationToken);

        await this.RegisterOrUpdateReminder(
            BackgroundExecutionReminderName,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Executes the response generation in streaming mode in the background.
    /// This is the single execution path for all modes (blocking, async, streaming).
    /// This method is idempotent and can be safely called multiple times.
    /// </summary>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // Yield immediately so the task can be captured and can run in the background.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.ContinueOnCapturedContext);

        var request = responseState.State.Request;
        Debug.Assert(request is not null);

        var response = responseState.State.Response;
        Debug.Assert(response is not null);

        // Check if already in a terminal state - idempotency check
        try
        {
            if (!response.IsTerminal)
            {
                await this.ProcessRequestAsync(cancellationToken);
            }

            await this.FinalizeResponseAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing streaming response {ResponseId}", this.ResponseId);

            // Update response status to failed
            responseState.State.Response = responseState.State.Response! with
            {
                Status = ResponseStatus.Failed,
                Error = new ResponseError
                {
                    Code = "execution_error",
                    Message = ex.Message
                }
            };

            // Emit failed event
            var sequenceNumber = responseState.State.StreamingUpdates.Count + 1;
            var failedEvent = new StreamingResponseFailed
            {
                SequenceNumber = sequenceNumber,
                Response = responseState.State.Response
            };
            responseState.State.StreamingUpdates.Add(failedEvent);

            await responseState.WriteStateAsync(cancellationToken);
            this._streamingUpdatedEvent.SignalAndReset();
        }
    }

    private async Task ProcessRequestAsync(CancellationToken cancellationToken)
    {
        var request = responseState.State.Request;
        Debug.Assert(request is not null);
        var response = responseState.State.Response;
        Debug.Assert(response is not null);

        // Update status to in_progress and emit event
        if (response.Status != ResponseStatus.InProgress)
        {
            responseState.State.Response = response with { Status = ResponseStatus.InProgress };
            await responseState.WriteStateAsync(cancellationToken);

            // Emit in_progress event
            var inProgressEvent = new StreamingResponseInProgress
            {
                SequenceNumber = responseState.State.StreamingUpdates.Count + 1,
                Response = responseState.State.Response
            };
            responseState.State.StreamingUpdates.Add(inProgressEvent);
            await responseState.WriteStateAsync(cancellationToken);
            this._streamingUpdatedEvent.SignalAndReset();
        }

        var agent = new ChatClientAgent(
            chatClient,
            instructions: request.Instructions,
            name: "ResponseAgent");

        var (messages, lastMessageId, thread) = await this.GetThreadAsync(request, agent, cancellationToken);

        // Store the last message ID before execution for idempotent appending
        responseState.State.LastMessageIdBeforeExecution = lastMessageId;

        var runOptions = request.ToRunOptions();

        // Create ID generator and JSON options for converting messages
        var idGenerator = new IdGenerator(this.ConversationId, this.ResponseId);
        var jsonOptions = JsonSerializerOptions.Default;

        // Track streaming state
        var sequenceNumber = responseState.State.StreamingUpdates.Count + 1;
        var outputIndex = 0;
        var allUpdates = new List<AgentRunResponseUpdate>();
        string? currentItemId = null;
        var currentContentParts = new Dictionary<int, (AIContent Content, System.Text.StringBuilder? TextBuilder)>();
        var maxContentIndex = -1;

        // Stream the response and emit events following OpenAI spec
        await foreach (var update in agent.RunStreamingAsync(messages, thread, runOptions, cancellationToken))
        {
            allUpdates.Add(update);

            // Check if this is a new message
            if (!string.IsNullOrEmpty(update.MessageId) && update.MessageId != currentItemId)
            {
                // Complete previous item if exists
                if (currentItemId is not null && allUpdates.Count > 1)
                {
                    var previousUpdates = allUpdates.Take(allUpdates.Count - 1).Where(u => u.MessageId == currentItemId).ToList();
                    var previousMessage = previousUpdates.ToAgentRunResponse().Messages[0];
                    var previousItems = previousMessage.ToItemResource(idGenerator, jsonOptions).ToList();

                    // Emit done events for all content parts
                    foreach (var kvp in currentContentParts.OrderBy(x => x.Key))
                    {
                        if (kvp.Value.Content is TextContent && kvp.Value.TextBuilder?.Length > 0)
                        {
                            var textDoneEvent = new StreamingOutputTextDone
                            {
                                SequenceNumber = sequenceNumber++,
                                ItemId = currentItemId,
                                OutputIndex = outputIndex,
                                ContentIndex = kvp.Key,
                                Text = kvp.Value.TextBuilder.ToString()
                            };
                            responseState.State.StreamingUpdates.Add(textDoneEvent);
                        }

                        var itemContent = ItemContentConverter.ToItemContent(kvp.Value.Content);
                        if (itemContent is not null)
                        {
                            var contentDoneEvent = new StreamingContentPartDone
                            {
                                SequenceNumber = sequenceNumber++,
                                ItemId = currentItemId,
                                OutputIndex = outputIndex,
                                ContentIndex = kvp.Key,
                                Part = itemContent
                            };
                            responseState.State.StreamingUpdates.Add(contentDoneEvent);
                        }
                    }

                    var itemDoneEvent = new StreamingOutputItemDone
                    {
                        SequenceNumber = sequenceNumber++,
                        OutputIndex = outputIndex,
                        Item = previousItems[0] // Use the first converted item
                    };
                    responseState.State.StreamingUpdates.Add(itemDoneEvent);
                }

                // Start new item
                currentItemId = update.MessageId;
                currentContentParts.Clear();
                maxContentIndex = -1;
                outputIndex++;

                // Create a placeholder item for output_item.added event
                var placeholderMessage = new ChatMessage(ChatRole.Assistant, [])
                {
                    MessageId = currentItemId,
                    CreatedAt = update.CreatedAt
                };
                var placeholderItems = placeholderMessage.ToItemResource(idGenerator, jsonOptions).ToList();

                var itemAddedEvent = new StreamingOutputItemAdded
                {
                    SequenceNumber = sequenceNumber++,
                    OutputIndex = outputIndex,
                    Item = placeholderItems.Count > 0 ? placeholderItems[0] : new ResponsesAssistantMessageItemResource
                    {
                        Id = currentItemId,
                        Status = ResponsesMessageItemResourceStatus.Completed,
                        Content = []
                    }
                };
                responseState.State.StreamingUpdates.Add(itemAddedEvent);
            }

            // Process all content items in this update
            if (update.Contents is { Count: > 0 } && currentItemId is not null)
            {
                foreach (var content in update.Contents)
                {
                    // Skip usage content as it's handled separately
                    if (content is UsageContent usageContent)
                    {
                        if (usageContent.Details is not null)
                        {
                            responseState.State.Response = responseState.State.Response! with
                            {
#pragma warning disable CS8601 // Possible null reference assignment
                                Usage = usageContent.Details.ToResponseUsage()
#pragma warning restore CS8601
                            };
                        }
                        continue;
                    }

                    // Determine content index - for streaming, text usually comes in the same index
                    var contentIndex = content switch
                    {
                        TextContent => 0, // Text content is typically at index 0
                        FunctionCallContent fc => currentContentParts.Values
                            .Select((v, i) => (v, i))
                            .FirstOrDefault(x => x.v.Content is FunctionCallContent fcc && fcc.CallId == fc.CallId)
                            .i,
                        DataContent or UriContent => ++maxContentIndex, // Images and other media
                        _ => ++maxContentIndex
                    };

                    // Track or update content part
                    if (!currentContentParts.TryGetValue(contentIndex, out var existingContent))
                    {
                        currentContentParts[contentIndex] = (content, content is TextContent ? new System.Text.StringBuilder() : null);
                        maxContentIndex = Math.Max(maxContentIndex, contentIndex);

                        // Emit content_part.added event
                        var itemContent = ItemContentConverter.ToItemContent(content);
                        if (itemContent is not null)
                        {
                            var partAddedEvent = new StreamingContentPartAdded
                            {
                                SequenceNumber = sequenceNumber++,
                                ItemId = currentItemId,
                                OutputIndex = outputIndex,
                                ContentIndex = contentIndex,
                                Part = itemContent
                            };
                            responseState.State.StreamingUpdates.Add(partAddedEvent);
                        }
                    }
                    else
                    {
                        // Update existing content part (e.g., accumulating text)
                        if (content is TextContent tc && existingContent.TextBuilder is not null)
                        {
                            currentContentParts[contentIndex] = (content, existingContent.TextBuilder);
                        }
                        else if (content is FunctionCallContent fc)
                        {
                            // Update function call content (arguments may be streaming)
                            currentContentParts[contentIndex] = (content, null);
                        }
                    }

                    // Emit specific delta events based on content type
                    if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    {
                        currentContentParts[contentIndex].TextBuilder?.Append(textContent.Text);

                        var textDeltaEvent = new StreamingOutputTextDelta
                        {
                            SequenceNumber = sequenceNumber++,
                            ItemId = currentItemId,
                            OutputIndex = outputIndex,
                            ContentIndex = contentIndex,
                            Delta = textContent.Text
                        };
                        responseState.State.StreamingUpdates.Add(textDeltaEvent);
                    }
                    // Note: For function calls, images, etc., the content_part.added event is sufficient
                    // The OpenAI spec has specific events for function_call_arguments.delta, but we emit
                    // content_part.added which contains the full function call information
                }
            }

            await responseState.WriteStateAsync(cancellationToken);
            this._streamingUpdatedEvent.SignalAndReset();
        }

        // Complete the last item
        if (currentItemId is not null)
        {
            var finalUpdates = allUpdates.Where(u => u.MessageId == currentItemId).ToList();
            var finalMessage = finalUpdates.ToAgentRunResponse().Messages[0];
            var finalItems = finalMessage.ToItemResource(idGenerator, jsonOptions).ToList();

            // Emit done events for all content parts
            foreach (var kvp in currentContentParts.OrderBy(x => x.Key))
            {
                if (kvp.Value.Content is TextContent && kvp.Value.TextBuilder?.Length > 0)
                {
                    var textDoneEvent = new StreamingOutputTextDone
                    {
                        SequenceNumber = sequenceNumber++,
                        ItemId = currentItemId,
                        OutputIndex = outputIndex,
                        ContentIndex = kvp.Key,
                        Text = kvp.Value.TextBuilder.ToString()
                    };
                    responseState.State.StreamingUpdates.Add(textDoneEvent);
                }

                var itemContent = ItemContentConverter.ToItemContent(kvp.Value.Content);
                if (itemContent is not null)
                {
                    var contentDoneEvent = new StreamingContentPartDone
                    {
                        SequenceNumber = sequenceNumber++,
                        ItemId = currentItemId,
                        OutputIndex = outputIndex,
                        ContentIndex = kvp.Key,
                        Part = itemContent
                    };
                    responseState.State.StreamingUpdates.Add(contentDoneEvent);
                }
            }

            var itemDoneEvent = new StreamingOutputItemDone
            {
                SequenceNumber = sequenceNumber++,
                OutputIndex = outputIndex,
                Item = finalItems[0] // Use the first converted item
            };
            responseState.State.StreamingUpdates.Add(itemDoneEvent);
        }

        var output = allUpdates.ToAgentRunResponse();

        // Update the response with completion data
        responseState.State.Response = responseState.State.Response! with
        {
            Status = ResponseStatus.Completed,
            Error = null,
            IncompleteDetails = null,
            Output = output.Messages.SelectMany(msg => msg.ToItemResource(idGenerator, jsonOptions)).ToList(),
#pragma warning disable CS8601 // Possible null reference assignment
            Usage = output.Usage.ToResponseUsage()
#pragma warning restore CS8601
        };

        // Emit completed event
        var completedEvent = new StreamingResponseCompleted
        {
            SequenceNumber = sequenceNumber++,
            Response = responseState.State.Response
        };
        responseState.State.StreamingUpdates.Add(completedEvent);

        await responseState.WriteStateAsync(cancellationToken);
        this._streamingUpdatedEvent.SignalAndReset();
    }

    private async Task FinalizeResponseAsync(CancellationToken cancellationToken)
    {
        var request = responseState.State.Request;
        Debug.Assert(request is not null);
        var response = responseState.State.Response;
        Debug.Assert(response is not null);

        // Append messages to the conversation if applicable
        if (response.Status == ResponseStatus.Completed && request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id))
        {
            var conversationGrain = this.GrainFactory.GetGrain<IConversationGrain>(request.Conversation.Id);

            // Use the last message ID captured before execution started for idempotency
            var lastMessageIdBeforeExecution = responseState.State.LastMessageIdBeforeExecution;

            // Build the list of messages to append
            var messagesToAppend = new List<ItemResource>();
            var idGenerator = new IdGenerator(this.ConversationId, this.ResponseId);

            foreach (var inputMessage in request.Input.GetInputMessages())
            {
                messagesToAppend.AddRange(inputMessage.ToItemResource(idGenerator));
            }

            // Add output items - they're already ItemResource
            messagesToAppend.AddRange(response.Output);

            var appendedCount = await conversationGrain.AppendItemsAsync(messagesToAppend, lastMessageIdBeforeExecution);
            if (appendedCount != messagesToAppend.Count)
            {
                logger.LogWarning("Appended {AppendedCount} out of {TotalCount} messages to conversation {ConversationId} for response {ResponseId}",
                    appendedCount, messagesToAppend.Count, request.Conversation.Id, this.ResponseId);
            }
        }

        var reminder = await this.GetReminder(BackgroundExecutionReminderName);
        if (reminder is not null)
        {
            await this.UnregisterReminder(reminder);
        }
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName == BackgroundExecutionReminderName && this._executionTask?.IsCompleted != false)
        {
            this._executionTask = this.RunAsync(this._shutdownCts.Token);
        }
    }

    public void Dispose()
    {
        ((IDisposable)this._shutdownCts).Dispose();
    }
}
