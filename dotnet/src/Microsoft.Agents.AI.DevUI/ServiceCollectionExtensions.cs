// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring DevUI services.
/// </summary>
public static class MicrosoftAgentsAIDevUIServiceCollectionExtensions
{
    /// <summary>
    /// Adds the necessary services for the DevUI to the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddDevUI(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddInMemoryConversationStorage();
        services.AddInMemoryAgentConversationIndex();

        return services;
    }
}
