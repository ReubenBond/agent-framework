# Error Handling Enhancement

## Summary
Improve error handling for background workflow execution failures.

## Details
Currently if background execution fails, it's only logged. This could lead to:
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

## Files to Modify
- `WorkflowHostService.cs` - Add failure callback
- `WorkflowHttpApi.cs` - Handle failure notifications
- Gateway side: Add failure handling in grain

## Acceptance Criteria
- [ ] Failed workflows are marked as failed in gateway
- [ ] Error details are captured and stored
- [ ] Failures are properly logged with correlation
- [ ] Client can see failure status via API
