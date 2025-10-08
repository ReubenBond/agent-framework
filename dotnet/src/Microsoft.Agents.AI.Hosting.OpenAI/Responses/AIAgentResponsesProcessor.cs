// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;

/// <summary>
/// OpenAI Responses processor for <see cref="AIAgent"/>.
/// </summary>
internal static class AIAgentResponsesProcessor
{
    public static async Task<IResult> CreateModelResponseAsync(AIAgent agent, CreateResponse request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var context = new AgentInvocationContext(idGenerator: IdGenerator.From(request));

        // Create options with properties from the request
        var chatOptions = new ChatOptions
        {
            ConversationId = request.Conversation?.Id,
            Temperature = (float?)request.Temperature,
            TopP = (float?)request.TopP,
            MaxOutputTokens = request.MaxOutputTokens,
            Instructions = request.Instructions,
            ModelId = request.Model,
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
            AllowBackgroundResponses = request.Background,
#pragma warning restore MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [nameof(CreateResponse)] = request
            }
        };
        var options = new ChatClientAgentRunOptions(chatOptions);

        if (request.Stream == true)
        {
            return new StreamingResponse(agent, request, context, options);
        }

        var messages = request.Input.GetInputMessages().Select(i => i.ToChatMessage());

        var response = await agent.RunAsync(messages, options: options, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Results.Ok(response.ToResponse(request, context));
    }

    private sealed class StreamingResponse(AIAgent agent, CreateResponse createResponse, AgentInvocationContext context, ChatClientAgentRunOptions? options) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var cancellationToken = httpContext.RequestAborted;
            var response = httpContext.Response;

            // Set SSE headers
            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache,no-store";
            response.Headers.Connection = "keep-alive";
            response.Headers.ContentEncoding = "identity";
            httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>().DisableBuffering();

            var chatMessages = createResponse.Input.GetInputMessages().Select(i => i.ToChatMessage()).ToList();

            var events = agent.RunStreamingAsync(chatMessages, options: options, cancellationToken: cancellationToken)
                .ToStreamingResponseAsync(createResponse, context, cancellationToken)
                .Select(static evt => new SseItem<StreamingResponseEvent>(evt, evt.Type));
            return SseFormatter.WriteAsync(
                source: events,
                destination: response.Body,
                itemFormatter: static (sseItem, bufferWriter) =>
                {
                    using var writer = new Utf8JsonWriter(bufferWriter);
                    JsonSerializer.Serialize(writer, sseItem.Data, OpenAIJsonContext.Default.StreamingResponseEvent);
                    writer.Flush();
                },
                cancellationToken);
        }
    }
}
