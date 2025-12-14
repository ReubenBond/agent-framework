// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AgentContracts.Workflows;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace AgentWebChat.AgentHost.Workflows;

/// <summary>
/// A checkpoint store that persists checkpoints to the Gateway workflow grain via HTTP APIs.
/// This enables workflow checkpoints to survive AgentHost restarts, supporting durable HITL scenarios.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by factory method")]
internal sealed class GatewayCheckpointStore : JsonCheckpointStore
{
    private readonly IWorkflowStateService _stateClient;
    private readonly string _runId;
    private readonly HashSet<CheckpointInfo> _checkpointIndex = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="GatewayCheckpointStore"/> class.
    /// </summary>
    /// <param name="stateClient">The state client for communicating with the Gateway.</param>
    /// <param name="runId">The workflow run ID this store is scoped to.</param>
    public GatewayCheckpointStore(IWorkflowStateService stateClient, string runId)
    {
        ArgumentNullException.ThrowIfNull(stateClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        this._stateClient = stateClient;
        this._runId = runId;
    }

    /// <inheritdoc/>
    public override async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string runId,
        JsonElement value,
        CheckpointInfo? parent = null)
    {
        // Generate a unique checkpoint ID
        var checkpointId = Guid.NewGuid().ToString("N");
        var checkpointInfo = new CheckpointInfo(runId, checkpointId);

        // Serialize the JsonElement to bytes for storage
        var data = JsonSerializer.SerializeToUtf8Bytes(value);

        // Store in the Gateway
        var checkpointData = new WorkflowCheckpointData
        {
            CheckpointId = checkpointId,
            Data = data,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await this._stateClient.SaveCheckpointAsync(
            this._runId,
            checkpointData,
            etag: null,
            CancellationToken.None).ConfigureAwait(false);

        // Track in local index
        this._checkpointIndex.Add(checkpointInfo);

        return checkpointInfo;
    }

    /// <inheritdoc/>
    public override async ValueTask<JsonElement> RetrieveCheckpointAsync(string runId, CheckpointInfo key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // Retrieve from the Gateway
        var checkpointData = await this._stateClient.GetCheckpointByIdAsync(
            this._runId,
            key.CheckpointId,
            CancellationToken.None).ConfigureAwait(false);

        if (checkpointData is null)
        {
            throw new KeyNotFoundException($"Checkpoint '{key.CheckpointId}' not found for workflow '{this._runId}'.");
        }

        // Deserialize the stored bytes back to JsonElement
        using var document = JsonDocument.Parse(checkpointData.Data);
        return document.RootElement.Clone();
    }

    /// <inheritdoc/>
    public override async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string runId,
        CheckpointInfo? withParent = null)
    {
        // Get the list of checkpoint IDs from the Gateway
        var checkpointIds = await this._stateClient.ListCheckpointIdsAsync(
            this._runId,
            CancellationToken.None).ConfigureAwait(false);

        // Convert to CheckpointInfo objects
        var checkpoints = checkpointIds.Select(id => new CheckpointInfo(runId, id)).ToList();

        // Update local index
        this._checkpointIndex.Clear();
        foreach (var checkpoint in checkpoints)
        {
            this._checkpointIndex.Add(checkpoint);
        }

        return checkpoints;
    }

    /// <summary>
    /// Creates a <see cref="CheckpointManager"/> that uses this Gateway-backed store.
    /// </summary>
    /// <param name="stateClient">The state client for communicating with the Gateway.</param>
    /// <param name="runId">The workflow run ID.</param>
    /// <param name="customOptions">Optional custom JSON serializer options.</param>
    /// <returns>A CheckpointManager configured to persist checkpoints to the Gateway.</returns>
    public static CheckpointManager CreateCheckpointManager(
        IWorkflowStateService stateClient,
        string runId,
        JsonSerializerOptions? customOptions = null)
    {
        var store = new GatewayCheckpointStore(stateClient, runId);
        return CheckpointManager.CreateJson(store, customOptions);
    }
}
