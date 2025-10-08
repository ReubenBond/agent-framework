// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Yarp.ReverseProxy.Forwarder;

namespace AgentGateway;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by dependency injection")]
internal sealed class WorkerHttpForwarder
{
    private readonly WorkerRegistry _registry;
    private readonly IHttpForwarder _forwarder;
    private readonly ForwardingHttpClientProvider _clientProvider;
    private readonly WorkerDiscoveryCache _cache;

    public WorkerHttpForwarder(
        WorkerRegistry registry,
        IHttpForwarder forwarder,
        ForwardingHttpClientProvider clientProvider,
        WorkerDiscoveryCache cache)
    {
        this._registry = registry;
        this._forwarder = forwarder;
        this._clientProvider = clientProvider;
        this._cache = cache;
    }

    public async ValueTask ForwardRequestAsync(HttpContext context, string agent, string? id = null)
    {
        var worker = await this.SelectWorkerAsync(agent, context.RequestAborted);
        if (worker is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync($"No available worker supports agent '{agent}'");
            return;
        }

        var basePrefix = worker.Endpoint.ToString().TrimEnd('/');
        var httpClient = this._clientProvider.HttpClient;
        var error = await this._forwarder.SendAsync(context, basePrefix, httpClient, ForwarderRequestConfig.Empty, HttpTransformer.Default);
        if (error != ForwarderError.None && !context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }

    // Select the best available worker (if any) from the registry that supports the given agent.
    private async ValueTask<WorkerRegistry.WorkerInfo?> SelectWorkerAsync(string? agentName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(agentName))
        {
            // If no specific agent is requested, return any available worker
            return this._registry.ActiveWorkers
                    .FirstOrDefault(w => w.DiscoveryPath is not null)
                ?? this._registry.ActiveWorkers.FirstOrDefault();
        }

        // Query each worker's discovery endpoint to find one that supports the specific agent
        foreach (var worker in this._registry.ActiveWorkers.Where(w => w.DiscoveryPath is not null))
        {
            var supportedAgents = await this._cache.DiscoverAgentsAsync(worker, cancellationToken);
            if (supportedAgents?.ContainsKey(agentName) == true)
            {
                return worker;
            }
        }

        // No worker found that supports this agent
        return null;
    }
}
