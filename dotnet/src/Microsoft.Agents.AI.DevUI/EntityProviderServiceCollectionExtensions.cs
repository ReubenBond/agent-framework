// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;

using Microsoft.Agents.AI.DevUI.Entities;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Agents.AI.DevUI;

/// <summary>
/// Provides extension methods for registering entity providers in the dependency injection container.
/// </summary>
public static class EntityProviderServiceCollectionExtensions
{
    /// <summary>
    /// Adds an <see cref="IEntityProvider"/> implementation to the service collection.
    /// Multiple providers can be registered and will be automatically combined when resolving entities.
    /// </summary>
    /// <typeparam name="TProvider">The type of entity provider to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddEntityProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(this IServiceCollection services)
        where TProvider : class, IEntityProvider
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEntityProvider, TProvider>());
        return services;
    }

    /// <summary>
    /// Adds an <see cref="IEntityProvider"/> implementation to the service collection.
    /// Multiple providers can be registered and will be automatically combined when resolving entities.
    /// </summary>
    /// <typeparam name="TProvider">The type of entity provider to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="implementationFactory">The factory that creates the entity provider.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddEntityProvider<TProvider>(
        this IServiceCollection services,
        Func<IServiceProvider, TProvider> implementationFactory)
        where TProvider : class, IEntityProvider
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEntityProvider>(implementationFactory));
        return services;
    }

    /// <summary>
    /// Adds an <see cref="IEntityProvider"/> instance to the service collection.
    /// Multiple providers can be registered and will be automatically combined when resolving entities.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="provider">The entity provider instance to register.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddEntityProvider(this IServiceCollection services, IEntityProvider provider)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton(provider));
        return services;
    }

    /// <summary>
    /// Adds the default <see cref="HostingEntityProvider"/> to the service collection.
    /// This provider discovers entities from registered <see cref="Hosting.AgentCatalog"/> and <see cref="Hosting.WorkflowCatalog"/> services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCatalogEntityProvider(this IServiceCollection services)
    {
        return services.AddEntityProvider<HostingEntityProvider>();
    }
}
