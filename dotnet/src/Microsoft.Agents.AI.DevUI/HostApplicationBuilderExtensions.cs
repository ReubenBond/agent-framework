// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for configuring DevUI with IHostApplicationBuilder.
/// </summary>
public static class MicrosoftAgentsAIDevUIHostApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the necessary services for the DevUI to the application builder.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/> to add the services to.</param>
    /// <returns>The <see cref="IHostApplicationBuilder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    public static IHostApplicationBuilder AddDevUI(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddDevUI();

        return builder;
    }
}
