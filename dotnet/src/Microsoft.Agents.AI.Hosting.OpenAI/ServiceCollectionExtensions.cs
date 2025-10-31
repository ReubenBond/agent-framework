// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses;
using Microsoft.AspNetCore.Http.Json;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to configure OpenAI Responses support.
/// </summary>
public static class MicrosoftAgentAIHostingOpenAIServiceCollectionExtensions
{
    /// <summary>
    /// Adds support for exposing <see cref="AIAgent"/> instances via OpenAI Responses.
    /// Uses the in-memory responses service implementation by default.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
    public static IServiceCollection AddOpenAIResponses(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<JsonOptions>(options => options.SerializerOptions.TypeInfoResolverChain.Add(OpenAIJsonContext.Default.Options.TypeInfoResolver!));

        // Register the default in-memory implementation if no custom implementation is registered
        services.AddSingleton<IResponsesService, InMemoryResponsesService>();

        return services;
    }

    /// <summary>
    /// Adds support for exposing <see cref="AIAgent"/> instances via OpenAI Responses with a specific agent.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="agent">The <see cref="AIAgent"/> to use for executing responses.</param>
    /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
    public static IServiceCollection AddOpenAIResponses(this IServiceCollection services, AIAgent agent)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(agent);

        services.AddOpenAIResponses();

        // Register the AIAgent-based executor as the default
        services.AddSingleton<IResponseExecutor>(sp => new AIAgentResponseExecutor(agent));

        return services;
    }

    /// <summary>
    /// Adds a custom <see cref="IResponsesService"/> implementation.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <typeparam name="TService">The type of the <see cref="IResponsesService"/> implementation.</typeparam>
    /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
    public static IServiceCollection AddOpenAIResponses<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(this IServiceCollection services)
        where TService : class, IResponsesService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<JsonOptions>(options => options.SerializerOptions.TypeInfoResolverChain.Add(OpenAIJsonContext.Default.Options.TypeInfoResolver!));

        // Replace the default implementation with the custom one
        services.AddSingleton<IResponsesService, TService>();

        return services;
    }

    /// <summary>
    /// Adds a custom <see cref="IResponseExecutor"/> implementation.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <typeparam name="TExecutor">The type of the <see cref="IResponseExecutor"/> implementation.</typeparam>
    /// <returns>The <see cref="IServiceCollection"/> for method chaining.</returns>
    public static IServiceCollection AddResponseExecutor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExecutor>(this IServiceCollection services)
        where TExecutor : class, IResponseExecutor
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IResponseExecutor, TExecutor>();

        return services;
    }
}
