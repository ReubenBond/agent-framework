// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Runtime.Abstractions.Telemetry;
using Microsoft.Agents.AI.Runtime.Abstractions.Workers;
using Microsoft.Agents.AI.Runtime.Abstractions.Workflows;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI.Runtime.Workers;

/// <summary>
/// Executes workflows by dispatching to registered workers.
/// Workers are expected to return 202 Accepted immediately and execute
/// the workflow asynchronously, reporting progress via state callbacks.
/// </summary>
public sealed class WorkerWorkflowExecutor : IWorkflowExecutor
{
    private readonly WorkerRegistry _registry;
    private readonly WorkerDiscoveryCache _discoveryCache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WorkerWorkflowExecutor> _logger;
    private readonly WorkflowMetrics? _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerWorkflowExecutor"/> class.
    /// </summary>
    public WorkerWorkflowExecutor(
        WorkerRegistry registry,
        WorkerDiscoveryCache discoveryCache,
        IHttpClientFactory httpClientFactory,
        ILogger<WorkerWorkflowExecutor> logger,
        WorkflowMetrics? metrics = null)
    {
        this._registry = registry;
        this._discoveryCache = discoveryCache;
        this._httpClientFactory = httpClientFactory;
        this._logger = logger;
        this._metrics = metrics;
    }

    /// <inheritdoc/>
    public async Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowExecutionRequest request,
        string? preferredWorkerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var worker = await this.SelectWorkerAsync(request.WorkflowName, preferredWorkerId, cancellationToken).ConfigureAwait(false);
        if (worker is null)
        {
            this._logger.LogError(
                "No worker available to execute workflow {WorkflowName} for run {RunId}",
                request.WorkflowName,
                request.RunId);

            return WorkflowExecutionResult.Failed(
                "NO_WORKER_AVAILABLE",
                $"No worker available to execute workflow '{request.WorkflowName}'");
        }

        return await this.DispatchToWorkerAsync(
            worker,
            new Uri(worker.Endpoint, "/v1/workflow-host/execute"),
            request,
            request.RunId,
            request.WorkflowName,
            "execute",
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<WorkflowExecutionResult> ResumeAsync(
        WorkflowResumeRequest request,
        string? preferredWorkerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var worker = await this.SelectWorkerAsync(request.WorkflowName, preferredWorkerId, cancellationToken).ConfigureAwait(false);
        if (worker is null)
        {
            this._logger.LogError(
                "No worker available to resume workflow {WorkflowName} for run {RunId}",
                request.WorkflowName,
                request.RunId);

            return WorkflowExecutionResult.Failed(
                "NO_WORKER_AVAILABLE",
                $"No worker available to resume workflow '{request.WorkflowName}'");
        }

        return await this.DispatchToWorkerAsync(
            worker,
            new Uri(worker.Endpoint, "/v1/workflow-host/resume"),
            request,
            request.RunId,
            request.WorkflowName,
            "resume",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Dispatches a request to a worker and returns immediately after receiving
    /// the response headers (202 Accepted). Does not wait for the workflow to complete.
    /// </summary>
    private async Task<WorkflowExecutionResult> DispatchToWorkerAsync<TRequest>(
        WorkerInfo worker,
        Uri uri,
        TRequest request,
        string runId,
        string workflowName,
        string operation,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;

        try
        {
            var client = this._httpClientFactory.CreateClient();

            // Create the request manually so we can use HttpCompletionOption.ResponseHeadersRead
            // This ensures we return immediately after receiving the 202 Accepted response headers,
            // without waiting for the response body or for the workflow to complete.
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(request)
            };

            using var response = await client.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                success = true;
                this._logger.LogInformation(
                    "Dispatched {Operation} for workflow {WorkflowName} run {RunId} to worker {WorkerId} (Status: {StatusCode}, Latency: {LatencyMs}ms)",
                    operation,
                    workflowName,
                    runId,
                    worker.Id,
                    response.StatusCode,
                    stopwatch.ElapsedMilliseconds);

                return WorkflowExecutionResult.Succeeded(worker.Id);
            }

            // For error responses, we need to read the body to get error details
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            this._logger.LogError(
                "Worker {WorkerId} rejected {Operation} for workflow {WorkflowName} run {RunId}: {Status} - {Error}",
                worker.Id,
                operation,
                workflowName,
                runId,
                response.StatusCode,
                errorContent);

            return WorkflowExecutionResult.Failed(
                "WORKER_REJECTED",
                $"Worker rejected {operation}: {response.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(
                ex,
                "Failed to dispatch {Operation} for workflow {WorkflowName} run {RunId} to worker {WorkerId}",
                operation,
                workflowName,
                runId,
                worker.Id);

            return WorkflowExecutionResult.Failed(
                "DISPATCH_FAILED",
                $"Failed to dispatch {operation} to worker: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            this._metrics?.RecordWorkerDispatch(worker.Id, workflowName, success, stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private async Task<WorkerInfo?> SelectWorkerAsync(
        string workflowName,
        string? preferredWorkerId,
        CancellationToken cancellationToken)
    {
        // If a preferred worker is specified and available, use it
        if (!string.IsNullOrEmpty(preferredWorkerId))
        {
            var preferred = this._registry.Get(preferredWorkerId);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        // Try to find a worker that supports this workflow
        var activeWorkers = this._registry.ActiveWorkers;

        foreach (var worker in activeWorkers)
        {
            var entities = await this._discoveryCache.DiscoverEntitiesAsync(worker, cancellationToken).ConfigureAwait(false);
            if (entities?.ContainsKey(workflowName) == true)
            {
                return worker;
            }
        }

        // Fall back to default worker (if available)
        var defaultWorker = this._registry.DefaultWorker;
        if (defaultWorker is not null && activeWorkers.Any(w => w.Id == defaultWorker.Id))
        {
            this._logger.LogDebug(
                "Using default worker {WorkerId} for workflow {WorkflowName}",
                defaultWorker.Id,
                workflowName);
            return defaultWorker;
        }

        // Last resort: use any active worker
        return activeWorkers.FirstOrDefault();
    }
}
