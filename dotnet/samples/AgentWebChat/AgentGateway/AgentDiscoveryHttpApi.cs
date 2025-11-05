// Copyright (c) Microsoft. All rights reserved.

using AgentContracts;

namespace AgentGateway;

internal static class AgentDiscoveryHttpApi
{
    public static void MapDiscovery(this WebApplication app)
    {
        // Agents discovery - aggregates agents from all available workers
        app.MapGet("/agents", async (WorkerRegistry registry, WorkerDiscoveryCache cache, CancellationToken cancellationToken) =>
        {
            var allAgents = new Dictionary<string, AgentDiscoveryCard>(StringComparer.OrdinalIgnoreCase);
            foreach (var worker in registry.ActiveWorkers.Where(w => w.DiscoveryPath is not null))
            {
                var supportedAgents = await cache.DiscoverAgentsAsync(worker, cancellationToken);
                if (supportedAgents is not null)
                {
                    foreach (var (agentName, agentCard) in supportedAgents)
                    {
                        // Only add each agent once (in case multiple workers support the same agent)
                        if (!allAgents.ContainsKey(agentName))
                        {
                            allAgents[agentName] = agentCard;
                        }
                    }
                }
            }

            return Results.Ok(allAgents.Values.ToList());
        });
    }
}
