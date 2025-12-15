// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using AgentContracts;
using AgentContracts.Workflows;

namespace AgentGateway.UnitTests.Workflows;

/// <summary>
/// Tests for checkpoint data serialization at the Gateway/AgentHost boundary.
/// These tests verify that WorkflowCheckpointData can properly store and retrieve binary checkpoint data.
/// </summary>
/// <remarks>
/// Note: Detailed checkpoint serialization tests (for internal types like Checkpoint, WorkflowInfo, etc.)
/// are located in Microsoft.Agents.AI.Workflows.UnitTests/CheckpointSerializationTests.cs
/// </remarks>
public class CheckpointDataSerializationTests
{
    #region WorkflowCheckpointData Tests

    [Fact]
    public void WorkflowCheckpointData_SerializesToJson_WithCorrectPropertyNames()
    {
        // Arrange
        var checkpointData = new WorkflowCheckpointData
        {
            CheckpointId = "test-checkpoint-123",
            Data = [0x01, 0x02, 0x03, 0x04],
            CreatedAt = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero)
        };

        // Act
        var json = JsonSerializer.Serialize(checkpointData, AgentContractsJsonUtilities.DefaultOptions);

        // Assert - Verify camelCase property names
        json.Should().Contain("\"checkpointId\"");
        json.Should().Contain("\"data\"");
        json.Should().Contain("\"createdAt\"");

        // Should NOT contain PascalCase
        json.Should().NotContain("\"CheckpointId\"");
        json.Should().NotContain("\"Data\"");
        json.Should().NotContain("\"CreatedAt\"");
    }

    [Fact]
    public void WorkflowCheckpointData_RoundTrip_PreservesAllData()
    {
        // Arrange
        var original = new WorkflowCheckpointData
        {
            CheckpointId = "checkpoint-abc123",
            Data = System.Text.Encoding.UTF8.GetBytes("""{"stepNumber":5,"workflow":{}}"""),
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(original, AgentContractsJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<WorkflowCheckpointData>(json, AgentContractsJsonUtilities.DefaultOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.CheckpointId.Should().Be(original.CheckpointId);
        deserialized.Data.Should().BeEquivalentTo(original.Data);
        deserialized.CreatedAt.Should().BeCloseTo(original.CreatedAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void WorkflowCheckpointData_WithLargeData_SerializesCorrectly()
    {
        // Arrange - Create a large checkpoint (simulating complex workflow state)
        var largeData = new byte[100_000];
        new Random(42).NextBytes(largeData);

        var checkpointData = new WorkflowCheckpointData
        {
            CheckpointId = "large-checkpoint",
            Data = largeData,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(checkpointData, AgentContractsJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<WorkflowCheckpointData>(json, AgentContractsJsonUtilities.DefaultOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Data.Should().BeEquivalentTo(largeData);
    }

    #endregion

    #region WorkflowCheckpointResult Tests

    [Fact]
    public void WorkflowCheckpointResult_SerializesToJson_WithCorrectPropertyNames()
    {
        // Arrange
        var result = new WorkflowCheckpointResult
        {
            Checkpoint = new WorkflowCheckpointData
            {
                CheckpointId = "result-checkpoint",
                Data = [0xAB, 0xCD],
                CreatedAt = DateTimeOffset.UtcNow
            },
            ETag = "etag-abc123"
        };

        // Act
        var json = JsonSerializer.Serialize(result, AgentContractsJsonUtilities.DefaultOptions);

        // Assert
        json.Should().Contain("\"checkpoint\"");
        json.Should().Contain("\"eTag\"");
    }

    [Fact]
    public void WorkflowCheckpointResult_RoundTrip_PreservesAllData()
    {
        // Arrange
        var original = new WorkflowCheckpointResult
        {
            Checkpoint = new WorkflowCheckpointData
            {
                CheckpointId = "test-cp-id",
                Data = [1, 2, 3, 4, 5],
                CreatedAt = DateTimeOffset.UtcNow
            },
            ETag = "etag-version-1"
        };

        // Act
        var json = JsonSerializer.Serialize(original, AgentContractsJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<WorkflowCheckpointResult>(json, AgentContractsJsonUtilities.DefaultOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.ETag.Should().Be(original.ETag);
        deserialized.Checkpoint.CheckpointId.Should().Be(original.Checkpoint.CheckpointId);
        deserialized.Checkpoint.Data.Should().BeEquivalentTo(original.Checkpoint.Data);
    }

    #endregion

    #region WorkflowResumeRequest Tests

    [Fact]
    public void WorkflowResumeRequest_WithCheckpointId_SerializesCorrectly()
    {
        // Arrange
        var request = new WorkflowResumeRequest
        {
            RunId = "run-123",
            WorkflowName = "TestWorkflow",
            CallbackBaseUrl = "http://localhost:5000",
            Signal = new WorkflowSignal
            {
                RequestId = "req-1",
                Response = new WorkflowMessage { TypeName = "System.String", Data = JsonDocument.Parse("\"test\"").RootElement }
            },
            CheckpointId = "cp-456"
        };

        // Act
        var json = JsonSerializer.Serialize(request, AgentContractsJsonUtilities.DefaultOptions);

        // Assert
        json.Should().Contain("\"checkpointId\":\"cp-456\"");
        json.Should().Contain("\"runId\":\"run-123\"");
    }

    [Fact]
    public void WorkflowResumeRequest_RoundTrip_PreservesCheckpointId()
    {
        // Arrange
        var original = new WorkflowResumeRequest
        {
            RunId = "run-abc",
            WorkflowName = "MarketingWorkflow",
            CallbackBaseUrl = "https://gateway.example.com",
            Signal = new WorkflowSignal
            {
                RequestId = "signal-req",
                Response = new WorkflowMessage { TypeName = "System.Object", Data = JsonDocument.Parse("""{"approved":true}""").RootElement }
            },
            CheckpointId = "checkpoint-xyz"
        };

        // Act
        var json = JsonSerializer.Serialize(original, AgentContractsJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<WorkflowResumeRequest>(json, AgentContractsJsonUtilities.DefaultOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.CheckpointId.Should().Be("checkpoint-xyz");
        deserialized.RunId.Should().Be("run-abc");
        deserialized.Signal.RequestId.Should().Be("signal-req");
    }

    #endregion
}
