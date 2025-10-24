// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;

using Microsoft.Agents.AI.Hosting;

namespace Microsoft.Agents.AI.DevUI.Entities;

/// <summary>
/// Entity provider that discovers entities from registered <see cref="AgentCatalog"/> and <see cref="WorkflowCatalog"/> services.
/// </summary>
public sealed class HostingEntityProvider : IEntityProvider
{
    private readonly AgentCatalog? _agentCatalog;
    private readonly WorkflowCatalog? _workflowCatalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostingEntityProvider"/> class.
    /// </summary>
    /// <param name="agentCatalog">The agent catalog to discover agents from.</param>
    /// <param name="workflowCatalog">The workflow catalog to discover workflows from.</param>
    public HostingEntityProvider(AgentCatalog? agentCatalog = null, WorkflowCatalog? workflowCatalog = null)
    {
        this._agentCatalog = agentCatalog;
        this._workflowCatalog = workflowCatalog;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<EntityInfo> GetEntitiesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Discover agents from the agent catalog
        if (this._agentCatalog is not null)
        {
            await foreach (var agent in this._agentCatalog.GetAgentsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (agent.GetType().Name == "WorkflowHostAgent")
                {
                    // HACK: ignore WorkflowHostAgent instances as they are just wrappers around workflows,
                    // and workflows are handled below.
                    continue;
                }

                yield return new EntityInfo(
                    Id: agent.Name ?? agent.Id,
                    Type: "agent",
                    Name: agent.Name ?? agent.Id,
                    Description: agent.Description,
                    Framework: "agent-framework",
                    Tools: null,
                    Metadata: []
                )
                {
                    Source = "in_memory"
                };
            }
        }

        // Discover workflows from the workflow catalog
        if (this._workflowCatalog is not null)
        {
            await foreach (var workflow in this._workflowCatalog.GetWorkflowsAsync(cancellationToken).ConfigureAwait(false))
            {
                // Extract executor IDs from the workflow structure
                var executorIds = new HashSet<string> { workflow.StartExecutorId };
                var reflectedEdges = workflow.ReflectEdges();
                foreach (var (sourceId, edgeSet) in reflectedEdges)
                {
                    executorIds.Add(sourceId);
                    foreach (var edge in edgeSet)
                    {
                        foreach (var sinkId in edge.Connection.SinkIds)
                        {
                            executorIds.Add(sinkId);
                        }
                    }
                }

                // Convert executor IDs to JsonElements for the Tools property
                var tools = executorIds.Select(id => JsonSerializer.SerializeToElement(id)).ToList();

                // Create a default input schema (string type)
                var defaultInputSchema = new Dictionary<string, object>
                {
                    ["type"] = "string"
                };

                yield return new EntityInfo(
                    Id: workflow.Name ?? workflow.StartExecutorId,
                    Type: "workflow",
                    Name: workflow.Name ?? workflow.StartExecutorId,
                    Description: workflow.Description,
                    Framework: "agent-framework",
                    Tools: tools,
                    Metadata: []
                )
                {
                    Source = "in_memory",
                    WorkflowDump = JsonSerializer.SerializeToElement(workflow.ToDevUIDict()),
                    InputSchema = JsonSerializer.SerializeToElement(defaultInputSchema),
                    InputTypeName = "string",
                    StartExecutorId = workflow.StartExecutorId
                };
            }
        }
    }

    /// <inheritdoc/>
    public async Task<EntityInfo?> GetEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        // Try to find the entity among discovered agents
        if (this._agentCatalog is not null)
        {
            await foreach (var agent in this._agentCatalog.GetAgentsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (agent.GetType().Name == "WorkflowHostAgent")
                {
                    // HACK: ignore WorkflowHostAgent instances as they are just wrappers around workflows,
                    // and workflows are handled below.
                    continue;
                }

                if (string.Equals(agent.Name, entityId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(agent.Id, entityId, StringComparison.OrdinalIgnoreCase))
                {
                    return new EntityInfo(
                        Id: agent.Name ?? agent.Id,
                        Type: "agent",
                        Name: agent.Name ?? agent.Id,
                        Description: agent.Description,
                        Framework: "agent-framework",
                        Tools: null,
                        Metadata: []
                    )
                    {
                        Source = "in_memory"
                    };
                }
            }
        }

        // Try to find the entity among discovered workflows
        if (this._workflowCatalog is not null)
        {
            await foreach (var workflow in this._workflowCatalog.GetWorkflowsAsync(cancellationToken).ConfigureAwait(false))
            {
                var workflowId = workflow.Name ?? workflow.StartExecutorId;
                if (string.Equals(workflowId, entityId, StringComparison.OrdinalIgnoreCase))
                {
                    // Extract executor IDs from the workflow structure
                    var executorIds = new HashSet<string> { workflow.StartExecutorId };
                    var reflectedEdges = workflow.ReflectEdges();
                    foreach (var (sourceId, edgeSet) in reflectedEdges)
                    {
                        executorIds.Add(sourceId);
                        foreach (var edge in edgeSet)
                        {
                            foreach (var sinkId in edge.Connection.SinkIds)
                            {
                                executorIds.Add(sinkId);
                            }
                        }
                    }

                    // Convert executor IDs to JsonElements for the Tools property
                    var tools = executorIds.Select(id => JsonSerializer.SerializeToElement(id)).ToList();

                    // Create a default input schema (string type)
                    var defaultInputSchema = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    };

                    return new EntityInfo(
                        Id: workflowId,
                        Type: "workflow",
                        Name: workflow.Name ?? workflow.StartExecutorId,
                        Description: workflow.Description,
                        Framework: "agent-framework",
                        Tools: tools,
                        Metadata: []
                    )
                    {
                        Source = "in_memory",
                        WorkflowDump = JsonSerializer.SerializeToElement(workflow.ToDevUIDict()),
                        InputSchema = JsonSerializer.SerializeToElement(defaultInputSchema),
                        InputTypeName = "Input",
                        StartExecutorId = workflow.StartExecutorId
                    };
                }
            }
        }

        return null;
    }
}
