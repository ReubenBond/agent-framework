// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Runtime.Abstractions.Monitoring;
using Microsoft.Agents.AI.Runtime.Abstractions.Workflows;
using Microsoft.Agents.AI.Runtime.Orleans.Workflows;
using Microsoft.Agents.AI.Runtime.Workers;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Microsoft.Agents.AI.Runtime.Orleans.Monitoring;

/// <summary>
/// Orleans-aware implementation of <see cref="IMonitoringService"/> that retrieves
/// workflow data from Orleans grains.
/// </summary>
public class OrleansMonitoringService : IMonitoringService
{
    private readonly IGrainFactory _grainFactory;
    private readonly WorkerRegistry _workerRegistry;
    private readonly IMonitoringEventBroadcaster _eventBroadcaster;
    private readonly ILogger<OrleansMonitoringService> _logger;
    private readonly DateTimeOffset _startTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrleansMonitoringService"/> class.
    /// </summary>
    public OrleansMonitoringService(
        IGrainFactory grainFactory,
        WorkerRegistry workerRegistry,
        IMonitoringEventBroadcaster eventBroadcaster,
        ILogger<OrleansMonitoringService> logger)
    {
        this._grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        this._workerRegistry = workerRegistry ?? throw new ArgumentNullException(nameof(workerRegistry));
        this._eventBroadcaster = eventBroadcaster ?? throw new ArgumentNullException(nameof(eventBroadcaster));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this._startTime = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public async Task<SystemStatus> GetSystemStatusAsync(CancellationToken cancellationToken = default)
    {
        var workers = this._workerRegistry.Entries.ToList();
        var healthyWorkers = workers.Count(w => !w.IsDown);
        var now = DateTimeOffset.UtcNow;

        // Get workflow counts from the index grain
        var indexGrain = this._grainFactory.GetGrain<IWorkflowIndexGrain>("default");
        var activeWorkflows = await indexGrain.ListAsync(WorkflowRunStatus.Running, limit: 1000, after: null, before: null, cancellationToken).ConfigureAwait(false);
        var pendingWorkflows = await indexGrain.ListAsync(WorkflowRunStatus.Queued, limit: 1000, after: null, before: null, cancellationToken).ConfigureAwait(false);
        var waitingWorkflows = await indexGrain.ListAsync(WorkflowRunStatus.WaitingForSignal, limit: 1000, after: null, before: null, cancellationToken).ConfigureAwait(false);

        return new SystemStatus
        {
            Status = healthyWorkers > 0 ? "Healthy" : "Degraded",
            Uptime = now - this._startTime,
            ActiveWorkers = healthyWorkers,
            TotalWorkers = workers.Count,
            ActiveWorkflows = activeWorkflows.Data.Count + waitingWorkflows.Data.Count,
            PendingWorkflows = pendingWorkflows.Data.Count,
            Timestamp = now
        };
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<WorkerStatus>> GetWorkersAsync(CancellationToken cancellationToken = default)
    {
        var workers = this._workerRegistry.Entries.Select(entry => new WorkerStatus
        {
            Id = entry.Info.Id,
            HostId = entry.Info.HostId,
            Endpoint = entry.Info.Endpoint.ToString(),
            Status = entry.IsDown ? "Unhealthy" : "Healthy",
            LastHeartbeat = entry.LastHeartbeat,
            ConsecutiveFailures = entry.ConsecutiveFailures,
            IsDefault = this._workerRegistry.DefaultWorker?.Id == entry.Info.Id,
            ActiveWorkflows = 0 // TODO: Track per-worker workflow counts
        }).ToList();

        return Task.FromResult<IReadOnlyList<WorkerStatus>>(workers);
    }

    /// <inheritdoc/>
    public Task<WorkerStatus?> GetWorkerAsync(string workerId, CancellationToken cancellationToken = default)
    {
        var entry = this._workerRegistry.Entries.FirstOrDefault(e => e.Info.Id == workerId);
        if (entry is null)
        {
            return Task.FromResult<WorkerStatus?>(null);
        }

        var status = new WorkerStatus
        {
            Id = entry.Info.Id,
            HostId = entry.Info.HostId,
            Endpoint = entry.Info.Endpoint.ToString(),
            Status = entry.IsDown ? "Unhealthy" : "Healthy",
            LastHeartbeat = entry.LastHeartbeat,
            ConsecutiveFailures = entry.ConsecutiveFailures,
            IsDefault = this._workerRegistry.DefaultWorker?.Id == entry.Info.Id,
            ActiveWorkflows = 0
        };

        return Task.FromResult<WorkerStatus?>(status);
    }

    /// <inheritdoc/>
    public async Task<PaginatedWorkflowsResponse> GetActiveWorkflowsAsync(int limit = 20, string? cursor = null, CancellationToken cancellationToken = default)
    {
        // Clamp limit to reasonable bounds
        limit = Math.Clamp(limit, 1, 100);

        var indexGrain = this._grainFactory.GetGrain<IWorkflowIndexGrain>("default");

        // For active workflows, we need to query multiple statuses
        // The cursor encodes: "status:lastRunId" to resume within a specific status query
        WorkflowRunStatus? currentStatus = null;
        string? afterRunId = null;

        if (!string.IsNullOrEmpty(cursor))
        {
            var parts = cursor.Split(':', 2);
            if (parts.Length == 2 && Enum.TryParse<WorkflowRunStatus>(parts[0], out var status))
            {
                currentStatus = status;
                afterRunId = parts[1];
            }
        }

        var allResults = new List<WorkflowRunSummary>();
        var hasMore = false;
        string? nextCursor = null;

        // Query active statuses in order: Running, Queued, WaitingForSignal
        var statuses = new[] { WorkflowRunStatus.Running, WorkflowRunStatus.Queued, WorkflowRunStatus.WaitingForSignal };

        // Skip statuses before the current one if we have a cursor
        var startIndex = 0;
        if (currentStatus.HasValue)
        {
            startIndex = Array.IndexOf(statuses, currentStatus.Value);
            if (startIndex < 0)
            {
                startIndex = 0;
            }
        }

        for (var i = startIndex; i < statuses.Length && allResults.Count < limit; i++)
        {
            var status = statuses[i];
            var after = (i == startIndex && afterRunId != null) ? afterRunId : null;
            var remaining = limit - allResults.Count;

            var response = await indexGrain.ListAsync(status, limit: remaining + 1, after: after, before: null, cancellationToken).ConfigureAwait(false);

            if (response.Data.Count > remaining)
            {
                // We have more results than we need
                allResults.AddRange(response.Data.Take(remaining));
                hasMore = true;
                // Create cursor for the last item we're returning
                var lastItem = response.Data[remaining - 1];
                nextCursor = $"{status}:{lastItem.Id}";
                break;
            }
            else
            {
                allResults.AddRange(response.Data);
                if (response.HasMore)
                {
                    // More in this status
                    hasMore = true;
                    var lastItem = response.Data[^1];
                    nextCursor = $"{status}:{lastItem.Id}";
                }
                else if (i < statuses.Length - 1)
                {
                    // Move to next status
                    hasMore = true;
                    nextCursor = $"{statuses[i + 1]}:";
                }
            }
        }

        return new PaginatedWorkflowsResponse
        {
            Data = allResults
                .OrderByDescending(w => w.CreatedAt)
                .Select(ToMonitoringSummary)
                .ToList(),
            HasMore = hasMore && allResults.Count == limit,
            NextCursor = allResults.Count == limit ? nextCursor : null
        };
    }

    /// <inheritdoc/>
    public async Task<PaginatedWorkflowsResponse> GetRecentWorkflowsAsync(int limit = 20, string? cursor = null, CancellationToken cancellationToken = default)
    {
        // Clamp limit to reasonable bounds
        limit = Math.Clamp(limit, 1, 100);

        var indexGrain = this._grainFactory.GetGrain<IWorkflowIndexGrain>("default");

        // Get all workflows (no status filter) ordered by creation time
        var response = await indexGrain.ListAsync(statusFilter: null, limit: limit + 1, after: cursor, before: null, cancellationToken).ConfigureAwait(false);

        var hasMore = response.Data.Count > limit;
        var data = hasMore ? response.Data.Take(limit).ToList() : response.Data;
        var nextCursor = hasMore || response.HasMore ? data[^1].Id : null;

        return new PaginatedWorkflowsResponse
        {
            Data = data.ConvertAll(ToMonitoringSummary),
            HasMore = hasMore || response.HasMore,
            NextCursor = nextCursor
        };
    }

    /// <inheritdoc/>
    public async Task<WorkflowMetricsSnapshot> GetWorkflowMetricsAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - window;

        var indexGrain = this._grainFactory.GetGrain<IWorkflowIndexGrain>("default");

        // Get recent workflows to calculate metrics
        var response = await indexGrain.ListAsync(statusFilter: null, limit: 1000, after: null, before: null, cancellationToken).ConfigureAwait(false);

        var workflowsInWindow = response.Data
            .Where(w => w.CreatedAt >= windowStart)
            .ToList();

        var completedCount = workflowsInWindow.Count(w => w.Status == WorkflowRunStatus.Completed);
        var failedCount = workflowsInWindow.Count(w => w.Status == WorkflowRunStatus.Failed);
        var cancelledCount = workflowsInWindow.Count(w => w.Status is WorkflowRunStatus.Cancelled or WorkflowRunStatus.Aborted);

        // Calculate durations for completed workflows
        var durations = workflowsInWindow
            .Where(w => w.Status == WorkflowRunStatus.Completed && w.CompletedAt.HasValue)
            .Select(w => (w.CompletedAt!.Value - w.CreatedAt).TotalMilliseconds)
            .Order()
            .ToList();

        return new WorkflowMetricsSnapshot
        {
            Window = window,
            Timestamp = now,
            TotalStarted = workflowsInWindow.Count,
            TotalCompleted = completedCount,
            TotalFailed = failedCount,
            TotalCancelled = cancelledCount,
            AverageDurationMs = durations.Count > 0 ? durations.Average() : 0,
            P50DurationMs = GetPercentile(durations, 50),
            P95DurationMs = GetPercentile(durations, 95),
            P99DurationMs = GetPercentile(durations, 99)
        };
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<MonitoringEvent> StreamEventsAsync(CancellationToken cancellationToken = default)
    {
        return this._eventBroadcaster.SubscribeAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> DrainWorkerAsync(string workerId, CancellationToken cancellationToken = default)
    {
        var entry = this._workerRegistry.Entries.FirstOrDefault(e => e.Info.Id == workerId);
        if (entry is null)
        {
            return Task.FromResult(false);
        }

        this._logger.LogInformation("Drain requested for worker {WorkerId}", workerId);

        this._eventBroadcaster.PublishWorkerEvent(MonitoringEventTypes.WorkerDrained, new WorkerEventPayload
        {
            WorkerId = workerId
        });

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<bool> EnableWorkerAsync(string workerId, CancellationToken cancellationToken = default)
    {
        var entry = this._workerRegistry.Entries.FirstOrDefault(e => e.Info.Id == workerId);
        if (entry is null)
        {
            return Task.FromResult(false);
        }

        this._logger.LogInformation("Enable requested for worker {WorkerId}", workerId);

        this._eventBroadcaster.PublishWorkerEvent(MonitoringEventTypes.WorkerEnabled, new WorkerEventPayload
        {
            WorkerId = workerId
        });

        return Task.FromResult(true);
    }

    private static WorkflowMonitoringSummary ToMonitoringSummary(WorkflowRunSummary summary)
    {
        return new WorkflowMonitoringSummary
        {
            RunId = summary.Id,
            WorkflowName = summary.WorkflowName,
            Status = summary.Status.ToString(),
            CreatedAt = summary.CreatedAt,
            CompletedAt = summary.CompletedAt,
            PendingRequestCount = summary.PendingRequestCount,
            StepCount = 0 // Would need to query individual workflow grain for this
        };
    }

    private static double GetPercentile(List<double> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile / 100.0 * sortedValues.Count) - 1;
        return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Count - 1))];
    }
}
