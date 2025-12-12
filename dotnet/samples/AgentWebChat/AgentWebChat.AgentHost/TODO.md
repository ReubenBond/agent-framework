# AgentWebChat.AgentHost Workflow Migration TODO

## Overview

The AgentWebChat.AgentHost sample currently uses a **custom `IWorkflow` interface** defined locally in `WorkflowHostService.cs`. This needs to be migrated to use the built-in `Microsoft.Agents.AI.Workflows.Workflow` type from the framework.

### Current State

- **Custom IWorkflow interface** defined in `Workflows/WorkflowHostService.cs:434-465`
- **Custom WorkflowExecutionContext** and **WorkflowResumeContext** classes
- **MarketingContentWorkflow** implements the custom `IWorkflow` interface
- Workflows are registered as `IWorkflow` keyed services in DI
- The app already has framework `Workflow` instances (science-sequential, science-concurrent workflows) registered via `builder.AddWorkflow()`

### Target State

- Use the built-in `Microsoft.Agents.AI.Workflows.Workflow` class
- Use `RequestPort`, `WorkflowBuilder`, `Executor<T>` types for HITL support
- Leverage `InProcessExecution`, `StreamingRun`, `CheckpointManager` for execution
- Unify workflow hosting between the simple workflows and HITL workflows

---

## Tasks

### Phase 1: Analysis and Preparation

- [ ] **1.1** Document the current `IWorkflow` interface contract
  - `Name`, `DisplayName`, `Description` properties
  - `ExecuteAsync()` returns `IAsyncEnumerable<WorkflowStatusEvent>`
  - `ResumeAsync()` for HITL continuation
  - Uses `WorkflowExecutionContext` and `WorkflowResumeContext`

- [ ] **1.2** Map custom types to framework equivalents
  | Custom Type | Framework Equivalent |
  |-------------|---------------------|
  | `IWorkflow` | `Workflow` (class, built via `WorkflowBuilder`) |
  | `WorkflowExecutionContext` | `IWorkflowContext` |
  | `WorkflowResumeContext` | `IWorkflowContext` + `CheckpointManager` |
  | `WorkflowStatusEvent` hierarchy | `WorkflowEvent` hierarchy |
  | `PendingExternalRequest` | `RequestPort` + `RequestInfoEvent` |
  | `WorkflowSignal` | `ExternalResponse` |
  | `WorkflowCheckpointData` | `CheckpointManager` + `CheckpointInfo` |

- [ ] **1.3** Study reference implementations
  - `GettingStarted/Workflows/HumanInTheLoop/HumanInTheLoopBasic/` - Basic HITL pattern
  - `GettingStarted/Workflows/Checkpoint/CheckpointWithHumanInTheLoop/` - HITL with checkpointing
  - `GettingStarted/Workflows/_Foundational/08_WriterCriticWorkflow/` - Iterative refinement pattern

---

### Phase 2: Create New Workflow Implementation

- [ ] **2.1** Create new `MarketingContentWorkflow` using framework types
  - File: `Workflows/MarketingContentWorkflowV2.cs` (new file)
  - Use `WorkflowBuilder` to construct the workflow
  - Define custom `Executor<T>` classes for:
    - `WriterExecutor` - AI content generation
    - `ReviewerExecutor` - AI content review
    - `ApprovalRequestPort` - Human approval (using `RequestPort`)

- [ ] **2.2** Implement `WriterExecutor : Executor<TurnToken, ContentData>`
  - Handle initial content creation based on input
  - Support revision mode with feedback from rejection

- [ ] **2.3** Implement `ReviewerExecutor : Executor<ContentData, ReviewedContent>`
  - Review content and add suggestions
  - Pass content to approval stage

- [ ] **2.4** Create `ApprovalRequestPort` using `RequestPort.Create<ApprovalRequest, ApprovalResponse>()`
  - Define request/response types for approval
  - Configure UI hints via metadata

- [ ] **2.5** Build workflow using `WorkflowBuilder`
  ```csharp
  // Pseudocode structure:
  var approvalPort = RequestPort.Create<ApprovalRequest, ApprovalResponse>("approval");
  var writer = new WriterExecutor(...);
  var reviewer = new ReviewerExecutor(...);
  
  return new WorkflowBuilder(writer)
      .AddEdge(writer, reviewer)
      .AddEdge(reviewer, approvalPort)
      .AddSwitch(approvalPort, sw => sw
          .AddCase<ApprovalResponse>(r => r.Decision == "approve", outputExecutor)
          .AddCase<ApprovalResponse>(r => r.Decision == "revise", writer))
      .WithOutputFrom(outputExecutor)
      .WithName("marketing-content")
      .Build();
  ```

---

### Phase 3: Update WorkflowHostService

- [ ] **3.1** Modify `WorkflowHostService` to support `Workflow` type
  - Accept both `IWorkflow` (legacy) and `Workflow` (framework) instances
  - Or fully migrate to `Workflow` only

- [ ] **3.2** Implement execution using `InProcessExecution`
  ```csharp
  await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input);
  await foreach (WorkflowEvent evt in run.WatchStreamAsync())
  {
      // Map WorkflowEvent to WorkflowStatusEvent for SSE
      yield return MapToStatusEvent(evt);
  }
  ```

- [ ] **3.3** Implement resume using `CheckpointManager`
  - Load checkpoint from `request.CheckpointData`
  - Use `InProcessExecution.ResumeStreamAsync(workflow, checkpoint, checkpointManager)`
  - Handle `RequestInfoEvent` for external input
  - Use `handle.SendResponseAsync()` for responses

- [ ] **3.4** Handle HITL via `RequestInfoEvent`
  ```csharp
  case RequestInfoEvent requestEvt:
      // Pause and signal to gateway that human input is needed
      yield return new WorkflowSignalRequestedEvent { Request = MapToPendingRequest(requestEvt) };
      break;
  ```

---

### Phase 4: Update Workflow Registration

- [ ] **4.1** Remove custom `IWorkflow` interface (or deprecate)
  - Move interface to a legacy/compat namespace if needed

- [ ] **4.2** Update `Program.cs` workflow registration
  ```csharp
  // Before (custom IWorkflow):
  builder.Services.AddKeyedSingleton<IWorkflow, MarketingContentWorkflow>("marketing-content");
  
  // After (framework Workflow):
  builder.AddWorkflow("marketing-content", (sp, key) =>
  {
      var chatClient = sp.GetRequiredKeyedService<IChatClient>("chat-model");
      return MarketingContentWorkflowFactory.Build(chatClient, key);
  });
  ```

- [ ] **4.3** Update `WorkflowHostEntityProvider` 
  - Discover `Workflow` instances from DI instead of `IWorkflow`
  - Use `workflow.Name`, `workflow.Description` properties
  - Extract executor info from `workflow.ExecutorBindings`

---

### Phase 5: Update HTTP API and Event Mapping

- [ ] **5.1** Update `WorkflowHttpApi.cs`
  - Inject `IServiceProvider` to resolve `Workflow` instances
  - Update SSE event mapping for framework `WorkflowEvent` types

- [ ] **5.2** Map framework events to SSE events
  | Framework Event | SSE Event Type |
  |-----------------|----------------|
  | `WorkflowStartedEvent` | `workflow.started` |
  | `ExecutorInvokedEvent` | `step.started` |
  | `ExecutorCompletedEvent` | `step.completed` |
  | `RequestInfoEvent` | `signal.requested` |
  | `WorkflowOutputEvent` | `workflow.completed` |
  | `WorkflowErrorEvent` | `workflow.failed` |

- [ ] **5.3** Update `GatewayWorkflowStateClient` if needed
  - May need to adapt callback payloads

---

### Phase 6: Testing and Cleanup

- [ ] **6.1** Create unit tests for new workflow
  - Test workflow construction
  - Test execution flow
  - Test HITL checkpoint/resume

- [ ] **6.2** Integration testing
  - Test with DevUI
  - Test with Gateway coordination
  - Test full HITL flow (pause -> resume)

- [ ] **6.3** Remove deprecated code
  - Delete `IWorkflow` interface
  - Delete `WorkflowExecutionContext` class
  - Delete `WorkflowResumeContext` class
  - Delete old `MarketingContentWorkflow` implementation

- [ ] **6.4** Update documentation
  - Update any sample docs
  - Add migration notes if this pattern is used elsewhere

---

## Key Reference Files

### Current Custom Implementation
- `Workflows/WorkflowHostService.cs` - Custom IWorkflow interface and host service
- `Workflows/MarketingContentWorkflow.cs` - Custom HITL workflow implementation
- `Workflows/WorkflowHttpApi.cs` - HTTP endpoints
- `Workflows/WorkflowHostEntityProvider.cs` - DevUI entity provider

### Framework Types (Target)
- `Microsoft.Agents.AI.Workflows/Workflow.cs` - Built-in Workflow class
- `Microsoft.Agents.AI.Workflows/WorkflowBuilder.cs` - Workflow construction
- `Microsoft.Agents.AI.Workflows/Executor.cs` - Base executor types
- `Microsoft.Agents.AI.Workflows/RequestPort.cs` - HITL request ports
- `Microsoft.Agents.AI.Workflows/InProcessExecution.cs` - Execution entry point
- `Microsoft.Agents.AI.Workflows/StreamingRun.cs` - Streaming execution handle
- `Microsoft.Agents.AI.Workflows/CheckpointManager.cs` - Checkpoint support

### Reference Samples
- `GettingStarted/Workflows/HumanInTheLoop/HumanInTheLoopBasic/` - Basic HITL
- `GettingStarted/Workflows/Checkpoint/CheckpointWithHumanInTheLoop/` - HITL + Checkpointing
- `GettingStarted/Workflows/_Foundational/08_WriterCriticWorkflow/` - Writer-Critic pattern

---

## Architecture Notes

### Current Custom Architecture
```
                  +-----------------+
                  |   AgentGateway  |
                  |   (Orleans)     |
                  +--------+--------+
                           |
            HTTP Callbacks | SSE Events
                           v
                  +--------+--------+
                  | WorkflowHost    |
                  | Service         |
                  +--------+--------+
                           |
                           v
                  +--------+--------+
                  |    IWorkflow    |  <-- CUSTOM INTERFACE
                  | (custom impl)   |
                  +-----------------+
```

### Target Framework Architecture
```
                  +-----------------+
                  |   AgentGateway  |
                  |   (Orleans)     |
                  +--------+--------+
                           |
            HTTP Callbacks | SSE Events
                           v
                  +--------+--------+
                  | WorkflowHost    |
                  | Service         |
                  +--------+--------+
                           |
                           v
                  +--------+--------+
                  |    Workflow     |  <-- FRAMEWORK TYPE
                  | (WorkflowBuilder)|
                  +--------+--------+
                           |
        +------------------+------------------+
        |                  |                  |
        v                  v                  v
   +----------+     +------------+     +------------+
   | Writer   |---->| Reviewer   |---->| Approval   |
   | Executor |     | Executor   |     | RequestPort|
   +----------+     +------------+     +-----+------+
                                             |
                    Loop on rejection  <-----+
```

### HITL Flow with Framework Types
1. Workflow starts with `InProcessExecution.StreamAsync(workflow, input)`
2. `StreamingRun.WatchStreamAsync()` yields `WorkflowEvent` instances
3. When `RequestInfoEvent` is received, pause and emit SSE `signal.requested`
4. Gateway stores checkpoint and pending request
5. User responds through Gateway API
6. Gateway calls AgentHost resume endpoint with signal
7. AgentHost uses `handle.SendResponseAsync(response)` to continue
8. Workflow resumes from `RequestPort`

---

## Questions / Decisions Needed

1. **Event Mapping Strategy**: Should we create adapter types or directly use framework events?
   - Option A: Map `WorkflowEvent` -> `WorkflowStatusEvent` at HTTP boundary
   - Option B: Update AgentContracts to use framework event types directly

2. **Backward Compatibility**: Should we maintain support for custom `IWorkflow`?
   - Option A: Remove completely (breaking change for this sample)
   - Option B: Support both with adapter pattern

3. **Checkpoint Storage**: Current impl uses `IWorkflowStateService` callbacks to Gateway
   - Need to adapt `CheckpointManager` to use same pattern, or
   - Create custom `CheckpointManager` implementation that calls Gateway

4. **Executor State**: Framework executors can override `OnCheckpointingAsync` / `OnCheckpointRestoredAsync`
   - Use this for revision count, previous outputs, etc.
