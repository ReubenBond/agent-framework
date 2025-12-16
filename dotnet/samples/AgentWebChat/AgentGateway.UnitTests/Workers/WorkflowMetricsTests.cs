// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.Metrics;
using Microsoft.Agents.AI.Runtime.Abstractions.Telemetry;

namespace AgentGateway.UnitTests.Workers;

/// <summary>
/// Unit tests for <see cref="WorkflowMetrics"/> instrumentation.
/// </summary>
public sealed class WorkflowMetricsTests : IDisposable
{
    private readonly WorkflowMetrics _metrics;
    private readonly MeterListener _listener;
    private readonly List<(string name, double value, KeyValuePair<string, object?>[] tags)> _recordedHistograms = [];
    private readonly List<(string name, long value, KeyValuePair<string, object?>[] tags)> _recordedCounters = [];

    public WorkflowMetricsTests()
    {
        _metrics = new WorkflowMetrics();
        _listener = new MeterListener();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name.StartsWith("Microsoft.Agents.AI.Runtime", StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            _recordedHistograms.Add((instrument.Name, value, tags.ToArray())));

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            _recordedCounters.Add((instrument.Name, value, tags.ToArray())));

        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _metrics.Dispose();
    }

    [Fact]
    public void RecordWorkerDispatch_RecordsDispatchesTotal()
    {
        // Act
        _metrics.RecordWorkerDispatch("worker-1", "test-workflow", success: true, latencyMs: 50.0);

        // Assert
        var dispatchCount = _recordedCounters.FirstOrDefault(c => c.name == "worker.dispatches.total");
        dispatchCount.Should().NotBe(default);
        dispatchCount.value.Should().Be(1);
    }

    [Fact]
    public void RecordWorkerDispatch_RecordsDispatchLatency()
    {
        // Act
        _metrics.RecordWorkerDispatch("worker-1", "test-workflow", success: true, latencyMs: 150.5);

        // Assert
        var latency = _recordedHistograms.FirstOrDefault(h => h.name == "worker.dispatch.latency");
        latency.Should().NotBe(default);
        latency.value.Should().Be(150.5);
    }

    [Fact]
    public void RecordWorkerDispatch_RecordsFailure_WhenNotSuccessful()
    {
        // Act
        _metrics.RecordWorkerDispatch("worker-1", "test-workflow", success: false, latencyMs: 100.0);

        // Assert
        var failureCount = _recordedCounters.FirstOrDefault(c => c.name == "worker.dispatches.failed");
        failureCount.Should().NotBe(default);
        failureCount.value.Should().Be(1);
    }

    [Fact]
    public void RecordWorkflowStarted_IncrementsRunsTotalAndActive()
    {
        // Act
        _metrics.RecordWorkflowStarted("test-workflow");

        // Assert
        var runsTotal = _recordedCounters.FirstOrDefault(c => c.name == "workflow.runs.total");
        runsTotal.Should().NotBe(default);
        runsTotal.value.Should().Be(1);

        var activeRuns = _recordedCounters.FirstOrDefault(c => c.name == "workflow.runs.active");
        activeRuns.Should().NotBe(default);
        activeRuns.value.Should().Be(1);
    }

    [Fact]
    public void RecordWorkflowCompleted_RecordsCompletionAndDuration()
    {
        // Arrange
        _metrics.RecordWorkflowStarted("test-workflow");
        _recordedCounters.Clear();
        _recordedHistograms.Clear();

        // Act
        _metrics.RecordWorkflowCompleted("test-workflow", durationMs: 5000.0);

        // Assert
        var completedCount = _recordedCounters.FirstOrDefault(c => c.name == "workflow.runs.completed");
        completedCount.Should().NotBe(default);
        completedCount.value.Should().Be(1);

        var activeRuns = _recordedCounters.FirstOrDefault(c => c.name == "workflow.runs.active");
        activeRuns.Should().NotBe(default);
        activeRuns.value.Should().Be(-1); // Decremented

        var duration = _recordedHistograms.FirstOrDefault(h => h.name == "workflow.run.duration");
        duration.Should().NotBe(default);
        duration.value.Should().Be(5000.0);
    }

    [Fact]
    public void RecordWorkflowFailed_RecordsFailureAndDuration()
    {
        // Arrange
        _metrics.RecordWorkflowStarted("test-workflow");
        _recordedCounters.Clear();
        _recordedHistograms.Clear();

        // Act
        _metrics.RecordWorkflowFailed("test-workflow", errorCode: "TEST_ERROR", durationMs: 1000.0);

        // Assert
        var failedCount = _recordedCounters.FirstOrDefault(c => c.name == "workflow.runs.failed");
        failedCount.Should().NotBe(default);
        failedCount.value.Should().Be(1);

        var activeRuns = _recordedCounters.FirstOrDefault(c => c.name == "workflow.runs.active");
        activeRuns.Should().NotBe(default);
        activeRuns.value.Should().Be(-1); // Decremented
    }

    [Fact]
    public void RecordStepExecution_RecordsStepMetrics()
    {
        // Act
        _metrics.RecordStepExecution("test-workflow", stepName: "generate-content", durationMs: 2500.0);

        // Assert
        var stepsTotal = _recordedCounters.FirstOrDefault(c => c.name == "workflow.steps.total");
        stepsTotal.Should().NotBe(default);
        stepsTotal.value.Should().Be(1);

        var stepDuration = _recordedHistograms.FirstOrDefault(h => h.name == "workflow.step.duration");
        stepDuration.Should().NotBe(default);
        stepDuration.value.Should().Be(2500.0);
    }

    [Fact]
    public void RecordWorkflowWaitingForSignal_IncrementsWaitingCounter()
    {
        // Act
        _metrics.RecordWorkflowWaitingForSignal("test-workflow");

        // Assert
        var waitingCount = _recordedCounters.FirstOrDefault(c => c.name == "workflow.runs.waiting_for_signal");
        waitingCount.Should().NotBe(default);
        waitingCount.value.Should().Be(1);
    }

    [Fact]
    public void RecordWorkflowResumedFromSignal_RecordsResumeAndWaitTime()
    {
        // Arrange
        _metrics.RecordWorkflowWaitingForSignal("test-workflow");
        _recordedCounters.Clear();
        _recordedHistograms.Clear();

        // Act
        _metrics.RecordWorkflowResumedFromSignal("test-workflow", waitTimeMs: 30000.0);

        // Assert
        var waitingCount = _recordedCounters.FirstOrDefault(c => c.name == "workflow.runs.waiting_for_signal");
        waitingCount.Should().NotBe(default);
        waitingCount.value.Should().Be(-1); // Decremented

        var waitTime = _recordedHistograms.FirstOrDefault(h => h.name == "workflow.signal.wait_time");
        waitTime.Should().NotBe(default);
        waitTime.value.Should().Be(30000.0);
    }

    [Fact]
    public void RecordWorkerRegistration_IncrementsRegistrationCounters()
    {
        // Act
        _metrics.RecordWorkerRegistration("worker-1");

        // Assert
        var registrations = _recordedCounters.FirstOrDefault(c => c.name == "worker.registrations.total");
        registrations.Should().NotBe(default);
        registrations.value.Should().Be(1);

        var registered = _recordedCounters.FirstOrDefault(c => c.name == "worker.registered");
        registered.Should().NotBe(default);
        registered.value.Should().Be(1);
    }

    [Fact]
    public void RecordWorkerDeregistration_IncrementsDeregistrationCounters()
    {
        // Arrange
        _metrics.RecordWorkerRegistration("worker-1");
        _recordedCounters.Clear();

        // Act
        _metrics.RecordWorkerDeregistration("worker-1");

        // Assert
        var deregistrations = _recordedCounters.FirstOrDefault(c => c.name == "worker.deregistrations.total");
        deregistrations.Should().NotBe(default);
        deregistrations.value.Should().Be(1);

        var registered = _recordedCounters.FirstOrDefault(c => c.name == "worker.registered");
        registered.Should().NotBe(default);
        registered.value.Should().Be(-1); // Decremented
    }

    [Fact]
    public void RecordWorkerHealthCheck_RecordsHealthCheckMetrics()
    {
        // Act
        _metrics.RecordWorkerHealthCheck("worker-1", healthy: true, latencyMs: 25.0);

        // Assert
        var healthChecks = _recordedCounters.FirstOrDefault(c => c.name == "worker.health_checks.total");
        healthChecks.Should().NotBe(default);
        healthChecks.value.Should().Be(1);

        var latency = _recordedHistograms.FirstOrDefault(h => h.name == "worker.health_check.latency");
        latency.Should().NotBe(default);
        latency.value.Should().Be(25.0);
    }

    [Fact]
    public void RecordWorkerHealthCheck_RecordsFailure_WhenUnhealthy()
    {
        // Act
        _metrics.RecordWorkerHealthCheck("worker-1", healthy: false, latencyMs: 5000.0);

        // Assert
        var failedChecks = _recordedCounters.FirstOrDefault(c => c.name == "worker.health_checks.failed");
        failedChecks.Should().NotBe(default);
        failedChecks.value.Should().Be(1);
    }
}
