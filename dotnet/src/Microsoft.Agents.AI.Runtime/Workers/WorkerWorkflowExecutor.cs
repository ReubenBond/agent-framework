// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Runtime.Abstractions.Workers;
using Microsoft.Agents.AI.Runtime.Abstractions.Workflows;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI.Runtime.Workers;

/// <summary>
/// Executes workflows by dispatching to registered workers.
/// </summary>
public sealed class WorkerWorkflowExecutor : IWorkflowExecutor
{
    private readonly WorkerRegistry _registry;
    private readonly WorkerDiscoveryCache _discoveryCache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WorkerWorkflowExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerWorkflowExecutor"/> class.
    /// </summary>
    public WorkerWorkflowExecutor(
        WorkerRegistry registry,
        WorkerDiscoveryCache discoveryCache,
        IHttpClientFactory httpClientFactory,
        ILogger<WorkerWorkflowExecutor> logger)
    {
        this._registry = registry;
        this._discoveryCache = discoveryCache;
        this._httpClientFactory = httpClientFactory;
        this._logger = logger;
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

        try
        {
            var client = this._httpClientFactory.CreateClient();
            var executeUri = new Uri(worker.Endpoint, "/v1/workflow-host/execute");

            var response = await client.PostAsJsonAsync(
                executeUri,
                request,
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                this._logger.LogInformation(
                    "Dispatched workflow {WorkflowName} run {RunId} to worker {WorkerId}",
                    request.WorkflowName,
                    request.RunId,
                    worker.Id);

                return WorkflowExecutionResult.Succeeded(worker.Id);
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            this._logger.LogError(
                "Worker {WorkerId} rejected workflow {WorkflowName} run {RunId}: {Status} - {Error}",
                worker.Id,
                request.WorkflowName,
                request.RunId,
                response.StatusCode,
                errorContent);

            return WorkflowExecutionResult.Failed(
                "WORKER_REJECTED",
                $"Worker rejected execution: {response.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(
                ex,
                "Failed to dispatch workflow {WorkflowName} run {RunId} to worker {WorkerId}",
                request.WorkflowName,
                request.RunId,
                worker.Id);

            return WorkflowExecutionResult.Failed(
                "DISPATCH_FAILED",
                $"Failed to dispatch to worker: {ex.Message}");
        }
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

        try
        {
            var client = this._httpClientFactory.CreateClient();
            var resumeUri = new Uri(worker.Endpoint, "/v1/workflow-host/resume");

            var response = await client.PostAsJsonAsync(
                resumeUri,
                request,
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                this._logger.LogInformation(
                    "Dispatched resume for workflow {WorkflowName} run {RunId} to worker {WorkerId}",
                    request.WorkflowName,
                    request.RunId,
                    worker.Id);

                return WorkflowExecutionResult.Succeeded(worker.Id);
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            this._logger.LogError(
                "Worker {WorkerId} rejected resume for workflow {WorkflowName} run {RunId}: {Status} - {Error}",
                worker.Id,
                request.WorkflowName,
                request.RunId,
                response.StatusCode,
                errorContent);

            return WorkflowExecutionResult.Failed(
                "WORKER_REJECTED",
                $"Worker rejected resume: {response.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogError(
                ex,
                "Failed to dispatch resume for workflow {WorkflowName} run {RunId} to worker {WorkerId}",
                request.WorkflowName,
                request.RunId,
                worker.Id);

            return WorkflowExecutionResult.Failed(
                "DISPATCH_FAILED",
                $"Failed to dispatch resume to worker: {ex.Message}");
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
