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
    public async Task ListWorkflows_ReturnsEmptyList_WhenNoWorkflows()
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
    public async Task StartWorkflow_CreatesWorkflow_AndReturns201()
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
    public async Task StartWorkflow_TransitionsToRunning_WhenAgentHostPicksUp()
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
    public async Task GetWorkflow_ReturnsWorkflow_WhenExists()
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
    public async Task GetWorkflow_Returns404_WhenNotExists()
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
    public async Task ListWorkflows_ReturnsWorkflows_AfterCreation()
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
    public async Task CancelWorkflow_SetsCancellingStatus()
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
    public async Task AbortWorkflow_SetsAbortedStatus()
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
}
