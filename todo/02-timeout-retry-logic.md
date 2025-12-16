# Timeout/Retry Logic

## Summary
Add configurable timeouts and retry logic for background workflow execution.

## Details
The background execution currently uses `CancellationToken.None` which means:
- No timeout on workflow execution
- If a workflow hangs, it runs indefinitely
- No automatic retry on transient failures

## Proposed Changes
1. Add configurable timeout settings in `WorkflowHostOptions`
2. Implement timeout cancellation in `ExecuteInBackgroundAsync` and `ResumeInBackgroundAsync`
3. Add retry policy with exponential backoff for transient HTTP failures
4. Consider using Polly for resilience patterns

## Files to Modify
- `AgentWebChat.AgentHost/Workflows/WorkflowHostService.cs`
- `AgentWebChat.AgentHost/Workflows/WorkflowHostOptions.cs` (new or existing)
- Potentially add Polly NuGet package

## Acceptance Criteria
- [ ] Configurable timeout for workflow execution
- [ ] Workflows are cancelled if they exceed timeout
- [ ] Transient failures trigger automatic retry
- [ ] Max retry count is configurable
- [ ] Proper logging for timeout and retry events
