// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.DevUI.Entities;
using Microsoft.Agents.AI.Hosting;

namespace Microsoft.Agents.AI.DevUI;

/// <summary>
/// Provides extension methods for mapping entity discovery and management endpoints to an <see cref="IEndpointRouteBuilder"/>.
/// </summary>
public static class EntitiesApiExtensions
{
    /// <summary>
    /// Maps HTTP API endpoints for entity discovery and management.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add the routes to.</param>
    /// <param name="entityProvider">
    /// Optional custom entity provider. If not specified, all registered <see cref="IEntityProvider"/> instances
    /// will be resolved from DI and combined. If no providers are registered, a default
    /// <see cref="HostingEntityProvider"/> will be created using <see cref="AgentCatalog"/> and <see cref="WorkflowCatalog"/>.
    /// </param>
    /// <returns>The <see cref="IEndpointRouteBuilder"/> for method chaining.</returns>
    /// <remarks>
    /// This extension method registers the following endpoints:
    /// <list type="bullet">
    /// <item><description>GET /v1/entities - List all registered entities (agents and workflows)</description></item>
    /// <item><description>GET /v1/entities/{entityId}/info - Get detailed information about a specific entity</description></item>
    /// </list>
    /// The endpoints are compatible with the Python DevUI frontend and automatically discover entities
    /// using the provided or registered <see cref="IEntityProvider"/> instances.
    /// </remarks>
    public static IEndpointConventionBuilder MapEntities(this IEndpointRouteBuilder endpoints, IEntityProvider? entityProvider = null)
    {
        var group = endpoints.MapGroup("/v1/entities")
            .WithTags("Entities");

        // List all entities
        group.MapGet("", (IEnumerable<IEntityProvider> providers, AgentCatalog? agentCatalog, WorkflowCatalog? workflowCatalog, CancellationToken cancellationToken) =>
            ListEntitiesAsync(ResolveEntityProvider(entityProvider, providers, agentCatalog, workflowCatalog), cancellationToken))
            .WithName("ListEntities")
            .WithSummary("List all registered entities (agents and workflows)")
            .Produces<DiscoveryResponse>(StatusCodes.Status200OK, contentType: "application/json");

        // Get detailed entity information
        group.MapGet("{entityId}/info", (string entityId, IEnumerable<IEntityProvider> providers, AgentCatalog? agentCatalog, WorkflowCatalog? workflowCatalog, CancellationToken cancellationToken) =>
            GetEntityInfoAsync(entityId, ResolveEntityProvider(entityProvider, providers, agentCatalog, workflowCatalog), cancellationToken))
            .WithName("GetEntityInfo")
            .WithSummary("Get detailed information about a specific entity")
            .Produces<EntityInfo>(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static IEntityProvider ResolveEntityProvider(
        IEntityProvider? explicitProvider,
        IEnumerable<IEntityProvider> registeredProviders,
        AgentCatalog? agentCatalog,
        WorkflowCatalog? workflowCatalog)
    {
        // If an explicit provider was passed to MapEntities, use it
        if (explicitProvider is not null)
        {
            return explicitProvider;
        }

        // Try to use registered providers from DI
        var providerList = registeredProviders.ToList();
        if (providerList.Count > 0)
        {
            return providerList.Count == 1
                ? providerList[0]
                : new CompositeEntityProvider(providerList);
        }

        // Fall back to catalog-based provider
        return new HostingEntityProvider(agentCatalog, workflowCatalog);
    }

    private static async Task<IResult> ListEntitiesAsync(
        IEntityProvider entityProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var entities = new List<EntityInfo>();

            await foreach (var entity in entityProvider.GetEntitiesAsync(cancellationToken).ConfigureAwait(false))
            {
                entities.Add(entity);
            }

            return Results.Json(new DiscoveryResponse(entities), EntitiesJsonContext.Default.DiscoveryResponse);
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
        IEntityProvider entityProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var entityInfo = await entityProvider.GetEntityAsync(entityId, cancellationToken).ConfigureAwait(false);

            if (entityInfo is not null)
            {
                return Results.Json(entityInfo, EntitiesJsonContext.Default.EntityInfo);
            }

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
}
