# Metrics/Observability

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

## Files to Modify
- `WorkerWorkflowExecutor.cs` - Add dispatch metrics
- `WorkflowHostService.cs` - Add execution metrics
- New: `WorkflowMetrics.cs` - Centralized metrics class

## Acceptance Criteria
- [ ] Metrics emitted for dispatch latency
- [ ] Metrics emitted for execution duration
- [ ] Metrics work with OpenTelemetry exporters
- [ ] Unit tests verify metrics are recorded
