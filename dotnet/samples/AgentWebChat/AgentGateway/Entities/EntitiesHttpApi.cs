// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using AgentContracts;

namespace AgentGateway.Entities;

/// <summary>
/// Minimal API endpoints for entity discovery and management.
/// Compatible with the Python DevUI frontend.
/// </summary>
public static class EntitiesHttpApi
{
    public static IEndpointRouteBuilder MapEntitiesApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/entities")
            .WithTags("Entities")
            .WithOpenApi();

        // List all entities
        group.MapGet("", ListEntitiesAsync)
            .WithName("ListEntities")
            .WithSummary("List all registered entities (agents and workflows)");

        // Get detailed entity information
        group.MapGet("{entityId}/info", GetEntityInfoAsync)
            .WithName("GetEntityInfo")
            .WithSummary("Get detailed information about a specific entity");

        // Add entity from URL
        group.MapPost("add", AddEntityAsync)
            .WithName("AddEntity")
            .WithSummary("Add entity from URL");

        // Remove entity by ID
        group.MapDelete("{entityId}", RemoveEntityAsync)
            .WithName("RemoveEntity")
            .WithSummary("Remove entity by ID");

        return endpoints;
    }

    private static async Task<IResult> ListEntitiesAsync(
        WorkerRegistry registry,
        WorkerDiscoveryCache cache,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = new List<EntityInfo>();

            // Discover agents from all workers
            var allAgents = new Dictionary<string, AgentDiscoveryCard>(StringComparer.OrdinalIgnoreCase);
            foreach (var worker in registry.ActiveWorkers.Where(w => w.DiscoveryPath is not null))
            {
                var supportedAgents = await cache.DiscoverAgentsAsync(worker, cancellationToken);
                if (supportedAgents is not null)
                {
                    foreach (var (agentName, agentCard) in supportedAgents)
                    {
                        if (!allAgents.ContainsKey(agentName))
                        {
                            allAgents[agentName] = agentCard;
                        }
                    }
                }
            }

            // Convert agents to EntityInfo
            foreach (var (agentId, agentCard) in allAgents)
            {
                var tools = new List<JsonElement>();
                // AgentDiscoveryCard.Tools property access removed - not available in current schema

                entities.Add(new EntityInfo
                {
                    Id = agentId,
                    Type = "agent",
                    Name = agentCard.Name ?? agentId,
                    Description = agentCard.Description,
                    Framework = "agent-framework",
                    Tools = tools,
                    Metadata = [],
                    Source = "directory"
                });
            }

            // TODO: Add workflow discovery when workflow support is implemented

            return Results.Ok(new DiscoveryResponse { Entities = entities });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error listing entities");
        }
    }

    private static async Task<IResult> GetEntityInfoAsync(
        string entityId,
        WorkerRegistry registry,
        WorkerDiscoveryCache cache,
        CancellationToken cancellationToken)
    {
        try
        {
            // Try to find the entity among discovered agents
            foreach (var worker in registry.ActiveWorkers.Where(w => w.DiscoveryPath is not null))
            {
                var supportedAgents = await cache.DiscoverAgentsAsync(worker, cancellationToken);
                if (supportedAgents is not null && supportedAgents.TryGetValue(entityId, out var agentCard))
                {
                    var tools = new List<JsonElement>();
                    // AgentDiscoveryCard.Tools property access removed - not available in current schema

                    var entityInfo = new EntityInfo
                    {
                        Id = entityId,
                        Type = "agent",
                        Name = agentCard.Name ?? entityId,
                        Description = agentCard.Description,
                        Framework = "agent-framework",
                        Tools = tools,
                        Metadata = [],
                        Source = "directory"
                    };

                    return Results.Ok(entityInfo);
                }
            }

            // TODO: Check workflows when workflow support is implemented

            return Results.NotFound(new { error = new { message = $"Entity '{entityId}' not found.", type = "invalid_request_error" } });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error getting entity info");
        }
    }

    private static Task<IResult> AddEntityAsync(
        AddEntityRequest request,
        CancellationToken cancellationToken)
    {
        // TODO: Implement remote entity fetching
        // This would require:
        // 1. HTTP client to fetch entity definition from URL
        // 2. Entity parser/validator
        // 3. Storage mechanism for remote entities
        // 4. Integration with WorkerRegistry

        return Task.FromResult(Results.Problem(
            detail: "Remote entity addition not yet implemented",
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented"));
    }

    private static Task<IResult> RemoveEntityAsync(
        string entityId,
        CancellationToken cancellationToken)
    {
        // TODO: Implement entity removal
        // This would require:
        // 1. Check if entity exists
        // 2. Remove from registry/storage
        // 3. Cleanup any associated resources

        return Task.FromResult(Results.Problem(
            detail: "Entity removal not yet implemented",
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented"));
    }
}
