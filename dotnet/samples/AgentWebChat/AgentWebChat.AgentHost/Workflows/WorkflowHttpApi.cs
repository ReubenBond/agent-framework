// Copyright (c) Microsoft. All rights reserved.

using System.Net.Mime;
using Microsoft.Agents.AI.Runtime.Abstractions;
using Microsoft.Agents.AI.Runtime.Abstractions.Workflows;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace AgentWebChat.AgentHost.Workflows;

/// <summary>
/// HTTP API endpoints for the workflow host.
/// These endpoints are called by the Gateway to execute/resume workflows.
/// </summary>
public static class WorkflowHttpApi
{
    /// <summary>
    /// Maps the workflow host HTTP endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkflowHost(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/workflow-host")
            .WithTags("Workflow Host");

        group.MapPost("/execute", ExecuteWorkflowAsync)
            .WithName("ExecuteWorkflow")
            .WithDescription("Accepts a workflow execution request and runs it asynchronously. Returns 202 Accepted immediately. Progress is reported via state callbacks to the Gateway.")
            .Accepts<WorkflowExecutionRequest>(MediaTypeNames.Application.Json)
            .Produces<WorkflowDispatchResponse>(StatusCodes.Status202Accepted)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        group.MapPost("/resume", ResumeWorkflowAsync)
            .WithName("ResumeWorkflow")
            .WithDescription("Accepts a workflow resume request and runs it asynchronously. Returns 202 Accepted immediately. Progress is reported via state callbacks to the Gateway.")
            .Accepts<WorkflowResumeRequest>(MediaTypeNames.Application.Json)
            .Produces<WorkflowDispatchResponse>(StatusCodes.Status202Accepted)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        group.MapGet("/workflows", GetAvailableWorkflowsAsync)
            .WithName("GetAvailableWorkflows")
            .WithDescription("Gets the list of available workflow definitions")
            .Produces<IReadOnlyList<WorkflowDefinitionInfo>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static IResult ExecuteWorkflowAsync(
        [FromBody] WorkflowExecutionRequest request,
        [FromServices] WorkflowHostService workflowHost,
        [FromServices] ILogger<WorkflowHostService> logger)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "RunId is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.WorkflowName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "WorkflowName is required"
            });
        }

        // Validate workflow exists before accepting
        if (!workflowHost.WorkflowExists(request.WorkflowName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Workflow not found",
                Detail = $"Unknown workflow: '{request.WorkflowName}'"
            });
        }

        // Start execution in background and return immediately
        // The workflow will report progress via state callbacks to the Gateway
        _ = workflowHost.ExecuteInBackgroundAsync(request, logger);

        logger.LogInformation(
            "Accepted workflow execution request: {RunId} (workflow: {WorkflowName})",
            request.RunId, request.WorkflowName);

        return Results.Accepted(
            uri: null,
            value: new WorkflowDispatchResponse
            {
                RunId = request.RunId,
                Status = "Accepted",
                Message = "Workflow execution started"
            });
    }

    private static IResult ResumeWorkflowAsync(
        [FromBody] WorkflowResumeRequest request,
        [FromServices] WorkflowHostService workflowHost,
        [FromServices] ILogger<WorkflowHostService> logger)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "RunId is required"
            });
        }

        if (string.IsNullOrWhiteSpace(request.WorkflowName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "WorkflowName is required"
            });
        }

        // Validate workflow exists before accepting
        if (!workflowHost.WorkflowExists(request.WorkflowName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Workflow not found",
                Detail = $"Unknown workflow: '{request.WorkflowName}'"
            });
        }

        // Start resume in background and return immediately
        // The workflow will report progress via state callbacks to the Gateway
        _ = workflowHost.ResumeInBackgroundAsync(request, logger);

        logger.LogInformation(
            "Accepted workflow resume request: {RunId} (workflow: {WorkflowName}, signal: {RequestId})",
            request.RunId, request.WorkflowName, request.Signal.RequestId);

        return Results.Accepted(
            uri: null,
            value: new WorkflowDispatchResponse
            {
                RunId = request.RunId,
                Status = "Accepted",
                Message = "Workflow resume started"
            });
    }

    private static async Task<Ok<IReadOnlyList<WorkflowDefinitionInfo>>> GetAvailableWorkflowsAsync(
        [FromServices] WorkflowHostService workflowHost,
        CancellationToken cancellationToken)
    {
        var workflows = await workflowHost.GetAvailableWorkflowsAsync(cancellationToken);
        return TypedResults.Ok(workflows);
    }
}

/// <summary>
/// Response returned when a workflow execution or resume request is accepted.
/// </summary>
public sealed class WorkflowDispatchResponse
{
    /// <summary>
    /// The workflow run ID.
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// The status of the dispatch (e.g., "Accepted").
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// A message describing the result.
    /// </summary>
    public string? Message { get; init; }
}
