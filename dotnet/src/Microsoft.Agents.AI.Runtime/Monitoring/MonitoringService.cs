// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Runtime.Abstractions.Monitoring;
using Microsoft.Agents.AI.Runtime.Workers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI.Runtime.Monitoring;

/// <summary>
/// Service for retrieving monitoring data from the system.
/// </summary>
public class MonitoringService : IMonitoringService
{
    private readonly WorkerRegistry _workerRegistry;
    private readonly WorkerDiscoveryCache _discoveryCache;
    private readonly IMonitoringEventBroadcaster _eventBroadcaster;
    private readonly ILogger<MonitoringService> _logger;
    private readonly DateTimeOffset _startTime;

    /// <summary>
    /// Initializes a new instance of the MonitoringService class.
    /// </summary>
    public MonitoringService(
        WorkerRegistry workerRegistry,
        WorkerDiscoveryCache discoveryCache,
        IMonitoringEventBroadcaster eventBroadcaster,
        ILogger<MonitoringService> logger)
    {
        this._workerRegistry = workerRegistry ?? throw new ArgumentNullException(nameof(workerRegistry));
        this._discoveryCache = discoveryCache ?? throw new ArgumentNullException(nameof(discoveryCache));
        this._eventBroadcaster = eventBroadcaster ?? throw new ArgumentNullException(nameof(eventBroadcaster));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this._startTime = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc/>
    public Task<SystemStatus> GetSystemStatusAsync(CancellationToken cancellationToken = default)
    {
        var workers = this._workerRegistry.Entries.ToList();
        var healthyWorkers = workers.Count(w => !w.IsDown);
        var now = DateTimeOffset.UtcNow;

        var status = new SystemStatus
        {
            Status = healthyWorkers > 0 ? "Healthy" : "Degraded",
            Uptime = now - this._startTime,
            ActiveWorkers = healthyWorkers,
            TotalWorkers = workers.Count,
            ActiveWorkflows = 0, // Will be populated by Orleans integration
            PendingWorkflows = 0,
            Timestamp = now
        };

        return Task.FromResult(status);
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
    public Task<IReadOnlyList<WorkflowMonitoringSummary>> GetActiveWorkflowsAsync(CancellationToken cancellationToken = default)
    {
        // This will be overridden by Orleans-aware implementation
        return Task.FromResult<IReadOnlyList<WorkflowMonitoringSummary>>([]);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<WorkflowMonitoringSummary>> GetRecentWorkflowsAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        // This will be overridden by Orleans-aware implementation
        return Task.FromResult<IReadOnlyList<WorkflowMonitoringSummary>>([]);
    }

    /// <inheritdoc/>
    public Task<WorkflowMetricsSnapshot> GetWorkflowMetricsAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var snapshot = new WorkflowMetricsSnapshot
        {
            Window = window,
            Timestamp = now,
            TotalStarted = 0,
            TotalCompleted = 0,
            TotalFailed = 0,
            TotalCancelled = 0,
            AverageDurationMs = 0,
            P50DurationMs = 0,
            P95DurationMs = 0,
            P99DurationMs = 0
        };

        return Task.FromResult(snapshot);
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
}
