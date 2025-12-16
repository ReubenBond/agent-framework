// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI.DevUI;
using Microsoft.Agents.AI.DevUI.Entities;
using Microsoft.Agents.AI.Workflows;

namespace AgentWebChat.AgentHost.Workflows;

/// <summary>
/// Entity provider that exposes workflows registered with the <see cref="WorkflowHostService"/>.
/// </summary>
internal sealed class WorkflowHostEntityProvider : IEntityProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkflowHostService _workflowHost;

    public WorkflowHostEntityProvider(IServiceProvider serviceProvider, WorkflowHostService workflowHost)
    {
        this._serviceProvider = serviceProvider;
        this._workflowHost = workflowHost;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<EntityInfo> GetEntitiesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var workflowInfos = await this._workflowHost.GetAvailableWorkflowsAsync(cancellationToken);

        foreach (var workflowInfo in workflowInfos)
        {
            // Try to get the actual workflow instance to extract more detailed information
            var workflow = this._serviceProvider.GetKeyedService<Workflow>(workflowInfo.Name);
            yield return CreateEntityInfo(workflowInfo, workflow);
        }
    }

    /// <inheritdoc/>
    public async Task<EntityInfo?> GetEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var workflowInfos = await this._workflowHost.GetAvailableWorkflowsAsync(cancellationToken);

        var workflowInfo = workflowInfos.FirstOrDefault(w =>
            string.Equals(w.Name, entityId, StringComparison.OrdinalIgnoreCase));

        if (workflowInfo == null)
        {
            return null;
        }

        var workflow = this._serviceProvider.GetKeyedService<Workflow>(workflowInfo.Name);
        return CreateEntityInfo(workflowInfo, workflow);
    }

    private static EntityInfo CreateEntityInfo(Microsoft.Agents.AI.Runtime.Abstractions.Workflows.WorkflowDefinitionInfo workflowInfo, Workflow? workflow)
    {
        // Extract executor IDs from the workflow edges if available
        var executors = new List<string>();
        string? startExecutorId = null;

        if (workflow != null)
        {
            startExecutorId = workflow.StartExecutorId;

            // Collect all executor IDs from the edge definitions
            var edges = workflow.ReflectEdges();
            var executorIds = new HashSet<string>();

            // Add the start executor
            if (!string.IsNullOrEmpty(startExecutorId))
            {
                executorIds.Add(startExecutorId);
            }

            // Add all source and sink (target) executors from edges
            foreach (var (source, edgeSet) in edges)
            {
                executorIds.Add(source);
                foreach (var edge in edgeSet)
                {
                    // Add all source IDs
                    foreach (var sourceId in edge.Connection.SourceIds)
                    {
                        executorIds.Add(sourceId);
                    }

                    // Add all sink IDs (targets)
                    foreach (var sinkId in edge.Connection.SinkIds)
                    {
                        executorIds.Add(sinkId);
                    }
                }
            }

            executors = executorIds.ToList();
        }

        return new EntityInfo(
            Id: workflowInfo.Name,
            Type: "workflow",
            Name: workflowInfo.DisplayName ?? workflowInfo.Name,
            Description: workflowInfo.Description,
            Framework: "agent_framework",
            Tools: [],
            Metadata: []
        )
        {
            Source = "in_memory",
            Executors = executors,
            StartExecutorId = startExecutorId
        };
    }
}
