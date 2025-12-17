# Error Handling Enhancement

## Status: COMPLETED

## Summary
Improve error handling for background workflow execution failures.

## Details
Previously if background execution failed, it was only logged. This could lead to:
- Workflows appearing stuck in "running" state indefinitely
- No notification to the gateway that execution failed
- Difficult to debug silent failures

## Proposed Changes
1. Add callback to notify gateway of execution failures
2. Implement dead-letter queue pattern for failed workflows
3. Add workflow state "Failed" with error details
4. Consider adding health check endpoint for workflow status

## Implementation Approach
- On background execution failure, make HTTP callback to gateway with error details
- Gateway updates workflow state to "Failed" with error information
- Add `/v1/workflow-host/failure` endpoint or use existing callback mechanism
- Log correlation ID for tracing failures

## Files Modified
- `WorkflowHostService.cs` - Added `NotifyGatewayOfFailureAsync()` helper method and enhanced `ExecuteInBackgroundAsync()` and `ResumeInBackgroundAsync()` to notify Gateway on failures
- `GatewayWorkflowStateClient.cs` - Implements `IWorkflowStateService` for HTTP callbacks to Gateway
- Gateway grain already handles failure status updates via `UpdateStatusAsync()`

## Acceptance Criteria
- [x] Failed workflows are marked as failed in gateway (via `SafeUpdateStatusAsync()` with `WorkflowRunStatus.Failed`)
- [x] Error details are captured and stored (`WorkflowErrorInfo` with Code, Message, StackTrace)
- [x] Failures are properly logged with correlation (RunId included in all log messages)
- [x] Client can see failure status via API (`WorkflowRun.Error` property exposed via GET `/v1/workflows/{id}`)

## Completion Notes
- Inner execution (`ExecuteWorkflowCoreAsync`, `ResumeWorkflowCoreAsync`) already had comprehensive error handling with `SafeUpdateStatusAsync()`
- Added `NotifyGatewayOfFailureAsync()` method to handle edge cases where exceptions escape the core execution methods
- Enhanced `ExecuteInBackgroundAsync()` and `ResumeInBackgroundAsync()` to call Gateway notification on any unhandled exception
- Error codes used: `WORKFLOW_EXECUTION_ERROR`, `WORKFLOW_RESUME_ERROR`, `BACKGROUND_EXECUTION_ERROR`, `BACKGROUND_RESUME_ERROR`
- All 28 AgentHost unit tests pass
