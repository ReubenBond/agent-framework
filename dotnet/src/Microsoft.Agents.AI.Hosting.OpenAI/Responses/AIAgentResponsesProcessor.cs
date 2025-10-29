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
    public static async Task<IResult> CreateModelResponseAsync(AIAgent agent, CreateResponse request, HttpContext? httpContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var context = new AgentInvocationContext(idGenerator: IdGenerator.From(request));

        // Extract the Response ID from the X-Response-ID header if present
        string? persistenceKey = null;
        if (httpContext is not null && httpContext.Request.Headers.TryGetValue("X-Response-ID", out var responseIdHeader))
        {
            persistenceKey = responseIdHeader.ToString();
        }

        if (request.Stream == true)
        {
            return new StreamingResponse(agent, request, context, persistenceKey);
        }

        var messages = request.Input.GetInputMessages().Select(i => i.ToChatMessage());

        // Create options with persistence key if available
        ChatClientAgentRunOptions? options = null;
        if (persistenceKey is not null)
        {
            var chatOptions = new ChatOptions();
            chatOptions.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["PersistenceKey"] = persistenceKey
            };
            options = new ChatClientAgentRunOptions(chatOptions);
        }

        var response = await agent.RunAsync(messages, options: options, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Results.Ok(response.ToResponse(request, context));
    }

    private sealed class StreamingResponse(AIAgent agent, CreateResponse createResponse, AgentInvocationContext context, string? persistenceKey) : IResult
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

            // Create options with persistence key if available
            ChatClientAgentRunOptions? options = null;
            if (persistenceKey is not null)
            {
                var chatOptions = new ChatOptions();
                chatOptions.AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["PersistenceKey"] = persistenceKey
                };
                options = new ChatClientAgentRunOptions(chatOptions);
            }

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
