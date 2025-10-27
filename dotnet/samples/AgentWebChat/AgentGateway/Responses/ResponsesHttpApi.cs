// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.AspNetCore.Mvc;

namespace AgentGateway.Responses;

/// <summary>
/// Minimal API endpoints for OpenAI Responses API.
/// </summary>
public static class ResponsesHttpApi
{
    public static IEndpointConventionBuilder MapResponses(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/responses")
            .WithTags("Responses")
            .WithOpenApi();

        // Create response endpoint
        group.MapPost("", CreateResponseAsync)
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

        // List response input items endpoint
        group.MapGet("{responseId}/input_items", ListResponseInputItemsAsync)
            .WithName("ListResponseInputItems")
            .WithSummary("Lists the input items for a response");

        return group;
    }

    private static async Task<IResult> CreateResponseAsync(
        [FromBody] CreateResponse request,
        [FromServices] ResponsesService responsesService,
        [FromQuery] bool? stream = null,
        CancellationToken cancellationToken = default)
    {
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
                        var json = System.Text.Json.JsonSerializer.Serialize(update);
                        await outputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"data: {json}\n\n"));
                        await outputStream.FlushAsync();
                    }
                    await outputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("data: [DONE]\n\n"));
                }, "text/event-stream");
            }

            var response = await responsesService.CreateResponseAsync(
                request,
                cancellationToken: cancellationToken);

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
        [FromServices] ResponsesService responsesService,
        [FromQuery] string[]? include = null,
        [FromQuery] bool? stream = null,
        [FromQuery] int? starting_after = null,
        CancellationToken cancellationToken = default)
    {
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
                        var json = System.Text.Json.JsonSerializer.Serialize(update);
                        await outputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"data: {json}\n\n"));
                        await outputStream.FlushAsync();
                    }
                    await outputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("data: [DONE]\n\n"));
                }, "text/event-stream");
            }

            // Non-streaming: return the response object
            var response = await responsesService.GetResponseAsync(responseId, cancellationToken);
            return response != null
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

    private static Task<IResult> CancelResponseAsync(
        string responseId,
        [FromServices] ResponsesService responsesService,
        CancellationToken cancellationToken = default)
    {
        // Note: The OpenAI SDK may not have a direct cancel method yet
        // This is a placeholder for when it's available
        return Task.FromResult(Results.Problem(
            detail: "Cancel operation not yet implemented in SDK",
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented"));
    }

    private static async Task<IResult> ListResponseInputItemsAsync(
        string responseId,
        [FromServices] ResponsesService responsesService,
        [FromQuery] int limit = 20,
        [FromQuery] string order = "asc",
        [FromQuery] string? after = null,
        [FromQuery] string? before = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await responsesService.ListResponseInputItemsAsync(
                responseId,
                limit,
                order,
                after,
                before,
                cancellationToken);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Response not found");
        }
    }
}
