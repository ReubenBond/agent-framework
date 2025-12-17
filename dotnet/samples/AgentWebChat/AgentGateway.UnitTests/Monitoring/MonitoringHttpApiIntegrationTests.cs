// Copyright (c) Microsoft. All rights reserved.

using System.Net;
using System.Text.Json;
using Microsoft.Agents.AI.Runtime;
using Microsoft.Agents.AI.Runtime.Abstractions;
using Microsoft.Agents.AI.Runtime.Abstractions.Monitoring;
using Microsoft.Agents.AI.Runtime.Abstractions.Workers;
using Microsoft.Agents.AI.Runtime.Abstractions.Workflows;
using Microsoft.Agents.AI.Runtime.Hosting;
using Microsoft.Agents.AI.Runtime.Orleans.Monitoring;
using Microsoft.Agents.AI.Runtime.Orleans.Workflows;
using Microsoft.Agents.AI.Runtime.Workers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Moq;
using Orleans.Serialization;
using Orleans.Storage;

namespace AgentGateway.UnitTests.Monitoring;

/// <summary>
/// Integration tests for the MonitoringHttpApi endpoints.
/// Uses ASP.NET Core TestServer with an in-memory Orleans cluster.
/// </summary>
public sealed class MonitoringHttpApiIntegrationTests : IAsyncDisposable
{
    private WebApplication? _app;
    private HttpClient? _httpClient;

    private static readonly JsonSerializerOptions s_jsonOptions = RuntimeJsonUtilities.DefaultOptions;

    /// <summary>
    /// Creates a URI from a relative path.
    /// </summary>
    private static Uri CreateUri(string relativePath) => new(relativePath, UriKind.Relative);

    #region Setup

    private async Task<HttpClient> CreateTestServerAsync(Action<RuntimeOptions>? configureOptions = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Configure System.Text.Json serialization for Orleans grain method parameters
        builder.Services.AddSerializer(serializerBuilder =>
        {
            serializerBuilder.AddJsonSerializer(
                isSupported: type => !typeof(Exception).IsAssignableFrom(type) &&
                                    (type.Namespace?.StartsWith("Microsoft.Agents", StringComparison.Ordinal) == true ||
                                     type.Namespace?.StartsWith("AgentContracts", StringComparison.Ordinal) == true ||
                                     type.Namespace?.StartsWith("AgentGateway", StringComparison.Ordinal) == true),
                jsonSerializerOptions: AgentGatewayJsonUtilities.DefaultOptions);
        });

        // Configure Orleans with in-memory storage
        builder.Host.UseOrleans(siloBuilder =>
        {
            siloBuilder.UseLocalhostClustering();
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.UseInMemoryReminderService();

            // Register System.Text.Json-based grain storage serializer
            siloBuilder.Services.AddSingleton<IGrainStorageSerializer>(sp =>
                new Utilities.SystemTextJsonGrainStorageSerializer(AgentGatewayJsonUtilities.DefaultOptions));

            // Register mock IWorkflowExecutor for workflow grain
            var mockWorkflowExecutor = new Mock<IWorkflowExecutor>();
            mockWorkflowExecutor
                .Setup(x => x.ExecuteAsync(
                    It.IsAny<WorkflowExecutionRequest>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkflowExecutionResult { Success = true, WorkerId = "test-worker-1" });
            mockWorkflowExecutor
                .Setup(x => x.ResumeAsync(
                    It.IsAny<WorkflowResumeRequest>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkflowExecutionResult { Success = true, WorkerId = "test-worker-1" });
            siloBuilder.Services.AddSingleton(_ => mockWorkflowExecutor.Object);
        });

        // Configure RuntimeOptions with workers
        builder.Services.Configure<RuntimeOptions>(options =>
        {
            // Add a default worker
            options.Workers.Add(new WorkerConfiguration
            {
                Endpoint = "http://localhost:5001",
                HostId = "test-worker-1"
            });

            // Allow customization
            configureOptions?.Invoke(options);
        });

        // Register WorkerRegistry (WorkerDiscoveryCache is nullable, so pass null)
        builder.Services.AddSingleton<WorkerRegistry>(sp =>
            new WorkerRegistry(null, sp.GetRequiredService<IOptions<RuntimeOptions>>()));

        // Register MonitoringEventBroadcaster
        var mockEventBroadcaster = new Mock<IMonitoringEventBroadcaster>();
        mockEventBroadcaster
            .Setup(x => x.SubscribeAsync(It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<MonitoringEvent>());
        builder.Services.AddSingleton(_ => mockEventBroadcaster.Object);

        // Register OrleansMonitoringService
        builder.Services.AddSingleton<IMonitoringService, OrleansMonitoringService>();

        // Configure AgentGatewayOptions
        builder.Services.Configure<AgentGatewayOptions>(options => options.CallbackBaseUrl = "http://localhost:5000");

        this._app = builder.Build();

        // Map API endpoints
        this._app.MapMonitoringApi();
        this._app.MapWorkflowApi();

        await this._app.StartAsync();

        var testServer = this._app.Services.GetRequiredService<IServer>() as TestServer
            ?? throw new InvalidOperationException("TestServer not found");

        this._httpClient = testServer.CreateClient();
        return this._httpClient;
    }

    public async ValueTask DisposeAsync()
    {
        this._httpClient?.Dispose();
        if (this._app != null)
        {
            await this._app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    #endregion

    #region System Status Tests

    [Fact]
    public async Task GetSystemStatus_ReturnsHealthyStatus_WhenWorkersExistAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/status"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<SystemStatus>(s_jsonOptions);
        status.Should().NotBeNull();
        status!.Status.Should().Be("Healthy");
        status.TotalWorkers.Should().Be(1);
        status.ActiveWorkers.Should().Be(1);
        status.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetSystemStatus_ReturnsCorrectWorkflowCountsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();
        var grainFactory = this._app!.Services.GetRequiredService<IGrainFactory>();

        // Create workflows with different statuses
        var request1 = new StartWorkflowRequest { WorkflowName = "Workflow1", Input = WorkflowMessage.Create(new { id = 1 }) };
        var response1 = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request1, s_jsonOptions);
        var run1 = await response1.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var request2 = new StartWorkflowRequest { WorkflowName = "Workflow2", Input = WorkflowMessage.Create(new { id = 2 }) };
        await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request2, s_jsonOptions);

        // Update first workflow to Running
        var grain1 = grainFactory.GetGrain<IWorkflowGrain>(run1!.Id);
        await grain1.UpdateStatusAsync(new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Running }, null, CancellationToken.None);

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/status"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<SystemStatus>(s_jsonOptions);
        status.Should().NotBeNull();
        // 1 running workflow is "active", 1 queued workflow is "pending"
        status!.ActiveWorkflows.Should().BeGreaterThanOrEqualTo(1);
        status.PendingWorkflows.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Worker Tests

    [Fact]
    public async Task GetWorkers_ReturnsWorkerList_WhenWorkersConfiguredAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workers"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workers = await response.Content.ReadFromJsonAsync<WorkerStatus[]>(s_jsonOptions);
        workers.Should().NotBeNull();
        workers.Should().HaveCount(1);
        workers![0].HostId.Should().Be("test-worker-1");
        workers[0].Status.Should().Be("Healthy");
        workers[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task GetWorkers_ReturnsEmptyList_WhenNoWorkersAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync(options => options.Workers.Clear());

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workers"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workers = await response.Content.ReadFromJsonAsync<WorkerStatus[]>(s_jsonOptions);
        workers.Should().NotBeNull();
        workers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWorker_ReturnsWorker_WhenExistsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act - Use the default worker ID
        var response = await client.GetAsync(CreateUri($"/v1/monitor/workers/{WorkerRegistry.DefaultWorkerId}"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var worker = await response.Content.ReadFromJsonAsync<WorkerStatus>(s_jsonOptions);
        worker.Should().NotBeNull();
        worker!.HostId.Should().Be("test-worker-1");
        worker.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task GetWorker_Returns404_WhenNotExistsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workers/nonexistent-worker"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DrainWorker_Returns404_WhenWorkerNotFoundAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act
        var response = await client.PostAsync(CreateUri("/v1/monitor/workers/nonexistent-worker/drain"), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DrainWorker_ReturnsOk_WhenWorkerExistsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act
        var response = await client.PostAsync(CreateUri($"/v1/monitor/workers/{WorkerRegistry.DefaultWorkerId}/drain"), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EnableWorker_Returns404_WhenWorkerNotFoundAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act
        var response = await client.PostAsync(CreateUri("/v1/monitor/workers/nonexistent-worker/enable"), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EnableWorker_ReturnsOk_WhenWorkerExistsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act
        var response = await client.PostAsync(CreateUri($"/v1/monitor/workers/{WorkerRegistry.DefaultWorkerId}/enable"), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Workflow Monitoring Tests

    [Fact]
    public async Task GetActiveWorkflows_ReturnsEmptyList_WhenNoActiveWorkflowsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workflows/active"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflows = await response.Content.ReadFromJsonAsync<WorkflowMonitoringSummary[]>(s_jsonOptions);
        workflows.Should().NotBeNull();
        workflows.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveWorkflows_ReturnsRunningWorkflowsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();
        var grainFactory = this._app!.Services.GetRequiredService<IGrainFactory>();

        // Create a workflow and set it to running
        var request = new StartWorkflowRequest { WorkflowName = "TestWorkflow", Input = WorkflowMessage.Create(new { test = true }) };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        var run = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        var grain = grainFactory.GetGrain<IWorkflowGrain>(run!.Id);
        await grain.UpdateStatusAsync(new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Running }, null, CancellationToken.None);

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workflows/active"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflows = await response.Content.ReadFromJsonAsync<WorkflowMonitoringSummary[]>(s_jsonOptions);
        workflows.Should().NotBeNull();
        workflows.Should().Contain(w => w.RunId == run.Id && w.Status == "Running");
    }

    [Fact]
    public async Task GetActiveWorkflows_ReturnsQueuedWorkflowsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Create a workflow (initially queued)
        var request = new StartWorkflowRequest { WorkflowName = "TestWorkflow", Input = WorkflowMessage.Create(new { test = true }) };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        var run = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workflows/active"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflows = await response.Content.ReadFromJsonAsync<WorkflowMonitoringSummary[]>(s_jsonOptions);
        workflows.Should().NotBeNull();
        workflows.Should().Contain(w => w.RunId == run!.Id && w.Status == "Queued");
    }

    [Fact]
    public async Task GetActiveWorkflows_ReturnsWaitingWorkflowsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();
        var grainFactory = this._app!.Services.GetRequiredService<IGrainFactory>();

        // Create a workflow and set it to waiting
        var request = new StartWorkflowRequest { WorkflowName = "TestWorkflow", Input = WorkflowMessage.Create(new { test = true }) };
        var startResponse = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        var run = await startResponse.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Add a pending request to make it waiting
        var grain = grainFactory.GetGrain<IWorkflowGrain>(run!.Id);
        await grain.RecordPendingRequestAsync(
            new PendingExternalRequest
            {
                RequestId = "req-1",
                PortId = "approval",
                RequestTypeName = "ApprovalRequest",
                ResponseTypeName = "ApprovalResponse",
                RequestData = WorkflowMessage.Create(new { content = "Review this" }),
                Title = "Approve Content",
                RequestedAt = DateTimeOffset.UtcNow
            },
            null,
            CancellationToken.None);

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workflows/active"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflows = await response.Content.ReadFromJsonAsync<WorkflowMonitoringSummary[]>(s_jsonOptions);
        workflows.Should().NotBeNull();
        workflows.Should().Contain(w => w.RunId == run.Id && w.Status == "WaitingForSignal");
    }

    [Fact]
    public async Task GetRecentWorkflows_ReturnsEmptyList_WhenNoWorkflowsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workflows/recent"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflows = await response.Content.ReadFromJsonAsync<WorkflowMonitoringSummary[]>(s_jsonOptions);
        workflows.Should().NotBeNull();
        workflows.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentWorkflows_ReturnsWorkflowsInOrderAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Create workflows
        var request1 = new StartWorkflowRequest { WorkflowName = "Workflow1", Input = WorkflowMessage.Create(new { id = 1 }) };
        var startResponse1 = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request1, s_jsonOptions);
        var run1 = await startResponse1.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Add a small delay to ensure different creation times
        await Task.Delay(10);

        var request2 = new StartWorkflowRequest { WorkflowName = "Workflow2", Input = WorkflowMessage.Create(new { id = 2 }) };
        var startResponse2 = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request2, s_jsonOptions);
        var run2 = await startResponse2.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workflows/recent"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflows = await response.Content.ReadFromJsonAsync<WorkflowMonitoringSummary[]>(s_jsonOptions);
        workflows.Should().NotBeNull();
        workflows.Should().HaveCount(2);

        // Should be in reverse chronological order
        workflows![0].RunId.Should().Be(run2!.Id);
        workflows[1].RunId.Should().Be(run1!.Id);
    }

    [Fact]
    public async Task GetRecentWorkflows_RespectsCountParameterAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Create 5 workflows
        for (int i = 0; i < 5; i++)
        {
            var request = new StartWorkflowRequest { WorkflowName = $"Workflow{i}", Input = WorkflowMessage.Create(new { id = i }) };
            await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);
        }

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workflows/recent?count=3"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workflows = await response.Content.ReadFromJsonAsync<WorkflowMonitoringSummary[]>(s_jsonOptions);
        workflows.Should().NotBeNull();
        workflows.Should().HaveCount(3);
    }

    #endregion

    #region Metrics Tests

    [Fact]
    public async Task GetMetrics_ReturnsZeroCounts_WhenNoWorkflowsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/metrics"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var metrics = await response.Content.ReadFromJsonAsync<WorkflowMetricsSnapshot>(s_jsonOptions);
        metrics.Should().NotBeNull();
        metrics!.TotalStarted.Should().Be(0);
        metrics.TotalCompleted.Should().Be(0);
        metrics.TotalFailed.Should().Be(0);
        metrics.TotalCancelled.Should().Be(0);
    }

    [Fact]
    public async Task GetMetrics_ReturnsCorrectCounts_WithWorkflowsAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();
        var grainFactory = this._app!.Services.GetRequiredService<IGrainFactory>();

        // Create a workflow and complete it
        var request1 = new StartWorkflowRequest { WorkflowName = "CompletedWorkflow", Input = WorkflowMessage.Create(new { id = 1 }) };
        var response1 = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request1, s_jsonOptions);
        var run1 = await response1.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        var grain1 = grainFactory.GetGrain<IWorkflowGrain>(run1!.Id);
        await grain1.UpdateStatusAsync(new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Completed }, null, CancellationToken.None);

        // Create a workflow and fail it
        var request2 = new StartWorkflowRequest { WorkflowName = "FailedWorkflow", Input = WorkflowMessage.Create(new { id = 2 }) };
        var response2 = await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request2, s_jsonOptions);
        var run2 = await response2.Content.ReadFromJsonAsync<WorkflowRun>(s_jsonOptions);
        var grain2 = grainFactory.GetGrain<IWorkflowGrain>(run2!.Id);
        await grain2.UpdateStatusAsync(new WorkflowRunStatusUpdate { Status = WorkflowRunStatus.Failed }, null, CancellationToken.None);

        // Create a running workflow
        var request3 = new StartWorkflowRequest { WorkflowName = "RunningWorkflow", Input = WorkflowMessage.Create(new { id = 3 }) };
        await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request3, s_jsonOptions);

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/metrics"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var metrics = await response.Content.ReadFromJsonAsync<WorkflowMetricsSnapshot>(s_jsonOptions);
        metrics.Should().NotBeNull();
        metrics!.TotalStarted.Should().Be(3);
        metrics.TotalCompleted.Should().Be(1);
        metrics.TotalFailed.Should().Be(1);
    }

    [Fact]
    public async Task GetMetrics_RespectsWindowParameterAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        // Create a workflow
        var request = new StartWorkflowRequest { WorkflowName = "TestWorkflow", Input = WorkflowMessage.Create(new { test = true }) };
        await client.PostAsJsonAsync(CreateUri("/v1/workflows"), request, s_jsonOptions);

        // Act - request with 60 minute window (default)
        var response = await client.GetAsync(CreateUri("/v1/monitor/metrics?windowMinutes=60"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var metrics = await response.Content.ReadFromJsonAsync<WorkflowMetricsSnapshot>(s_jsonOptions);
        metrics.Should().NotBeNull();
        metrics!.Window.Should().Be(TimeSpan.FromMinutes(60));
        metrics.TotalStarted.Should().Be(1);
    }

    #endregion

    #region SSE Events Test

    [Fact]
    public async Task StreamEvents_ReturnsConnectedEventAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/events"), HttpCompletionOption.ResponseHeadersRead, cts.Token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        // Read the first event
        var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // Read lines until we find the connected event or timeout
        var lines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) != null && lines.Count < 5)
        {
            lines.Add(line);
            if (line.Contains("Connected to monitoring stream"))
            {
                break;
            }
        }

        lines.Should().Contain(l => l.StartsWith("event: connected", StringComparison.Ordinal));
    }

    #endregion

    #region Multiple Workers Tests

    [Fact]
    public async Task GetWorkers_ReturnsMultipleWorkers_WhenMultipleConfiguredAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync(options =>
        {
            options.Workers.Add(new WorkerConfiguration
            {
                Endpoint = "http://localhost:5002",
                HostId = "test-worker-2"
            });
        });

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/workers"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var workers = await response.Content.ReadFromJsonAsync<WorkerStatus[]>(s_jsonOptions);
        workers.Should().NotBeNull();
        workers.Should().HaveCount(2);

        // First worker should be default
        workers!.Should().Contain(w => w.HostId == "test-worker-1" && w.IsDefault);
        workers.Should().Contain(w => w.HostId == "test-worker-2" && !w.IsDefault);
    }

    [Fact]
    public async Task GetSystemStatus_ReturnsDegradedStatus_WhenNoWorkersAsync()
    {
        // Arrange
        var client = await this.CreateTestServerAsync(options => options.Workers.Clear());

        // Act
        var response = await client.GetAsync(CreateUri("/v1/monitor/status"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<SystemStatus>(s_jsonOptions);
        status.Should().NotBeNull();
        status!.Status.Should().Be("Degraded");
        status.TotalWorkers.Should().Be(0);
        status.ActiveWorkers.Should().Be(0);
    }

    #endregion
}
