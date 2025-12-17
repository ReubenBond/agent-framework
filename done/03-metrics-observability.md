# Metrics/Observability

## Status: COMPLETED

## Summary
Add telemetry to track dispatch latency vs execution time for workflow operations.

## Details
With the async dispatch pattern, it's important to have visibility into:
- **Dispatch latency**: Time from receiving request to returning 202 Accepted
- **Execution time**: Total time for workflow to complete
- **Callback latency**: Time for state updates to propagate back to gateway

## Proposed Metrics
1. `workflow.dispatch.duration` - Time to dispatch workflow to worker
2. `workflow.execution.duration` - Total workflow execution time
3. `workflow.callback.duration` - Time for state callbacks to complete
4. `workflow.dispatch.count` - Counter for workflow dispatches
5. `workflow.execution.success` / `workflow.execution.failure` - Success/failure counters

## Implementation Approach
- Use `System.Diagnostics.Metrics` for .NET metrics
- Create `WorkflowMetrics` class with meter and instruments
- Add OpenTelemetry instrumentation support
- Emit metrics at key points in the workflow lifecycle

## Files Modified
- `WorkerWorkflowExecutor.cs` - Added dispatch metrics via `RecordWorkerDispatch()`
- `WorkflowHostService.cs` - Added execution metrics via `RecordWorkflowStarted()`, `RecordWorkflowCompleted()`, `RecordWorkflowFailed()`, `RecordStepExecution()`
- `Microsoft.Agents.AI.Runtime.Abstractions/Telemetry/WorkflowMetrics.cs` - Centralized metrics class

## Acceptance Criteria
- [x] Metrics emitted for dispatch latency (`worker.dispatch.latency` histogram)
- [x] Metrics emitted for execution duration (`workflow.run.duration` histogram)
- [x] Metrics work with OpenTelemetry exporters (uses `System.Diagnostics.Metrics`)
- [x] Unit tests verify metrics are recorded (14 tests in `WorkflowMetricsTests.cs`)

## Completion Notes
- `WorkflowMetrics` class implements comprehensive instrumentation:
  - Worker dispatch metrics: `worker.dispatches.total`, `worker.dispatch.latency`, `worker.dispatches.failed`
  - Workflow run metrics: `workflow.runs.total`, `workflow.runs.active`, `workflow.runs.completed`, `workflow.runs.failed`, `workflow.run.duration`
  - Step execution metrics: `workflow.steps.total`, `workflow.step.duration`
  - Signal handling metrics: `workflow.runs.waiting_for_signal`, `workflow.signal.wait_time`
  - Worker health metrics: `worker.health_checks.total`, `worker.health_check.latency`, `worker.health_checks.failed`
- All 14 metrics unit tests pass in `WorkflowMetricsTests.cs`
