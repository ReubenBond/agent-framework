// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;

namespace Microsoft.Agents.AI.DevUI.Entities;

/// <summary>
/// Composite entity provider that aggregates entities from multiple underlying providers.
/// </summary>
public sealed class CompositeEntityProvider : IEntityProvider
{
    private readonly IReadOnlyList<IEntityProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeEntityProvider"/> class.
    /// </summary>
    /// <param name="providers">The collection of entity providers to aggregate.</param>
    public CompositeEntityProvider(IEnumerable<IEntityProvider> providers)
    {
        this._providers = providers.ToList();
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<EntityInfo> GetEntitiesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var provider in this._providers)
        {
            await foreach (var entity in provider.GetEntitiesAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return entity;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<EntityInfo?> GetEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        foreach (var provider in this._providers)
        {
            var entity = await provider.GetEntityAsync(entityId, cancellationToken).ConfigureAwait(false);
            if (entity is not null)
            {
                return entity;
            }
        }

        return null;
    }
}
