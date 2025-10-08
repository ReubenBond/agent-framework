// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using AgentContracts;
using Microsoft.Agents.AI.Hosting;

namespace AgentWebChat.AgentHost;

internal static class AgentFrameworkWebApplicationExtensions
{
    public static void MapAgentDiscovery(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string path)
    {
        var routeGroup = endpoints.MapGroup(path);
        routeGroup.MapGet("/", async (
            AgentCatalog agentCatalog,
            CancellationToken cancellationToken) =>
            {
                var results = new List<AgentDiscoveryCard>();
                await foreach (var result in agentCatalog.GetAgentsAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(new AgentDiscoveryCard
                    {
                        Name = result.Name!,
                        Description = result.Description,
                    });
                }

                return Results.Ok(results);
            })
            .WithName("GetAgents");
    }
}
