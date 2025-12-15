// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Agents.AI.Workflows.Execution;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Workflows.UnitTests;

/// <summary>
/// Comprehensive tests for checkpoint serialization and deserialization.
/// These tests cover various scenarios to ensure checkpoints can be reliably
/// persisted and restored, which is critical for workflow resumption (HITL).
/// </summary>
public class CheckpointSerializationTests
{
    #region Test Data Helpers

    private static readonly string s_testRunId = Guid.NewGuid().ToString("N");

    private static WorkflowInfo CreateMinimalWorkflowInfo()
    {
        // Create a minimal workflow for testing
        ForwardMessageExecutor<string> executor = new("TestExecutor");
        WorkflowBuilder builder = new(executor);
        return builder.Build().ToWorkflowInfo();
    }

    private static WorkflowInfo CreateComplexWorkflowInfo()
    {
        // Create a more complex workflow with ports and edges
        ForwardMessageExecutor<string> forwardString = new("ForwardString");
        ForwardMessageExecutor<int> forwardInt = new("ForwardInt");

        RequestPort stringToInt = RequestPort.Create<string, int>("StringToInt");
        RequestPort intToString = RequestPort.Create<int, string>("IntToString");

        WorkflowBuilder builder = new(forwardString);
        builder.AddEdge(forwardString, stringToInt)
               .AddEdge(stringToInt, forwardInt)
               .AddEdge(forwardInt, intToString)
               .AddEdge(intToString, StreamingAggregators.Last<int>().BindAsExecutor("Aggregate"));

        return builder.Build().ToWorkflowInfo();
    }

    private static RunnerStateData CreateEmptyRunnerStateData() =>
        new([], [], []);

    private static RunnerStateData CreateRunnerStateDataWithRequests()
    {
        RequestPort port = RequestPort.Create<string, int>("TestPort");
        ExternalRequest request = ExternalRequest.Create(port, "Request1", "TestData");

        return new(
            ["Executor1", "Executor2"],
            [],
            [request]
        );
    }

    private static Dictionary<ScopeKey, PortableValue> CreateEmptyStateData() => [];

    private static Dictionary<ScopeKey, PortableValue> CreateStateDataWithValues()
    {
        ScopeKey key1 = new(new ScopeId("Executor1", null), "Key1");
        ScopeKey key2 = new(new ScopeId("Executor2", "Shared"), "Key2");

        return new()
        {
            [key1] = new("StringValue"),
            [key2] = new(42)
        };
    }

    private static Dictionary<EdgeId, PortableValue> CreateEmptyEdgeStateData() => [];

    #endregion

    #region Checkpoint Serialization Tests

    [Fact]
    public void Checkpoint_WithMinimalData_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        Checkpoint original = new(
            stepNumber: 0,
            workflow: workflowInfo,
            runnerData: CreateEmptyRunnerStateData(),
            stateData: CreateEmptyStateData(),
            edgeStateData: CreateEmptyEdgeStateData());

        // Act
        Checkpoint result = JsonSerializationTests.RunJsonRoundtrip(original);

        // Assert
        result.StepNumber.Should().Be(0);
        result.Workflow.Should().NotBeNull();
        result.RunnerData.Should().NotBeNull();
        result.StateData.Should().NotBeNull();
        result.EdgeStateData.Should().NotBeNull();
        result.Parent.Should().BeNull();
    }

    [Fact]
    public void Checkpoint_WithComplexWorkflowInfo_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateComplexWorkflowInfo();
        Checkpoint original = new(
            stepNumber: 5,
            workflow: workflowInfo,
            runnerData: CreateRunnerStateDataWithRequests(),
            stateData: CreateStateDataWithValues(),
            edgeStateData: CreateEmptyEdgeStateData());

        // Act
        Checkpoint result = JsonSerializationTests.RunJsonRoundtrip(original);

        // Assert
        result.StepNumber.Should().Be(5);
        result.Workflow.Should().NotBeNull();
        result.Workflow.Executors.Should().NotBeEmpty();
        result.Workflow.Edges.Should().NotBeEmpty();
        result.Workflow.RequestPorts.Should().NotBeEmpty();
    }

    [Fact]
    public void Checkpoint_WithParentCheckpoint_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        CheckpointInfo parentInfo = new(s_testRunId, Guid.NewGuid().ToString("N"));

        Checkpoint original = new(
            stepNumber: 10,
            workflow: workflowInfo,
            runnerData: CreateEmptyRunnerStateData(),
            stateData: CreateEmptyStateData(),
            edgeStateData: CreateEmptyEdgeStateData(),
            parent: parentInfo);

        // Act
        Checkpoint result = JsonSerializationTests.RunJsonRoundtrip(original);

        // Assert
        result.Parent.Should().NotBeNull();
        result.Parent!.RunId.Should().Be(parentInfo.RunId);
        result.Parent.CheckpointId.Should().Be(parentInfo.CheckpointId);
    }

    [Fact]
    public void Checkpoint_InitialCheckpoint_SerializesAndDeserializesCorrectly()
    {
        // Arrange - Initial checkpoint has stepNumber = -1
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        Checkpoint original = new(
            stepNumber: -1,
            workflow: workflowInfo,
            runnerData: CreateEmptyRunnerStateData(),
            stateData: CreateEmptyStateData(),
            edgeStateData: CreateEmptyEdgeStateData());

        // Act
        Checkpoint result = JsonSerializationTests.RunJsonRoundtrip(original);

        // Assert
        result.StepNumber.Should().Be(-1);
        result.IsInitial.Should().BeTrue();
    }

    [Fact]
    public void Checkpoint_WithChatMessages_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        ScopeKey messageKey = new(new ScopeId("ChatExecutor", null), "Messages");

        ChatMessage userMessage = new(ChatRole.User, "Hello, world!");
        ChatMessage assistantMessage = new(ChatRole.Assistant, "Hi there!");

        Dictionary<ScopeKey, PortableValue> stateData = new()
        {
            [messageKey] = new(new List<ChatMessage> { userMessage, assistantMessage })
        };

        Checkpoint original = new(
            stepNumber: 3,
            workflow: workflowInfo,
            runnerData: CreateEmptyRunnerStateData(),
            stateData: stateData,
            edgeStateData: CreateEmptyEdgeStateData());

        // Act
        Checkpoint result = JsonSerializationTests.RunJsonRoundtrip(original);

        // Assert
        result.StateData.Should().ContainKey(messageKey);
        var messages = result.StateData[messageKey].As<List<ChatMessage>>();
        messages.Should().HaveCount(2);
    }

    #endregion

    #region WorkflowInfo Serialization Tests

    [Fact]
    public void WorkflowInfo_WithExecutors_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        WorkflowInfo original = CreateComplexWorkflowInfo();

        // Act
        WorkflowInfo result = JsonSerializationTests.RunJsonRoundtrip(original);

        // Assert
        result.Executors.Should().HaveCount(original.Executors.Count);
        foreach (var executorId in original.Executors.Keys)
        {
            result.Executors.Should().ContainKey(executorId);
        }
    }

    [Fact]
    public void WorkflowInfo_WithEdges_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        WorkflowInfo original = CreateComplexWorkflowInfo();

        // Act
        WorkflowInfo result = JsonSerializationTests.RunJsonRoundtrip(original);

        // Assert
        result.Edges.Should().HaveCount(original.Edges.Count);
        foreach (var sourceId in original.Edges.Keys)
        {
            result.Edges.Should().ContainKey(sourceId);
            result.Edges[sourceId].Should().HaveCount(original.Edges[sourceId].Count);
        }
    }

    [Fact]
    public void WorkflowInfo_WithRequestPorts_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        WorkflowInfo original = CreateComplexWorkflowInfo();

        // Act
        WorkflowInfo result = JsonSerializationTests.RunJsonRoundtrip(original);

        // Assert
        result.RequestPorts.Should().HaveCount(original.RequestPorts.Count);
    }

    [Fact]
    public void WorkflowInfo_StartExecutorId_PreservedAfterSerialization()
    {
        // Arrange
        WorkflowInfo original = CreateComplexWorkflowInfo();

        // Act
        WorkflowInfo result = JsonSerializationTests.RunJsonRoundtrip(original);

        // Assert
        result.StartExecutorId.Should().Be(original.StartExecutorId);
    }

    [Fact]
    public void WorkflowInfo_OutputExecutorIds_PreservedAfterSerialization()
    {
        // Arrange
        WorkflowInfo original = CreateComplexWorkflowInfo();

        // Act
        WorkflowInfo result = JsonSerializationTests.RunJsonRoundtrip(original);

        // Assert
        result.OutputExecutorIds.Should().HaveCount(original.OutputExecutorIds.Count);
        result.OutputExecutorIds.Should().BeEquivalentTo(original.OutputExecutorIds);
    }

    #endregion

    #region CheckpointManager Tests

    [Fact]
    public async Task CheckpointManager_CommitAndLookup_WorksCorrectlyAsync()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        Checkpoint checkpoint = new(
            stepNumber: 1,
            workflow: workflowInfo,
            runnerData: CreateEmptyRunnerStateData(),
            stateData: CreateEmptyStateData(),
            edgeStateData: CreateEmptyEdgeStateData());

        CheckpointManager manager = CheckpointManager.CreateJson(new InMemoryJsonStore());
        string runId = Guid.NewGuid().ToString("N");

        // Act
        CheckpointInfo info = await manager.CommitCheckpointAsync(runId, checkpoint);
        Checkpoint retrieved = await manager.LookupCheckpointAsync(runId, info);

        // Assert
        retrieved.StepNumber.Should().Be(checkpoint.StepNumber);
        retrieved.Workflow.Should().NotBeNull();
        retrieved.Workflow.StartExecutorId.Should().Be(checkpoint.Workflow.StartExecutorId);
    }

    [Fact]
    public async Task CheckpointManager_MultipleCheckpoints_AllRetrievableAsync()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        CheckpointManager manager = CheckpointManager.CreateJson(new InMemoryJsonStore());
        string runId = Guid.NewGuid().ToString("N");

        List<CheckpointInfo> checkpointInfos = [];

        // Act - Create multiple checkpoints
        for (int i = 0; i < 5; i++)
        {
            Checkpoint checkpoint = new(
                stepNumber: i,
                workflow: workflowInfo,
                runnerData: CreateEmptyRunnerStateData(),
                stateData: CreateEmptyStateData(),
                edgeStateData: CreateEmptyEdgeStateData());

            CheckpointInfo info = await manager.CommitCheckpointAsync(runId, checkpoint);
            checkpointInfos.Add(info);
        }

        // Assert - All checkpoints should be retrievable
        for (int i = 0; i < checkpointInfos.Count; i++)
        {
            Checkpoint retrieved = await manager.LookupCheckpointAsync(runId, checkpointInfos[i]);
            retrieved.StepNumber.Should().Be(i);
        }
    }

    [Fact]
    public async Task CheckpointManager_WithExternalRequests_PreservesRequestDataAsync()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        RunnerStateData runnerData = CreateRunnerStateDataWithRequests();

        Checkpoint checkpoint = new(
            stepNumber: 2,
            workflow: workflowInfo,
            runnerData: runnerData,
            stateData: CreateEmptyStateData(),
            edgeStateData: CreateEmptyEdgeStateData());

        CheckpointManager manager = CheckpointManager.CreateJson(new InMemoryJsonStore());
        string runId = Guid.NewGuid().ToString("N");

        // Act
        CheckpointInfo info = await manager.CommitCheckpointAsync(runId, checkpoint);
        Checkpoint retrieved = await manager.LookupCheckpointAsync(runId, info);

        // Assert
        retrieved.RunnerData.OutstandingRequests.Should().HaveCount(1);
        retrieved.RunnerData.OutstandingRequests[0].RequestId.Should().Be("Request1");
    }

    #endregion

    #region JSON Property Naming Tests (camelCase compliance)

    [Fact]
    public void Checkpoint_JsonPropertyNames_AreCamelCase()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        Checkpoint checkpoint = new(
            stepNumber: 1,
            workflow: workflowInfo,
            runnerData: CreateEmptyRunnerStateData(),
            stateData: CreateEmptyStateData(),
            edgeStateData: CreateEmptyEdgeStateData());

        JsonMarshaller marshaller = new();

        // Act
        JsonElement json = marshaller.Marshal(checkpoint);
        string jsonString = json.GetRawText();

        // Assert - Property names should be camelCase
        jsonString.Should().Contain("\"stepNumber\"");
        jsonString.Should().Contain("\"workflow\"");
        jsonString.Should().Contain("\"runnerData\"");
        jsonString.Should().Contain("\"stateData\"");
        jsonString.Should().Contain("\"edgeStateData\"");

        // Should NOT contain PascalCase property names
        jsonString.Should().NotContain("\"StepNumber\"");
        jsonString.Should().NotContain("\"Workflow\"");
        jsonString.Should().NotContain("\"RunnerData\"");
    }

    [Fact]
    public void WorkflowInfo_JsonPropertyNames_AreCamelCase()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateComplexWorkflowInfo();
        JsonMarshaller marshaller = new();

        // Act
        JsonElement json = marshaller.Marshal(workflowInfo);
        string jsonString = json.GetRawText();

        // Assert
        jsonString.Should().Contain("\"executors\"");
        jsonString.Should().Contain("\"edges\"");
        jsonString.Should().Contain("\"requestPorts\"");
        jsonString.Should().Contain("\"startExecutorId\"");
        jsonString.Should().Contain("\"outputExecutorIds\"");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Checkpoint_WithNullParent_SerializesCorrectly()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        Checkpoint checkpoint = new(
            stepNumber: 0,
            workflow: workflowInfo,
            runnerData: CreateEmptyRunnerStateData(),
            stateData: CreateEmptyStateData(),
            edgeStateData: CreateEmptyEdgeStateData(),
            parent: null);

        // Act
        Checkpoint result = JsonSerializationTests.RunJsonRoundtrip(checkpoint);

        // Assert
        result.Parent.Should().BeNull();
    }

    [Fact]
    public void Checkpoint_WithEmptyCollections_SerializesCorrectly()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        Checkpoint checkpoint = new(
            stepNumber: 0,
            workflow: workflowInfo,
            runnerData: new([], [], []),
            stateData: [],
            edgeStateData: []);

        // Act
        Checkpoint result = JsonSerializationTests.RunJsonRoundtrip(checkpoint);

        // Assert
        result.RunnerData.InstantiatedExecutors.Should().BeEmpty();
        result.RunnerData.QueuedMessages.Should().BeEmpty();
        result.RunnerData.OutstandingRequests.Should().BeEmpty();
        result.StateData.Should().BeEmpty();
        result.EdgeStateData.Should().BeEmpty();
    }

    [Fact]
    public void Checkpoint_WithLargeStepNumber_SerializesCorrectly()
    {
        // Arrange
        WorkflowInfo workflowInfo = CreateMinimalWorkflowInfo();
        Checkpoint checkpoint = new(
            stepNumber: int.MaxValue,
            workflow: workflowInfo,
            runnerData: CreateEmptyRunnerStateData(),
            stateData: CreateEmptyStateData(),
            edgeStateData: CreateEmptyEdgeStateData());

        // Act
        Checkpoint result = JsonSerializationTests.RunJsonRoundtrip(checkpoint);

        // Assert
        result.StepNumber.Should().Be(int.MaxValue);
    }

    [Fact]
    public async Task CheckpointManager_LookupNonExistentCheckpoint_ThrowsAsync()
    {
        // Arrange
        CheckpointManager manager = CheckpointManager.CreateJson(new InMemoryJsonStore());
        string runId = Guid.NewGuid().ToString("N");
        CheckpointInfo nonExistentInfo = new(runId, "non-existent-checkpoint-id");

        // Act & Assert
        Func<Task> act = async () => await manager.LookupCheckpointAsync(runId, nonExistentInfo);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    #endregion

    #region Cross-Serializer Compatibility Tests

    [Fact]
    public void Checkpoint_DeserializedFromRawJson_WorksCorrectly()
    {
        // This test simulates deserializing checkpoint data that might have been
        // stored by a different version of the code or with different serializer settings

        // Arrange - Create JSON that matches expected camelCase format
        string jsonString = """
        {
            "stepNumber": 5,
            "workflow": {
                "executors": {},
                "edges": {},
                "requestPorts": [],
                "startExecutorId": "TestExecutor",
                "outputExecutorIds": []
            },
            "runnerData": {
                "instantiatedExecutors": [],
                "queuedMessages": {},
                "outstandingRequests": []
            },
            "stateData": {},
            "edgeStateData": {},
            "parent": null
        }
        """;

        JsonMarshaller marshaller = new();

        // Act
        using JsonDocument doc = JsonDocument.Parse(jsonString);
        Checkpoint result = marshaller.Marshal<Checkpoint>(doc.RootElement);

        // Assert
        result.StepNumber.Should().Be(5);
        result.Workflow.Should().NotBeNull();
        result.Workflow.StartExecutorId.Should().Be("TestExecutor");
    }

    #endregion
}
