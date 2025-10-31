// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Provides extension methods for mapping OpenAI capabilities to an <see cref="AIAgent"/>.
/// </summary>
public static partial class MicrosoftAgentAIHostingOpenAIEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps OpenAI Responses API endpoints to the specified <see cref="IEndpointRouteBuilder"/> for the given <see cref="AIAgent"/>.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the OpenAI Responses endpoints to.</param>
    /// <param name="agent">The <see cref="AIAgent"/> instance to map the OpenAI Responses endpoints for.</param>
    public static IEndpointConventionBuilder MapOpenAIResponses(this IEndpointRouteBuilder endpoints, AIAgent agent) =>
        MapOpenAIResponses(endpoints, agent, responsesPath: null);

    /// <summary>
    /// Maps OpenAI Responses API endpoints to the specified <see cref="IEndpointRouteBuilder"/> for the given <see cref="AIAgent"/>.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the OpenAI Responses endpoints to.</param>
    /// <param name="agent">The <see cref="AIAgent"/> instance to map the OpenAI Responses endpoints for.</param>
    /// <param name="responsesPath">Custom route path for the responses endpoint.</param>
    public static IEndpointConventionBuilder MapOpenAIResponses(
        this IEndpointRouteBuilder endpoints,
        AIAgent agent,
        [StringSyntax("Route")] string? responsesPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(agent.Name, nameof(agent.Name));
        ValidateAgentName(agent.Name);

        responsesPath ??= $"/{agent.Name}/v1/responses";
        var group = endpoints.MapGroup(responsesPath);
        var endpointAgentName = agent.DisplayName;
        group.MapPost("/", async ([FromBody] CreateResponse createResponse, CancellationToken cancellationToken)
            => await AIAgentResponsesProcessor.CreateModelResponseAsync(agent, createResponse, cancellationToken).ConfigureAwait(false))
            .WithName(endpointAgentName + "/CreateResponse");
        return group;
    }

    /// <summary>
    /// Maps OpenAI Responses API endpoints to the specified <see cref="IEndpointRouteBuilder"/>.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the OpenAI Responses endpoints to.</param>
    public static IEndpointConventionBuilder MapOpenAIResponses(this IEndpointRouteBuilder endpoints) =>
        MapOpenAIResponses(endpoints, responsesPath: null);

    /// <summary>
    /// Maps OpenAI Responses API endpoints to the specified <see cref="IEndpointRouteBuilder"/>.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the OpenAI Responses endpoints to.</param>
    /// <param name="responsesPath">Custom route path for the responses endpoint.</param>
    public static IEndpointConventionBuilder MapOpenAIResponses(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string? responsesPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        responsesPath ??= "/v1/responses";
        var group = endpoints.MapGroup(responsesPath);

        // Create response endpoint
        group.MapPost("/", CreateResponseAsync)
            .WithName("CreateResponse")
            .WithSummary("Creates a model response for the given input");

        // Get response endpoint
        group.MapGet("{responseId}", GetResponseAsync)
            .WithName("GetResponse")
            .WithSummary("Retrieves a response by ID");

        // Cancel response endpoint
        group.MapPost("{responseId}/cancel", CancelResponseAsync)
            .WithName("CancelResponse")
            .WithSummary("Cancels an in-progress response");

        // Delete response endpoint
        group.MapDelete("{responseId}", DeleteResponseAsync)
            .WithName("DeleteResponse")
            .WithSummary("Deletes a response");

        // List response input items endpoint
        group.MapGet("{responseId}/input_items", ListResponseInputItemsAsync)
            .WithName("ListResponseInputItems")
            .WithSummary("Lists the input items for a response");

        return group;
    }

    private static async Task<IResult> CreateResponseAsync(
        [FromBody] CreateResponse request,
        [FromServices] IResponsesService? responsesService,
        [FromQuery] bool? stream = null,
        CancellationToken cancellationToken = default)
    {
        if (responsesService is null)
        {
            return Results.Problem(
                detail: "IResponsesService is not registered. Call AddOpenAIResponses() in your service configuration.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Service not configured");
        }

        try
        {
            // Handle streaming vs non-streaming
            bool shouldStream = stream ?? request.Stream ?? false;

            if (shouldStream)
            {
                var streamingResponse = responsesService.CreateResponseStreamingAsync(
                    request,
                    cancellationToken: cancellationToken);

                return Results.Stream(async outputStream =>
                {
                    await foreach (var update in streamingResponse.ConfigureAwait(false))
                    {
                        // Write SSE format: data: {json}\n\n
                        var json = JsonSerializer.Serialize(update, OpenAIJsonContext.Default.StreamingResponseEvent);
                        await outputStream.WriteAsync(Encoding.UTF8.GetBytes($"data: {json}\n\n"), cancellationToken).ConfigureAwait(false);
                        await outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }, "text/event-stream");
            }

            var response = await responsesService.CreateResponseAsync(
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return Results.Ok(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Mutually exclusive"))
        {
            // Return OpenAI-style error for mutual exclusivity violations
            return Results.BadRequest(new
            {
                error = new
                {
                    message = ex.Message,
                    type = "invalid_request_error",
                    code = "mutually_exclusive_parameters"
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found") || ex.Message.Contains("does not exist"))
        {
            // Return OpenAI-style error for not found errors
            return Results.NotFound(new
            {
                error = new
                {
                    message = ex.Message,
                    type = "invalid_request_error"
                }
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error creating response");
        }
    }

    private static async Task<IResult> GetResponseAsync(
        string responseId,
        [FromServices] IResponsesService? responsesService,
        [FromQuery] string[]? include = null,
        [FromQuery] bool? stream = null,
        [FromQuery] int? starting_after = null,
        CancellationToken cancellationToken = default)
    {
        if (responsesService is null)
        {
            return Results.Problem(
                detail: "IResponsesService is not registered. Call AddOpenAIResponses() in your service configuration.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Service not configured");
        }

        try
        {
            // If streaming is requested, return SSE stream
            if (stream == true)
            {
                var streamingResponse = responsesService.GetResponseStreamingAsync(
                    responseId,
                    startingAfter: starting_after,
                    cancellationToken: cancellationToken);

                return Results.Stream(async outputStream =>
                {
                    await foreach (var update in streamingResponse.ConfigureAwait(false))
                    {
                        // Write SSE format: data: {json}\n\n
                        var json = JsonSerializer.Serialize(update, OpenAIJsonContext.Default.StreamingResponseEvent);
                        await outputStream.WriteAsync(Encoding.UTF8.GetBytes($"data: {json}\n\n"), cancellationToken).ConfigureAwait(false);
                        await outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }, "text/event-stream");
            }

            // Non-streaming: return the response object
            var response = await responsesService.GetResponseAsync(responseId, cancellationToken).ConfigureAwait(false);
            return response is not null
                ? Results.Ok(response)
                : Results.NotFound(new { error = new { message = $"Response '{responseId}' not found.", type = "invalid_request_error" } });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Response not found");
        }
    }

    private static async Task<IResult> CancelResponseAsync(
        string responseId,
        [FromServices] IResponsesService? responsesService,
        CancellationToken cancellationToken = default)
    {
        if (responsesService is null)
        {
            return Results.Problem(
                detail: "IResponsesService is not registered. Call AddOpenAIResponses() in your service configuration.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Service not configured");
        }

        try
        {
            var response = await responsesService.CancelResponseAsync(responseId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new
            {
                error = new
                {
                    message = ex.Message,
                    type = "invalid_request_error"
                }
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error cancelling response");
        }
    }

    private static async Task<IResult> DeleteResponseAsync(
        string responseId,
        [FromServices] IResponsesService? responsesService,
        CancellationToken cancellationToken = default)
    {
        if (responsesService is null)
        {
            return Results.Problem(
                detail: "IResponsesService is not registered. Call AddOpenAIResponses() in your service configuration.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Service not configured");
        }

        try
        {
            var deleted = await responsesService.DeleteResponseAsync(responseId, cancellationToken).ConfigureAwait(false);
            return deleted
                ? Results.Ok(new { id = responseId, deleted = true })
                : Results.NotFound(new { error = new { message = $"Response '{responseId}' not found.", type = "invalid_request_error" } });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error deleting response");
        }
    }

    private static async Task<IResult> ListResponseInputItemsAsync(
        string responseId,
        [FromServices] IResponsesService? responsesService,
        [FromQuery] int limit = 20,
        [FromQuery] string order = "desc",
        [FromQuery] string? after = null,
        [FromQuery] string? before = null,
        CancellationToken cancellationToken = default)
    {
        if (responsesService is null)
        {
            return Results.Problem(
                detail: "IResponsesService is not registered. Call AddOpenAIResponses() in your service configuration.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Service not configured");
        }

        try
        {
            var result = await responsesService.ListResponseInputItemsAsync(
                responseId,
                limit,
                order,
                after,
                before,
                cancellationToken).ConfigureAwait(false);

            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new
            {
                error = new
                {
                    message = ex.Message,
                    type = "invalid_request_error"
                }
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error listing input items");
        }
    }

    private static void ValidateAgentName([NotNull] string agentName)
    {
        var escaped = Uri.EscapeDataString(agentName);
        if (!string.Equals(escaped, agentName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Agent name '{agentName}' contains characters invalid for URL routes.", nameof(agentName));
        }
    }
}
