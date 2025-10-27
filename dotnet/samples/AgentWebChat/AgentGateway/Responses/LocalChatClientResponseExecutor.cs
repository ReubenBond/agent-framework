// Copyright (c) Microsoft. All rights reserved.

using AgentGateway.Conversations;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.AI;

namespace AgentGateway.Responses;

/// <summary>
/// Response executor that uses a local IChatClient to execute responses using ChatClientAgent.
/// This is the original implementation that was embedded in ResponseGrain.
/// </summary>
public sealed class LocalChatClientResponseExecutor : IResponseExecutor
{
    private readonly IChatClient _chatClient;
    private readonly IGrainFactory _grainFactory;

    public LocalChatClientResponseExecutor(
        IChatClient chatClient,
        IGrainFactory grainFactory)
    {
        this._chatClient = chatClient;
        this._grainFactory = grainFactory;
    }

    public async IAsyncEnumerable<StreamingResponseEvent> ExecuteAsync(
        AgentInvocationContext context,
        CreateResponse request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agent = new ChatClientAgent(
            this._chatClient,
            instructions: request.Instructions,
            name: "ResponseAgent");

        var (messages, _) = await this.GetThreadAsync(request, agent, context.ConversationId, cancellationToken);

        var runOptions = request.ToRunOptions();

        // Use the extension method to convert streaming updates to streaming response events
        await foreach (var streamingEvent in agent.RunStreamingAsync(messages, agent.GetNewThread(), runOptions, cancellationToken)
            .ToStreamingResponseAsync(request, context, cancellationToken))
        {
            yield return streamingEvent;
        }
    }

    private async Task<(List<ChatMessage> Messages, string? LastMessageId)> GetThreadAsync(
        CreateResponse request,
        ChatClientAgent agent,
        string? conversationId,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>();
        string? lastMessageId = null;

        if (request.Conversation is not null && !string.IsNullOrEmpty(request.Conversation.Id))
        {
            conversationId = request.Conversation.Id;
            lastMessageId = await this.LoadConversationAsync(messages, conversationId, cancellationToken);
        }
        else if (!string.IsNullOrEmpty(request.PreviousResponseId))
        {
            var previousGrain = this._grainFactory.GetGrain<IResponseGrain>(request.PreviousResponseId);
            var previousResult = await previousGrain.GetWithThreadAsync(cancellationToken);

            if (previousResult is not null)
            {
                var (previousResponse, previousItems) = previousResult.Value;

                // Check if we have a conversation ID to load messages from
                conversationId = previousResponse.Conversation?.Id;
                if (conversationId is not null)
                {
                    lastMessageId = await this.LoadConversationAsync(messages, conversationId, cancellationToken);
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

        // Add input items from the current request
        foreach (var inputMessage in request.Input.GetInputMessages())
        {
            messages.Add(inputMessage.ToChatMessage());
        }

        return (messages, lastMessageId);
    }

    private async Task<string?> LoadConversationAsync(List<ChatMessage> messages, string conversationId, CancellationToken cancellationToken)
    {
        // Get the conversation messages
        var conversationGrain = this._grainFactory.GetGrain<IConversationGrain>(conversationId);

        string? lastMessageId = null;
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
