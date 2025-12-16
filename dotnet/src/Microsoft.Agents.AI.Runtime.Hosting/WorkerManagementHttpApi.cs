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

    private static Task<IResult> RegisterAsync(
        WorkerRegistrationRequest request,
        WorkerRegistry registry,
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

        return Task.FromResult(Results.Ok(new WorkerRegistrationResponse
        {
            Id = workerInfo.Id,
            HostId = request.HostId,
            RegisteredAt = DateTimeOffset.UtcNow,
            Message = isNew ? "Worker registered successfully" : "Worker registration refreshed"
        }));
    }

    private static IResult Deregister(
        [FromQuery] string endpoint,
        WorkerRegistry registry,
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

            // Publish worker deregistered event
            if (workerId is not null)
            {
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
