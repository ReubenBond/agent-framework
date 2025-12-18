// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Agents.AI.Runtime.Abstractions.Monitoring;

/// <summary>
/// Service interface for retrieving monitoring data.
/// </summary>
public interface IMonitoringService
{
    /// <summary>
    /// Gets the current system status overview.
    /// </summary>
    Task<SystemStatus> GetSystemStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of all registered workers.
    /// </summary>
    Task<IReadOnlyList<WorkerStatus>> GetWorkersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a specific worker.
    /// </summary>
    Task<WorkerStatus?> GetWorkerAsync(string workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all currently active (running or waiting) workflows with pagination.
    /// </summary>
    /// <param name="limit">Maximum number of workflows to return (default 20, max 100).</param>
    /// <param name="cursor">Cursor for pagination, obtained from previous response's NextCursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of active workflows.</returns>
    Task<PaginatedWorkflowsResponse> GetActiveWorkflowsAsync(int limit = 20, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent workflows (all statuses) with pagination.
    /// </summary>
    /// <param name="limit">Maximum number of workflows to return (default 20, max 100).</param>
    /// <param name="cursor">Cursor for pagination, obtained from previous response's NextCursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of recent workflows.</returns>
    Task<PaginatedWorkflowsResponse> GetRecentWorkflowsAsync(int limit = 20, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated workflow metrics for a time window.
    /// </summary>
    Task<WorkflowMetricsSnapshot> GetWorkflowMetricsAsync(TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams real-time monitoring events.
    /// </summary>
    IAsyncEnumerable<MonitoringEvent> StreamEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drains a worker (stops accepting new workflows).
    /// </summary>
    Task<bool> DrainWorkerAsync(string workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-enables a drained worker.
    /// </summary>
    Task<bool> EnableWorkerAsync(string workerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for broadcasting monitoring events.
/// </summary>
public interface IMonitoringEventBroadcaster
{
    /// <summary>
    /// Publishes a monitoring event to all subscribers.
    /// </summary>
    void PublishEvent(MonitoringEvent evt);

    /// <summary>
    /// Publishes a workflow event.
    /// </summary>
    void PublishWorkflowEvent(string eventType, WorkflowEventPayload payload);

    /// <summary>
    /// Publishes a worker event.
    /// </summary>
    void PublishWorkerEvent(string eventType, WorkerEventPayload payload);

    /// <summary>
    /// Subscribes to monitoring events.
    /// </summary>
    IAsyncEnumerable<MonitoringEvent> SubscribeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// System status overview.
/// </summary>
public sealed class SystemStatus
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("uptime")]
    public TimeSpan Uptime { get; init; }

    [JsonPropertyName("activeWorkers")]
    public int ActiveWorkers { get; init; }

    [JsonPropertyName("totalWorkers")]
    public int TotalWorkers { get; init; }

    [JsonPropertyName("activeWorkflows")]
    public int ActiveWorkflows { get; init; }

    [JsonPropertyName("pendingWorkflows")]
    public int PendingWorkflows { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Status of a worker.
/// </summary>
public sealed class WorkerStatus
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("hostId")]
    public required string HostId { get; init; }

    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("lastHeartbeat")]
    public DateTimeOffset LastHeartbeat { get; init; }

    [JsonPropertyName("consecutiveFailures")]
    public int ConsecutiveFailures { get; init; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    [JsonPropertyName("activeWorkflows")]
    public int ActiveWorkflows { get; init; }
}

/// <summary>
/// Monitoring summary of a workflow.
/// </summary>
public sealed class WorkflowMonitoringSummary
{
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    [JsonPropertyName("workflowName")]
    public required string WorkflowName { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("workerId")]
    public string? WorkerId { get; init; }

    [JsonPropertyName("pendingRequestCount")]
    public int PendingRequestCount { get; init; }

    [JsonPropertyName("stepCount")]
    public int StepCount { get; init; }
}

/// <summary>
/// Paginated response for workflow listings.
/// </summary>
public sealed class PaginatedWorkflowsResponse
{
    /// <summary>
    /// The list of workflows for the current page.
    /// </summary>
    [JsonPropertyName("data")]
    public required IReadOnlyList<WorkflowMonitoringSummary> Data { get; init; }

    /// <summary>
    /// Whether there are more results available beyond this page.
    /// </summary>
    [JsonPropertyName("hasMore")]
    public bool HasMore { get; init; }

    /// <summary>
    /// Cursor for fetching the next page of results. Null if no more pages.
    /// </summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }

    /// <summary>
    /// Total count of items matching the query (if available).
    /// May be null if total count is expensive to compute.
    /// </summary>
    [JsonPropertyName("totalCount")]
    public int? TotalCount { get; init; }
}

/// <summary>
/// Aggregated workflow metrics for a time window.
/// </summary>
public sealed class WorkflowMetricsSnapshot
{
    [JsonPropertyName("window")]
    public TimeSpan Window { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("totalStarted")]
    public int TotalStarted { get; init; }

    [JsonPropertyName("totalCompleted")]
    public int TotalCompleted { get; init; }

    [JsonPropertyName("totalFailed")]
    public int TotalFailed { get; init; }

    [JsonPropertyName("totalCancelled")]
    public int TotalCancelled { get; init; }

    [JsonPropertyName("averageDurationMs")]
    public double AverageDurationMs { get; init; }

    [JsonPropertyName("p50DurationMs")]
    public double P50DurationMs { get; init; }

    [JsonPropertyName("p95DurationMs")]
    public double P95DurationMs { get; init; }

    [JsonPropertyName("p99DurationMs")]
    public double P99DurationMs { get; init; }
}

/// <summary>
/// Base class for monitoring events.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(WorkflowMonitoringEvent), "workflow")]
[JsonDerivedType(typeof(WorkerMonitoringEvent), "worker")]
[JsonDerivedType(typeof(SystemMonitoringEvent), "system")]
public abstract class MonitoringEvent
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Workflow-related monitoring event.
/// </summary>
public sealed class WorkflowMonitoringEvent : MonitoringEvent
{
    [JsonPropertyName("eventType")]
    public required string EventType { get; init; }

    [JsonPropertyName("payload")]
    public required WorkflowEventPayload Payload { get; init; }
}

/// <summary>
/// Worker-related monitoring event.
/// </summary>
public sealed class WorkerMonitoringEvent : MonitoringEvent
{
    [JsonPropertyName("eventType")]
    public required string EventType { get; init; }

    [JsonPropertyName("payload")]
    public required WorkerEventPayload Payload { get; init; }
}

/// <summary>
/// System-related monitoring event.
/// </summary>
public sealed class SystemMonitoringEvent : MonitoringEvent
{
    [JsonPropertyName("eventType")]
    public required string EventType { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Payload for workflow events.
/// </summary>
public sealed class WorkflowEventPayload
{
    [JsonPropertyName("runId")]
    public string? RunId { get; init; }

    [JsonPropertyName("workflowName")]
    public string? WorkflowName { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("stepName")]
    public string? StepName { get; init; }

    [JsonPropertyName("workerId")]
    public string? WorkerId { get; init; }
}

/// <summary>
/// Payload for worker events.
/// </summary>
public sealed class WorkerEventPayload
{
    [JsonPropertyName("workerId")]
    public string? WorkerId { get; init; }

    [JsonPropertyName("hostId")]
    public string? HostId { get; init; }

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>
/// Constants for monitoring event types.
/// </summary>
public static class MonitoringEventTypes
{
    // Workflow events
    public const string WorkflowStarted = "workflow.started";
    public const string WorkflowCompleted = "workflow.completed";
    public const string WorkflowFailed = "workflow.failed";
    public const string WorkflowCancelled = "workflow.cancelled";
    public const string WorkflowAborted = "workflow.aborted";
    public const string WorkflowWaitingForSignal = "workflow.waiting_for_signal";
    public const string WorkflowStepStarted = "workflow.step.started";
    public const string WorkflowStepCompleted = "workflow.step.completed";

    // Worker events
    public const string WorkerRegistered = "worker.registered";
    public const string WorkerDeregistered = "worker.deregistered";
    public const string WorkerUnregistered = "worker.unregistered";
    public const string WorkerHealthChanged = "worker.health_changed";
    public const string WorkerHealthy = "worker.healthy";
    public const string WorkerUnhealthy = "worker.unhealthy";
    public const string WorkerDrained = "worker.drained";
    public const string WorkerEnabled = "worker.enabled";

    // System events
    public const string SystemStarted = "system.started";
    public const string SystemShutdown = "system.shutdown";
}
