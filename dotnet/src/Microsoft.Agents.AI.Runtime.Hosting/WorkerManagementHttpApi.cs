// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Runtime.Abstractions.Monitoring;
using Microsoft.Agents.AI.Runtime.Abstractions.Workers;
using Microsoft.Agents.AI.Runtime.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Agents.AI.Runtime.Hosting;

/// <summary>
/// HTTP API endpoints for worker management.
/// Provides endpoints for worker registration and deregistration.
/// </summary>
public static class WorkerManagementHttpApi
{
    /// <summary>
    /// Maps worker management API endpoints to the application.
    /// </summary>
    public static WebApplication MapWorkerManagementApi(this WebApplication app)
    {
        app.MapPost("/workers/registrations", RegisterAsync)
            .WithName("WorkerRegister")
            .WithDescription("Register or refresh a worker")
            .Produces<WorkerRegistrationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapDelete("/workers/registrations", Deregister)
            .WithName("WorkerDeregister")
            .WithDescription("Deregister a worker")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        WorkerRegistrationRequest request,
        WorkerRegistry registry,
        WorkerDiscoveryCache discoveryCache,
        IMonitoringEventBroadcaster eventBroadcaster,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("WorkerManagementHttpApi");

        var isNew = !registry.Entries.Any(e => e.Info.Endpoint.ToString() == request.Endpoint);
        var workerInfo = registry.Upsert(request);

        logger.LogInformation(
            "Worker registration/heartbeat from host {HostId}. Endpoint={Endpoint} HealthPath={HealthPath} DiscoveryPath={DiscoveryPath}",
            request.HostId,
            request.Endpoint,
            request.HealthPath,
            request.DiscoveryPath);

        // Pre-warm the discovery cache for this worker.
        // This runs in the background to avoid blocking the registration response,
        // but ensures the cache is populated before the first workflow dispatch.
        if (isNew)
        {
            _ = PreWarmDiscoveryCacheAsync(workerInfo, discoveryCache, logger, ct);
        }

        // Publish worker event
        eventBroadcaster.PublishWorkerEvent(
            isNew ? MonitoringEventTypes.WorkerRegistered : MonitoringEventTypes.WorkerHealthChanged,
            new WorkerEventPayload
            {
                WorkerId = workerInfo.Id,
                HostId = request.HostId,
                Endpoint = request.Endpoint,
                Status = "Healthy"
            });

        return Results.Ok(new WorkerRegistrationResponse
        {
            Id = workerInfo.Id,
            HostId = request.HostId,
            RegisteredAt = DateTimeOffset.UtcNow,
            Message = isNew ? "Worker registered successfully" : "Worker registration refreshed"
        });
    }

    /// <summary>
    /// Pre-warms the discovery cache by calling the worker's discovery endpoint.
    /// This ensures that the first workflow dispatch doesn't have to wait for discovery.
    /// </summary>
    private static async Task PreWarmDiscoveryCacheAsync(
        WorkerInfo workerInfo,
        WorkerDiscoveryCache discoveryCache,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var entities = await discoveryCache.DiscoverEntitiesAsync(workerInfo, ct).ConfigureAwait(false);
            if (entities is not null)
            {
                logger.LogDebug(
                    "Pre-warmed discovery cache for worker {WorkerId} with {EntityCount} entities",
                    workerInfo.Id,
                    entities.Count);
            }
        }
        catch (Exception ex)
        {
            // Don't fail registration if cache pre-warming fails
            logger.LogWarning(
                ex,
                "Failed to pre-warm discovery cache for worker {WorkerId}",
                workerInfo.Id);
        }
    }

    private static IResult Deregister(
        [FromQuery] string endpoint,
        WorkerRegistry registry,
        WorkerDiscoveryCache discoveryCache,
        IMonitoringEventBroadcaster eventBroadcaster,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("WorkerManagementHttpApi");

        // Get worker info before removal
        var worker = registry.Entries.FirstOrDefault(e => e.Info.Endpoint.ToString() == endpoint);
        var workerId = worker?.Info.Id;

        if (registry.Remove(endpoint))
        {
            logger.LogInformation("Worker '{Endpoint}' deregistered", endpoint);

            // Invalidate the discovery cache for this worker
            if (workerId is not null)
            {
                discoveryCache.Invalidate(workerId);

                eventBroadcaster.PublishWorkerEvent(MonitoringEventTypes.WorkerDeregistered, new WorkerEventPayload
                {
                    WorkerId = workerId
                });
            }

            return Results.NoContent();
        }

        return Results.NotFound();
    }
}
