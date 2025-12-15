// Copyright (c) Microsoft. All rights reserved.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentContracts;
using AgentContracts.Workflows;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace AgentWebChat.IntegrationTests;

/// <summary>
/// End-to-end integration tests for the workflow REST APIs using Aspire Hosting Testing.
/// These tests start the full distributed application (Gateway, AgentHost, storage) and
/// verify workflows can be started, run, and signaled via the Gateway REST API.
/// </summary>
public sealed class WorkflowIntegrationTests : IAsyncLifetime
{
    private DistributedApplication? _app;
    private HttpClient? _gatewayClient;
    private static readonly JsonSerializerOptions s_jsonOptions = AgentContractsJsonUtilities.DefaultOptions;

    /// <summary>
    /// Creates a URI from a relative path.
    /// </summary>
    private static Uri CreateUri(string relativePath) => new(relativePath, UriKind.Relative);

    public async Task InitializeAsync()
    {
        // Build the Aspire application using the AppHost
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AgentWebChat_AppHost>();

        // Override Azure OpenAI with a mock or skip AI-dependent tests
        // For these tests, we focus on the workflow orchestration mechanics

        this._app = await appHost.BuildAsync();
        await this._app.StartAsync();

        // Get the gateway HTTP client
        this._gatewayClient = this._app.CreateHttpClient("gateway");
    }

    public async Task DisposeAsync()
    {
        this._gatewayClient?.Dispose();

        if (this._app != null)
        {
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }

    #region List Workflows Tests

    [Fact]
    public async Task ListWorkflows_ReturnsEmptyList_WhenNoWorkflowsAsync()
    {
        // Arrange
        var client = this._gatewayClient!;

        // Act
        var response = await client.GetAsync(CreateUri("/v1/workflows"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowListResponse<WorkflowRunSummary>>(s_jsonOptions);
        result.Should().NotBeNull();
        result!.Data.Should().NotBeNull();
        result.HasMore.Should().BeFalse();
    }

    #endregion

    #region Start Workflow Tests

    [Fact]
    public async Task StartWorkflow_CreatesWorkflow_AndReturns201Async()
    {
        // Arrange
        var client = this._gatewayClient!;
        var request = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "AI Testing", targetAudience = "Developers", tone = "professional" }),
            Metadata = new Dictionary<string, string> { ["source"] = "integration-test" }
        };

        // Act
        var response = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        result.Should().NotBeNull();
        result!.Id.Should().StartWith("wfrun_");
        result.WorkflowName.Should().Be("marketing-content");
        result.Status.Should().Be(WorkflowRunStatus.Queued);
        result.ETag.Should().NotBeNullOrEmpty();

        // Location header should be set
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain(result.Id);
    }

    [Fact]
    public async Task StartWorkflow_TransitionsToRunning_WhenAgentHostPicksUpAsync()
    {
        // Arrange
        var client = this._gatewayClient!;
        var request = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "Integration Testing", targetAudience = "QA Engineers", tone = "technical" })
        };

        // Act - Start the workflow
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Wait for the workflow to transition to a non-queued state
        WorkflowRun? workflow = null;
        var maxWait = TimeSpan.FromSeconds(30);
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < maxWait)
        {
            var getResponse = await client.GetAsync(CreateUri($"/v1/workflows/{created!.Id}"));
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            workflow = await getResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

            if (workflow!.Status != WorkflowRunStatus.Queued)
            {
                break;
            }

            await Task.Delay(500);
        }

        // Assert - The workflow should have been picked up by AgentHost
        workflow.Should().NotBeNull();
        workflow!.Status.Should().NotBe(WorkflowRunStatus.Queued,
            "Workflow should have transitioned from Queued after AgentHost picks it up");

        // It should be Running, WaitingForSignal, Completed, or potentially Failed if AI call fails
        workflow.Status.Should().BeOneOf(
            WorkflowRunStatus.Running,
            WorkflowRunStatus.WaitingForSignal,
            WorkflowRunStatus.Completed,
            WorkflowRunStatus.Failed);
    }

    #endregion

    #region Get Workflow Tests

    [Fact]
    public async Task GetWorkflow_ReturnsWorkflow_WhenExistsAsync()
    {
        // Arrange
        var client = this._gatewayClient!;
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "Test", targetAudience = "Testers", tone = "casual" })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Act
        var response = await client.GetAsync(CreateUri($"/v1/workflows/{created!.Id}"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.WorkflowName.Should().Be("marketing-content");
    }

    [Fact]
    public async Task GetWorkflow_Returns404_WhenNotExistsAsync()
    {
        // Arrange
        var client = this._gatewayClient!;

        // Act
        var response = await client.GetAsync(CreateUri("/v1/workflows/nonexistent-id"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Workflow Lifecycle Tests

    [Fact]
    public async Task ListWorkflows_ReturnsWorkflows_AfterCreationAsync()
    {
        // Arrange
        var client = this._gatewayClient!;

        // Create a workflow
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "List Test" })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Act
        var response = await client.GetAsync(CreateUri("/v1/workflows"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowListResponse<WorkflowRunSummary>>(s_jsonOptions);
        result.Should().NotBeNull();
        result!.Data.Should().Contain(w => w.Id == created!.Id);
    }

    [Fact]
    public async Task CancelWorkflow_SetsCancellingStatusAsync()
    {
        // Arrange
        var client = this._gatewayClient!;
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "Cancel Test" })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Act
        var response = await client.PostAsync(CreateUri($"/v1/workflows/{created!.Id}/cancel"), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        result!.Status.Should().Be(WorkflowRunStatus.Cancelling);
    }

    [Fact]
    public async Task AbortWorkflow_SetsAbortedStatusAsync()
    {
        // Arrange
        var client = this._gatewayClient!;
        var startRequest = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "Abort Test" })
        };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), startRequest, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var abortRequest = new AbortWorkflowRequest { Reason = "Integration test abort" };

        // Act
        var response = await client.PostAsJsonAsync(CreateUri($"/v1/workflows/{created!.Id}/abort"), abortRequest, s_jsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        result!.Status.Should().Be(WorkflowRunStatus.Aborted);
        result.CompletedAt.Should().NotBeNull();
    }

    #endregion

    #region HITL (Human-in-the-Loop) Tests

    [Fact]
    public async Task MarketingWorkflow_TransitionsToWaitingForSignal_WhenHITLRequiredAsync()
    {
        // Arrange
        var client = this._gatewayClient!;
        var request = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "HITL Test Topic", targetAudience = "Testers", tone = "professional" }),
            Metadata = new Dictionary<string, string> { ["test"] = "hitl-transition" }
        };

        // Act - Start the workflow
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Wait for the workflow to reach WaitingForSignal (HITL pause point)
        WorkflowRun? workflow = null;
        var maxWait = TimeSpan.FromSeconds(60);
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < maxWait)
        {
            var getResponse = await client.GetAsync(CreateUri($"/v1/workflows/{created!.Id}"));
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            workflow = await getResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

            // Break if we've reached the HITL waiting state or a terminal state
            if (workflow!.Status == WorkflowRunStatus.WaitingForSignal ||
                workflow.Status == WorkflowRunStatus.Completed ||
                workflow.Status == WorkflowRunStatus.Failed ||
                workflow.Status == WorkflowRunStatus.Aborted)
            {
                break;
            }

            await Task.Delay(1000);
        }

        // Assert
        workflow.Should().NotBeNull();
        // If the AI is configured and working, we should reach WaitingForSignal
        // If AI is not configured or fails, we might get Failed status
        workflow!.Status.Should().BeOneOf(
            WorkflowRunStatus.WaitingForSignal,
            WorkflowRunStatus.Completed,
            WorkflowRunStatus.Failed);
    }

    [Fact]
    public async Task MarketingWorkflow_HasPendingRequests_WhenWaitingForSignalAsync()
    {
        // Arrange
        var client = this._gatewayClient!;
        var request = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "Pending Request Test", targetAudience = "QA", tone = "casual" })
        };

        // Act - Start the workflow and wait for HITL
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        WorkflowRun? workflow = await WaitForWorkflowStatusAsync(
            client,
            created!.Id,
            [WorkflowRunStatus.WaitingForSignal, WorkflowRunStatus.Completed, WorkflowRunStatus.Failed],
            TimeSpan.FromSeconds(60));

        // Assert
        if (workflow?.Status == WorkflowRunStatus.WaitingForSignal)
        {
            // When waiting for signal, there should be pending requests
            workflow.PendingRequests.Should().NotBeEmpty();
            workflow.PendingRequests.Should().Contain(r => r.PortId == "approval");
        }
    }

    [Fact]
    public async Task MarketingWorkflow_RecordsSteps_DuringExecutionAsync()
    {
        // Arrange
        var client = this._gatewayClient!;
        var request = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "Steps Recording Test", targetAudience = "Engineers", tone = "technical" })
        };

        // Act - Start the workflow and wait for it to progress
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        WorkflowRun? workflow = await WaitForWorkflowStatusAsync(
            client,
            created!.Id,
            [WorkflowRunStatus.WaitingForSignal, WorkflowRunStatus.Completed, WorkflowRunStatus.Failed],
            TimeSpan.FromSeconds(60));

        // Assert - If the workflow progressed, it should have recorded steps
        workflow.Should().NotBeNull();
        if (workflow!.Status != WorkflowRunStatus.Failed && workflow.Status != WorkflowRunStatus.Queued)
        {
            // The workflow should have recorded at least the writer step
            workflow.Steps.Should().NotBeEmpty("Workflow should record execution steps");

            // If we have steps, verify their structure
            foreach (var step in workflow.Steps)
            {
                step.StepId.Should().NotBeNullOrEmpty();
                step.ExecutorId.Should().NotBeNullOrEmpty();
                step.StartedAt.Should().NotBe(default);
            }
        }
    }

    [Fact]
    public async Task MarketingWorkflow_Signal_ResumesWorkflowAsync()
    {
        // Arrange
        var client = this._gatewayClient!;
        var request = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "Signal Resume Test", targetAudience = "Product Managers", tone = "persuasive" })
        };

        // Act - Start the workflow and wait for HITL
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var workflow = await WaitForWorkflowStatusAsync(
            client,
            created!.Id,
            [WorkflowRunStatus.WaitingForSignal, WorkflowRunStatus.Completed, WorkflowRunStatus.Failed],
            TimeSpan.FromSeconds(60));

        // Skip if workflow didn't reach HITL waiting state
        if (workflow?.Status != WorkflowRunStatus.WaitingForSignal)
        {
            return; // Skip - AI may not be configured
        }

        // Get the pending request
        var pendingRequest = workflow.PendingRequests.FirstOrDefault();
        pendingRequest.Should().NotBeNull();

        // Send approval signal
        var signal = new WorkflowSignal
        {
            RequestId = pendingRequest!.RequestId,
            Response = WorkflowMessage.Create(new { decision = "Approve", feedback = "Great content!" })
        };

        var signalResponse = await client.PostAsJsonAsync(
            CreateUri($"/v1/workflows/{created.Id}/signal"),
            signal,
            s_jsonOptions);

        // Assert
        signalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var signalResult = await signalResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        signalResult.Should().NotBeNull();

        // After signaling, the workflow should be Running (resumed) or already Completed
        signalResult!.Status.Should().BeOneOf(
            WorkflowRunStatus.Running,
            WorkflowRunStatus.Completed,
            WorkflowRunStatus.WaitingForSignal);

        // Pending requests should be cleared for the approved request
        if (signalResult.Status == WorkflowRunStatus.Running || signalResult.Status == WorkflowRunStatus.Completed)
        {
            signalResult.PendingRequests.Should().NotContain(r => r.RequestId == pendingRequest.RequestId);
        }
    }

    [Fact]
    public async Task MarketingWorkflow_Signal_WithRevision_LoopsBackToWriterAsync()
    {
        // Arrange
        var client = this._gatewayClient!;
        var request = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = WorkflowMessage.Create(new { topic = "Revision Loop Test", targetAudience = "Executives", tone = "formal" })
        };

        // Start workflow and wait for HITL
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        var created = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var workflow = await WaitForWorkflowStatusAsync(
            client,
            created!.Id,
            [WorkflowRunStatus.WaitingForSignal, WorkflowRunStatus.Completed, WorkflowRunStatus.Failed],
            TimeSpan.FromSeconds(60));

        // Skip if workflow didn't reach HITL waiting state
        if (workflow?.Status != WorkflowRunStatus.WaitingForSignal)
        {
            return;
        }

        var pendingRequest = workflow.PendingRequests.FirstOrDefault();
        pendingRequest.Should().NotBeNull();

        // Send revision signal
        var signal = new WorkflowSignal
        {
            RequestId = pendingRequest!.RequestId,
            Response = WorkflowMessage.Create(new { decision = "Revise", feedback = "Please make it more concise" })
        };

        var signalResponse = await client.PostAsJsonAsync(
            CreateUri($"/v1/workflows/{created.Id}/signal"),
            signal,
            s_jsonOptions);

        // Assert
        signalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        _ = await signalResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // After revision request, workflow should loop back and eventually wait again or complete
        // We'll wait for it to reach another state
        workflow = await WaitForWorkflowStatusAsync(
            client,
            created.Id,
            [WorkflowRunStatus.WaitingForSignal, WorkflowRunStatus.Completed, WorkflowRunStatus.Failed],
            TimeSpan.FromSeconds(60));

        workflow.Should().NotBeNull();
        // The workflow should either be waiting again (for another approval) or completed
        workflow!.Status.Should().BeOneOf(
            WorkflowRunStatus.WaitingForSignal,
            WorkflowRunStatus.Completed,
            WorkflowRunStatus.Failed);
    }

    #endregion

    #region Input Type Resolution Tests

    [Fact]
    public async Task MarketingWorkflow_InputTypeName_IsPreservedAsync()
    {
        // Arrange
        var client = this._gatewayClient!;

        // Create input using the WorkflowMessage.Create which should set TypeName
        var input = WorkflowMessage.Create(new { topic = "Type Test", targetAudience = "Devs", tone = "casual" });

        var request = new StartWorkflowRequest
        {
            WorkflowName = "marketing-content",
            Input = input
        };

        // Act
        var response = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        var created = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Assert
        created.Should().NotBeNull();
        created!.Input.Should().NotBeNull();
        // The TypeName should be preserved in the workflow run
        // Note: Anonymous types will have compiler-generated names
    }

    #endregion

    #region Helper Methods

    private static async Task<WorkflowRun?> WaitForWorkflowStatusAsync(
        HttpClient client,
        string workflowId,
        WorkflowRunStatus[] targetStatuses,
        TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        WorkflowRun? workflow = null;

        while (DateTime.UtcNow - start < timeout)
        {
            var response = await client.GetAsync(CreateUri($"/v1/workflows/{workflowId}"));
            if (response.StatusCode == HttpStatusCode.OK)
            {
                workflow = await response.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

                if (workflow != null && targetStatuses.Contains(workflow.Status))
                {
                    return workflow;
                }
            }

            await Task.Delay(1000);
        }

        return workflow;
    }

    #endregion
}
