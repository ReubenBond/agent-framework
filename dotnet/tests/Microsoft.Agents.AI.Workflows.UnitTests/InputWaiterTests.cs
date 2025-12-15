// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.AI.Workflows.Execution;

namespace Microsoft.Agents.AI.Workflows.UnitTests;

public class InputWaiterTests
{
    [Fact]
    public async Task WaitForInputAsync_WithSignalBeforeWait_ReturnsImmediately()
    {
        // Arrange
        using var waiter = new InputWaiter();

        // Act - signal before waiting
        waiter.SignalInput();
        var waitTask = waiter.WaitForInputAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        // Assert - should return immediately with true
        var result = await waitTask;
        result.Should().BeTrue("signal was raised before wait started");
    }

    [Fact]
    public async Task WaitForInputAsync_WithSignalDuringWait_ReturnsTrue()
    {
        // Arrange
        using var waiter = new InputWaiter();
        var waitTask = waiter.WaitForInputAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        // Act - signal after wait started
        await Task.Delay(50); // Give the wait a moment to start
        waiter.SignalInput();

        // Assert
        var result = await waitTask;
        result.Should().BeTrue("signal was raised during wait");
    }

    [Fact]
    public async Task WaitForInputAsync_WithTimeout_ReturnsFalse()
    {
        // Arrange
        using var waiter = new InputWaiter();

        // Act - wait with short timeout, no signal
        var result = await waiter.WaitForInputAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        // Assert
        result.Should().BeFalse("no signal was raised before timeout");
    }

    [Fact]
    public async Task WaitForInputAsync_WithZeroTimeout_ReturnsFalseWhenNotSignaled()
    {
        // Arrange
        using var waiter = new InputWaiter();

        // Act - check without blocking
        var result = await waiter.WaitForInputAsync(TimeSpan.Zero, CancellationToken.None);

        // Assert
        result.Should().BeFalse("no signal was raised");
    }

    [Fact]
    public async Task WaitForInputAsync_WithZeroTimeout_ReturnsTrueWhenSignaled()
    {
        // Arrange
        using var waiter = new InputWaiter();
        waiter.SignalInput();

        // Act - check without blocking
        var result = await waiter.WaitForInputAsync(TimeSpan.Zero, CancellationToken.None);

        // Assert
        result.Should().BeTrue("signal was raised before check");
    }

    [Fact]
    public async Task WaitForInputAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        using var waiter = new InputWaiter();
        using var cts = new CancellationTokenSource();

        // Act
        var waitTask = waiter.WaitForInputAsync(TimeSpan.FromSeconds(10), cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => waitTask);
    }

    [Fact]
    public async Task WaitForInputAsync_WithNullTimeout_WaitsIndefinitelyUntilSignal()
    {
        // Arrange
        using var waiter = new InputWaiter();
        var waitTask = waiter.WaitForInputAsync(timeout: null, CancellationToken.None);

        // Act - signal after a delay
        await Task.Delay(100);
        waiter.SignalInput();

        // Assert - should complete after signal
        var result = await waitTask;
        result.Should().BeTrue();
    }

    [Fact]
    public async Task WaitForInputAsync_NoTimeout_WaitsIndefinitelyUntilSignal()
    {
        // Arrange
        using var waiter = new InputWaiter();
        var waitTask = waiter.WaitForInputAsync(CancellationToken.None);

        // Act - signal after a delay
        await Task.Delay(100);
        waiter.SignalInput();

        // Assert - should complete after signal
        await waitTask;
    }

    [Fact]
    public void SignalInput_MultipleSignalsBeforeWait_AreCoalesced()
    {
        // Arrange
        using var waiter = new InputWaiter();

        // Act - multiple signals (should not throw)
        waiter.SignalInput();
        waiter.SignalInput();
        waiter.SignalInput();

        // Assert - no exception thrown, signals are coalesced
    }

    [Fact]
    public async Task SignalInput_MultipleSignals_OnlyFirstWaitSucceeds()
    {
        // Arrange
        using var waiter = new InputWaiter();

        // Act - signal once
        waiter.SignalInput();

        // First wait should succeed
        var result1 = await waiter.WaitForInputAsync(TimeSpan.Zero, CancellationToken.None);
        // Second wait should fail (no more signals)
        var result2 = await waiter.WaitForInputAsync(TimeSpan.Zero, CancellationToken.None);

        // Assert
        result1.Should().BeTrue("first wait consumes the signal");
        result2.Should().BeFalse("signal was already consumed");
    }

    [Fact]
    public async Task SignalInput_AfterDispose_DoesNotThrow()
    {
        // Arrange
        var waiter = new InputWaiter();
        waiter.Dispose();

        // Act & Assert - should not throw
        waiter.SignalInput();
    }

    [Fact]
    public void Dispose_MultipleTimes_DoesNotThrow()
    {
        // Arrange
        var waiter = new InputWaiter();

        // Act & Assert - should not throw
        waiter.Dispose();
        waiter.Dispose();
        waiter.Dispose();
    }

    [Fact]
    public async Task SignalInput_ConcurrentWithDispose_DoesNotThrow()
    {
        // Arrange
        var waiter = new InputWaiter();
        var tasks = new Task[100];

        // Act - race signals and dispose
        for (int i = 0; i < 99; i++)
        {
            tasks[i] = Task.Run(() => waiter.SignalInput());
        }
        tasks[99] = Task.Run(() => waiter.Dispose());

        // Assert - should not throw
        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task WaitForInputAsync_SequentialWaits_EachRequiresSignal()
    {
        // Arrange
        using var waiter = new InputWaiter();

        // First cycle
        waiter.SignalInput();
        var result1 = await waiter.WaitForInputAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Second cycle - need another signal
        var result2 = await waiter.WaitForInputAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Third cycle - signal again
        waiter.SignalInput();
        var result3 = await waiter.WaitForInputAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Assert
        result1.Should().BeTrue("first signal was consumed");
        result2.Should().BeFalse("no signal for second wait");
        result3.Should().BeTrue("third signal was consumed");
    }

    [Fact]
    public async Task WaitForInputAsync_PollingPattern_WorksCorrectly()
    {
        // This test simulates the actual usage pattern in StreamingRunEventStream
        // where the run loop polls with a timeout and continues if signaled

        // Arrange
        using var waiter = new InputWaiter();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int pollCount = 0;
        int signalReceived = 0;

        // Simulate the run loop polling
        var runLoopTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested && pollCount < 10)
            {
                pollCount++;
                bool gotSignal = await waiter.WaitForInputAsync(TimeSpan.FromMilliseconds(50), cts.Token);
                if (gotSignal)
                {
                    signalReceived++;
                }
            }
        });

        // Signal after a short delay
        await Task.Delay(150);
        waiter.SignalInput();

        // Wait for run loop to complete
        await runLoopTask;

        // Assert
        pollCount.Should().BeGreaterThan(1, "should have polled multiple times");
        signalReceived.Should().Be(1, "should have received exactly one signal");
    }
}
