// Copyright (c) Microsoft. All rights reserved.

using System;
using Microsoft.Agents.AI.Runtime.Abstractions.Monitoring;
using Microsoft.Agents.AI.Runtime.Abstractions.Workers;
using Microsoft.Agents.AI.Runtime.Monitoring;
using Microsoft.Agents.AI.Runtime.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Agents.AI.Runtime.Hosting;

/// <summary>
/// Extension methods for configuring the Agent Runtime.
/// </summary>
public static class RuntimeHostingExtensions
{
    /// <summary>
    /// Adds Agent Runtime services to the service collection.
    /// </summary>
    public static IServiceCollection AddAgentRuntime(
        this IServiceCollection services,
        Action<RuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configure options
        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Core services
        services.AddSingleton<WorkerDiscoveryCache>();
        services.AddSingleton<WorkerRegistry>();
        services.AddSingleton<IWorkflowExecutor, WorkerWorkflowExecutor>();

        // Monitoring
        services.AddSingleton<MonitoringEventBroadcaster>();
        services.AddSingleton<IMonitoringEventBroadcaster>(sp => sp.GetRequiredService<MonitoringEventBroadcaster>());
        services.AddSingleton<IMonitoringService, MonitoringService>();

        // Background services
        services.AddHostedService<WorkerHealthCheckService>();

        // HTTP client factory
        services.AddHttpClient();

        return services;
    }

    /// <summary>
    /// Adds Agent Runtime services to the host application builder.
    /// </summary>
    public static IHostApplicationBuilder AddAgentRuntime(
        this IHostApplicationBuilder builder,
        string configurationSectionName = RuntimeOptions.SectionName,
        Action<RuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Bind configuration
        builder.Services.Configure<RuntimeOptions>(
            builder.Configuration.GetSection(configurationSectionName));

        // Add core services
        builder.Services.AddAgentRuntime(configure);

        return builder;
    }
}
