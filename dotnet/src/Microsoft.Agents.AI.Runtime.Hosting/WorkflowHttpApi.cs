// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Runtime.Abstractions;
using Microsoft.Agents.AI.Runtime.Abstractions.Monitoring;
using Microsoft.Agents.AI.Runtime.Abstractions.Workers;
using Microsoft.Agents.AI.Runtime.Abstractions.Workflows;
using Microsoft.Agents.AI.Runtime.Orleans.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace Microsoft.Agents.AI.Runtime.Hosting;

/// <summary>
/// HTTP API endpoints for workflow management.
/// </summary>
public static class WorkflowHttpApi
{
    private const string IfMatchHeader = "If-Match";

    /// <summary>
    /// Maps workflow API endpoints to the application.
    /// </summary>
    public static WebApplication MapWorkflowApi(this WebApplication app)
    {
        var group = app.MapGroup("/v1/workflows");

        // Frontend API
        group.MapGet("/", ListWorkflowsAsync).WithName("ListWorkflows");
        group.MapPost("/", StartWorkflowAsync).WithName("StartWorkflow");
        group.MapGet("/{runId}", GetWorkflowAsync).WithName("GetWorkflow");
        group.MapGet("/{runId}/events", StreamWorkflowEventsAsync).WithName("StreamWorkflowEvents");
        group.MapPost("/{runId}/signals", SendSignalAsync).WithName("SendWorkflowSignal");
        group.MapPost("/{runId}/cancel", CancelWorkflowAsync).WithName("CancelWorkflow");
        group.MapPost("/{runId}/abort", AbortWorkflowAsync).WithName("AbortWorkflow");
        group.MapDelete("/{runId}", DeleteWorkflowAsync).WithName("DeleteWorkflow");

        // State Callback API
        var stateGroup = app.MapGroup("/v1/workflows/{runId}/state");
        stateGroup.MapPut("/status", UpdateStatusAsync).WithName("UpdateWorkflowStatus");
        stateGroup.MapPost("/steps/started", RecordStepStartedAsync).WithName("RecordStepStarted");
        stateGroup.MapPost("/steps/completed", RecordStepCompletedAsync).WithName("RecordStepCompleted");
        stateGroup.MapPost("/pending-requests", RecordPendingRequestAsync).WithName("RecordPendingRequest");
        stateGroup.MapDelete("/pending-requests/{requestId}", ClearPendingRequestAsync).WithName("ClearPendingRequest");
        stateGroup.MapPut("/checkpoint", SaveCheckpointAsync).WithName("SaveCheckpoint");
        stateGroup.MapGet("/checkpoint", GetCheckpointAsync).WithName("GetCheckpoint");
        stateGroup.MapGet("/checkpoints/{checkpointId}", GetCheckpointByIdAsync).WithName("GetCheckpointById");
        stateGroup.MapGet("/checkpoints", ListCheckpointIdsAsync).WithName("ListCheckpointIds");
        stateGroup.MapPost("/artifacts", RecordArtifactAsync).WithName("RecordArtifact");
        stateGroup.MapPost("/output-delta", RecordOutputDeltaAsync).WithName("RecordOutputDelta");

        return app;
    }

    private static async Task<IResult> ListWorkflowsAsync(
        [FromQuery] string? status,
        [FromQuery] int? limit,
        [FromQuery] string? after,
        [FromQuery] string? before,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        WorkflowRunStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<WorkflowRunStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            statusFilter = parsedStatus;
        }

        var indexGrain = grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        var response = await indexGrain.ListAsync(statusFilter, limit ?? 50, after, before, ct).ConfigureAwait(false);

        return Results.Ok(response);
    }

    private static async Task<IResult> StartWorkflowAsync(
        StartWorkflowRequest request,
        IGrainFactory grainFactory,
        IWorkflowExecutor workflowExecutor,
        IMonitoringEventBroadcaster eventBroadcaster,
        IOptions<RuntimeOptions> runtimeOptions,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("WorkflowHttpApi");
        var runId = $"wfrun_{Guid.NewGuid():N}";
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var run = await grain.StartAsync(request, ct).ConfigureAwait(false);
            logger.LogInformation("Workflow started: {RunId} (workflow: {WorkflowName})", runId, request.WorkflowName);

            eventBroadcaster.PublishWorkflowEvent(MonitoringEventTypes.WorkflowStarted, new WorkflowEventPayload
            {
                RunId = runId,
                WorkflowName = request.WorkflowName,
                Status = "Queued"
            });

            var callbackBaseUrl = runtimeOptions.Value.CallbackBaseUrl;
            if (string.IsNullOrEmpty(callbackBaseUrl))
            {
                var scheme = httpContext.Request.Scheme;
                var host = httpContext.Request.Host.ToString();
                callbackBaseUrl = $"{scheme}://{host}";
            }

            var executionRequest = new WorkflowExecutionRequest
            {
                RunId = runId,
                WorkflowName = request.WorkflowName,
                Input = request.Input,
                CallbackBaseUrl = callbackBaseUrl,
                Options = request.Options
            };

            var result = await workflowExecutor.ExecuteAsync(executionRequest, preferredWorkerId: null, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                logger.LogError(
                    "Workflow execution dispatch failed: {RunId}, Error: {ErrorCode} - {ErrorMessage}",
                    runId, result.ErrorCode, result.ErrorMessage);

                await grain.AbortAsync($"Dispatch failed: {result.ErrorMessage}", ct).ConfigureAwait(false);

                eventBroadcaster.PublishWorkflowEvent(MonitoringEventTypes.WorkflowFailed, new WorkflowEventPayload
                {
                    RunId = runId,
                    WorkflowName = request.WorkflowName,
                    Status = "Aborted"
                });

                return Results.Problem(
                    title: "Workflow dispatch failed",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status502BadGateway);
            }

            if (!string.IsNullOrEmpty(result.WorkerId))
            {
                await grain.SetAssignedWorkerIdAsync(result.WorkerId, ct).ConfigureAwait(false);
            }

            return Results.Created($"/v1/workflows/{runId}", run);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start workflow {WorkflowName}", request.WorkflowName);
            return Results.Problem(
                title: "Failed to start workflow",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> GetWorkflowAsync(
        string runId,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);
        var run = await grain.GetAsync(ct).ConfigureAwait(false);

        if (run is null)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(run);
    }

    private static async Task StreamWorkflowEventsAsync(
        string runId,
        [FromQuery] int? after,
        IGrainFactory grainFactory,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        var run = await grain.GetAsync(ct).ConfigureAwait(false);
        if (run is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Workflow not found", runId }, ct).ConfigureAwait(false);
            return;
        }

        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";

        var options = RuntimeJsonContext.DefaultOptions;

        await foreach (var evt in grain.StreamEventsAsync(after, ct).ConfigureAwait(false))
        {
            var eventType = evt.GetType().Name;
            var json = JsonSerializer.Serialize(evt, options);

            await httpContext.Response.WriteAsync($"event: {eventType}\n", ct).ConfigureAwait(false);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
            await httpContext.Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }

        await httpContext.Response.WriteAsync("event: done\ndata: {}\n\n", ct).ConfigureAwait(false);
        await httpContext.Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IResult> SendSignalAsync(
        string runId,
        WorkflowSignal signal,
        IGrainFactory grainFactory,
        IWorkflowExecutor workflowExecutor,
        IOptions<RuntimeOptions> runtimeOptions,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("WorkflowHttpApi");
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var currentRun = await grain.GetAsync(ct).ConfigureAwait(false);
            if (currentRun is null)
            {
                return Results.Problem(
                    title: "Workflow not found",
                    detail: $"Workflow '{runId}' not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var run = await grain.SendSignalAsync(signal, ct).ConfigureAwait(false);
            logger.LogInformation("Signal sent to workflow {RunId}: request {RequestId}", runId, signal.RequestId);

            var callbackBaseUrl = runtimeOptions.Value.CallbackBaseUrl;
            if (string.IsNullOrEmpty(callbackBaseUrl))
            {
                var scheme = httpContext.Request.Scheme;
                var host = httpContext.Request.Host.ToString();
                callbackBaseUrl = $"{scheme}://{host}";
            }

            var checkpointResult = await grain.GetCheckpointAsync(ct).ConfigureAwait(false);
            var assignedWorkerId = await grain.GetAssignedWorkerIdAsync(ct).ConfigureAwait(false);

            var resumeRequest = new WorkflowResumeRequest
            {
                RunId = runId,
                WorkflowName = currentRun.WorkflowName,
                CallbackBaseUrl = callbackBaseUrl,
                Signal = signal,
                CheckpointId = checkpointResult?.Checkpoint.CheckpointId
            };

            var result = await workflowExecutor.ResumeAsync(resumeRequest, assignedWorkerId, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                logger.LogError(
                    "Workflow resume dispatch failed: {RunId}, Error: {ErrorCode} - {ErrorMessage}",
                    runId, result.ErrorCode, result.ErrorMessage);

                await grain.AbortAsync($"Resume dispatch failed: {result.ErrorMessage}", ct).ConfigureAwait(false);

                return Results.Problem(
                    title: "Workflow resume failed",
                    detail: result.ErrorMessage,
                    statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.Ok(run);
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Invalid signal",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> CancelWorkflowAsync(
        string runId,
        IGrainFactory grainFactory,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("WorkflowHttpApi");
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var run = await grain.CancelAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Workflow cancellation requested: {RunId}", runId);
            return Results.Ok(run);
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Cannot cancel workflow",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> AbortWorkflowAsync(
        string runId,
        AbortWorkflowRequest request,
        IGrainFactory grainFactory,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("WorkflowHttpApi");
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var run = await grain.AbortAsync(request.Reason, ct).ConfigureAwait(false);
            logger.LogWarning("Workflow aborted: {RunId} - Reason: {Reason}", runId, request.Reason);
            return Results.Ok(run);
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Cannot abort workflow",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> DeleteWorkflowAsync(
        string runId,
        IGrainFactory grainFactory,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("WorkflowHttpApi");
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            await grain.DeleteAsync(ct).ConfigureAwait(false);
            logger.LogInformation("Workflow deleted: {RunId}", runId);
            return Results.NoContent();
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(
                title: "Workflow not found",
                detail: $"Workflow '{runId}' not found.",
                statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(
                title: "Cannot delete workflow",
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    // State callback API handlers

    private static async Task<IResult> UpdateStatusAsync(
        string runId,
        WorkflowRunStatusUpdate update,
        IGrainFactory grainFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var etag = httpContext.Request.Headers[IfMatchHeader].ToString();
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.UpdateStatusAsync(update, string.IsNullOrEmpty(etag) ? null : etag, ct).ConfigureAwait(false);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(title: "Concurrency conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> RecordStepStartedAsync(
        string runId,
        WorkflowStepStartedRecord step,
        IGrainFactory grainFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var etag = httpContext.Request.Headers[IfMatchHeader].ToString();
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.RecordStepStartedAsync(step, string.IsNullOrEmpty(etag) ? null : etag, ct).ConfigureAwait(false);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(title: "Concurrency conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> RecordStepCompletedAsync(
        string runId,
        WorkflowStepCompletedRecord step,
        IGrainFactory grainFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var etag = httpContext.Request.Headers[IfMatchHeader].ToString();
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.RecordStepCompletedAsync(step, string.IsNullOrEmpty(etag) ? null : etag, ct).ConfigureAwait(false);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(title: "Concurrency conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> RecordPendingRequestAsync(
        string runId,
        PendingExternalRequest request,
        IGrainFactory grainFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var etag = httpContext.Request.Headers[IfMatchHeader].ToString();
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.RecordPendingRequestAsync(request, string.IsNullOrEmpty(etag) ? null : etag, ct).ConfigureAwait(false);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(title: "Concurrency conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> ClearPendingRequestAsync(
        string runId,
        string requestId,
        IGrainFactory grainFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var etag = httpContext.Request.Headers[IfMatchHeader].ToString();
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.ClearPendingRequestAsync(requestId, string.IsNullOrEmpty(etag) ? null : etag, ct).ConfigureAwait(false);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(title: "Concurrency conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> SaveCheckpointAsync(
        string runId,
        WorkflowCheckpointData checkpoint,
        IGrainFactory grainFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var etag = httpContext.Request.Headers[IfMatchHeader].ToString();
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.SaveCheckpointAsync(checkpoint, string.IsNullOrEmpty(etag) ? null : etag, ct).ConfigureAwait(false);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(title: "Concurrency conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> GetCheckpointAsync(
        string runId,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var result = await grain.GetCheckpointAsync(ct).ConfigureAwait(false);
            if (result is null)
            {
                return Results.NoContent();
            }
            return Results.Ok(result);
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> GetCheckpointByIdAsync(
        string runId,
        string checkpointId,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var result = await grain.GetCheckpointByIdAsync(checkpointId, ct).ConfigureAwait(false);
            if (result is null)
            {
                return Results.NoContent();
            }
            return Results.Ok(result);
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> ListCheckpointIdsAsync(
        string runId,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var result = await grain.ListCheckpointIdsAsync(ct).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
    }

    private static async Task<IResult> RecordArtifactAsync(
        string runId,
        WorkflowArtifactRecord artifact,
        IGrainFactory grainFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var etag = httpContext.Request.Headers[IfMatchHeader].ToString();
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            var newETag = await grain.RecordArtifactAsync(artifact, string.IsNullOrEmpty(etag) ? null : etag, ct).ConfigureAwait(false);
            return Results.Ok(new ETagResponse { ETag = newETag });
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkflowConcurrencyException ex)
        {
            return Results.Problem(title: "Concurrency conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> RecordOutputDeltaAsync(
        string runId,
        WorkflowOutputDelta delta,
        IGrainFactory grainFactory,
        CancellationToken ct)
    {
        var grain = grainFactory.GetGrain<IWorkflowGrain>(runId);

        try
        {
            await grain.RecordOutputDeltaAsync(delta, etag: null, ct).ConfigureAwait(false);
            return Results.Accepted();
        }
        catch (WorkflowNotFoundException)
        {
            return Results.Problem(title: "Workflow not found", statusCode: StatusCodes.Status404NotFound);
        }
    }
}
