// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.Runtime.Abstractions.Telemetry;

/// <summary>
/// Constants for telemetry across the Runtime.
/// </summary>
public static class TelemetryConstants
{
    // ============ Activity Source Names ============

    /// <summary>
    /// The name of the activity source for workflow operations.
    /// </summary>
    public const string WorkflowActivitySourceName = "Microsoft.Agents.AI.Runtime.Workflows";

    /// <summary>
    /// The name of the activity source for monitoring operations.
    /// </summary>
    public const string MonitoringActivitySourceName = "Microsoft.Agents.AI.Runtime.Monitoring";

    // ============ Meter Names ============

    /// <summary>
    /// The name of the meter for workflow metrics.
    /// </summary>
    public const string WorkflowMeterName = "Microsoft.Agents.AI.Runtime.Workflows";

    /// <summary>
    /// The name of the meter for worker metrics.
    /// </summary>
    public const string WorkerMeterName = "Microsoft.Agents.AI.Runtime.Workers";

    // ============ Workflow Tags ============

    /// <summary>Workflow run identifier.</summary>
    public const string WorkflowRunId = "workflow.run_id";

    /// <summary>Workflow name.</summary>
    public const string WorkflowName = "workflow.name";

    /// <summary>Current workflow status.</summary>
    public const string WorkflowStatus = "workflow.status";

    /// <summary>Previous workflow status (for transitions).</summary>
    public const string WorkflowPreviousStatus = "workflow.previous_status";

    /// <summary>Workflow error code.</summary>
    public const string WorkflowErrorCode = "workflow.error_code";

    // ============ Step Tags ============

    /// <summary>Step identifier.</summary>
    public const string StepId = "workflow.step_id";

    /// <summary>Step name.</summary>
    public const string StepName = "workflow.step_name";

    /// <summary>Step executor identifier.</summary>
    public const string StepExecutorId = "workflow.step_executor_id";

    /// <summary>Step executor name.</summary>
    public const string StepExecutorName = "workflow.step_executor_name";

    /// <summary>Step duration in milliseconds.</summary>
    public const string StepDurationMs = "workflow.step_duration_ms";

    // ============ Signal Tags ============

    /// <summary>Signal request identifier.</summary>
    public const string SignalRequestId = "workflow.signal_request_id";

    /// <summary>Signal port identifier.</summary>
    public const string SignalPortId = "workflow.signal_port_id";

    // ============ Checkpoint Tags ============

    /// <summary>Checkpoint identifier.</summary>
    public const string CheckpointId = "workflow.checkpoint_id";

    // ============ Worker Tags ============

    /// <summary>Worker identifier.</summary>
    public const string WorkerId = "worker.id";

    /// <summary>Worker host identifier.</summary>
    public const string WorkerHostId = "worker.host_id";

    /// <summary>Worker address/URL.</summary>
    public const string WorkerAddress = "worker.address";

    // ============ Orleans Tags ============

    /// <summary>Orleans grain type.</summary>
    public const string OrleansGrainType = "orleans.grain_type";

    /// <summary>Orleans grain key.</summary>
    public const string OrleansGrainKey = "orleans.grain_key";

    // ============ HTTP Tags ============

    /// <summary>HTTP method.</summary>
    public const string HttpMethod = "http.method";

    /// <summary>HTTP path.</summary>
    public const string HttpPath = "http.path";

    /// <summary>HTTP status code.</summary>
    public const string HttpStatusCode = "http.status_code";

    // ============ Event Names ============

    /// <summary>Event name for workflow state changes.</summary>
    public const string EventWorkflowStateChanged = "workflow.state_changed";

    /// <summary>Event name for signal received.</summary>
    public const string EventSignalReceived = "signal.received";

    /// <summary>Event name for checkpoint saved.</summary>
    public const string EventCheckpointSaved = "checkpoint.saved";

    /// <summary>Event name for step started.</summary>
    public const string EventStepStarted = "step.started";

    /// <summary>Event name for step completed.</summary>
    public const string EventStepCompleted = "step.completed";
}
