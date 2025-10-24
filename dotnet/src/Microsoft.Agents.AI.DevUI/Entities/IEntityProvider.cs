// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Agents.AI.DevUI.Entities;

/// <summary>
/// Provides an abstraction for discovering and retrieving entity information.
/// Implementations can use in-memory catalogs, remote discovery, or any other mechanism.
/// </summary>
public interface IEntityProvider
{
    /// <summary>
    /// Gets a collection of all available entities.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An asynchronous enumerable of entity information.</returns>
    IAsyncEnumerable<EntityInfo> GetEntitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed information about a specific entity by its identifier.
    /// </summary>
    /// <param name="entityId">The unique identifier of the entity.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The entity information if found; otherwise, <see langword="null"/>.
    /// </returns>
    Task<EntityInfo?> GetEntityAsync(string entityId, CancellationToken cancellationToken = default);
}
