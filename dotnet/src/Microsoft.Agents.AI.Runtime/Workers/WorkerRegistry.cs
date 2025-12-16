// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Agents.AI.Runtime.Abstractions.Workers;
using Microsoft.Extensions.Options;

namespace Microsoft.Agents.AI.Runtime.Workers;

/// <summary>
/// Tracks Worker instances registered with the runtime.
/// </summary>
public sealed class WorkerRegistry
{
    /// <summary>
    /// Well-known ID for the default worker.
    /// </summary>
    public const string DefaultWorkerId = "__default__";

    private readonly ConcurrentDictionary<string, WorkerEntry> _entries = new();
    private readonly WorkerDiscoveryCache? _discoveryCache;

    /// <summary>
    /// Gets the default worker if one is configured, otherwise null.
    /// </summary>
    public WorkerInfo? DefaultWorker { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerRegistry"/> class.
    /// </summary>
    public WorkerRegistry(WorkerDiscoveryCache? discoveryCache, IOptions<RuntimeOptions> options)
    {
        this._discoveryCache = discoveryCache;

        // Register configured workers
        var workers = options.Value.Workers;
        for (int i = 0; i < workers.Count; i++)
        {
            var worker = workers[i];
            if (string.IsNullOrEmpty(worker.Endpoint))
            {
                continue;
            }

            var isDefault = i == 0;
            var hostId = worker.HostId ?? $"worker-{i + 1}";
            var id = isDefault ? DefaultWorkerId : worker.Endpoint;

            var workerInfo = new WorkerInfo(
                id,
                hostId,
                new Uri(worker.Endpoint, UriKind.Absolute),
                worker.HealthPath,
                worker.DiscoveryPath,
                IsDefault: isDefault);

            this._entries[id] = new WorkerEntry(workerInfo, DateTimeOffset.UtcNow);

            if (isDefault)
            {
                this.DefaultWorker = workerInfo;
            }
        }
    }

    /// <summary>
    /// Gets all active (not down) workers.
    /// </summary>
    public IReadOnlyCollection<WorkerInfo> ActiveWorkers => this._entries.Values
        .Where(e => !e.IsDown)
        .Select(e => e.Info)
        .ToList();

    /// <summary>
    /// Gets all worker entries.
    /// </summary>
    public IEnumerable<WorkerEntry> Entries => this._entries.Values;

    /// <summary>
    /// Upserts a worker registration.
    /// </summary>
    /// <remarks>
    /// The request parameter should be validated using Data Annotations validation before calling this method.
    /// </remarks>
    public WorkerInfo Upsert(WorkerRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = request.Endpoint;

        var result = this._entries.AddOrUpdate(
            id,
            static (id, state) => new WorkerEntry(new WorkerInfo(
                id,
                state.HostId,
                new Uri(state.Endpoint, UriKind.Absolute),
                state.HealthPath,
                state.DiscoveryPath,
                IsDefault: false), DateTimeOffset.UtcNow),
            static (id, existing, state) => existing with
            {
                Info = existing.Info with
                {
                    HostId = state.HostId,
                    Endpoint = new Uri(state.Endpoint, UriKind.Absolute),
                    HealthPath = state.HealthPath,
                    DiscoveryPath = state.DiscoveryPath,
                },
                LastHeartbeat = DateTimeOffset.UtcNow,
                ConsecutiveFailures = 0,
                IsDown = false
            },
            request);

        return result.Info;
    }

    /// <summary>
    /// Removes a worker registration by ID.
    /// </summary>
    public bool Remove(string registrationId)
    {
        // Don't allow removal of the default worker
        if (registrationId == DefaultWorkerId)
        {
            return false;
        }

        var removed = this._entries.TryRemove(registrationId, out _);
        if (removed)
        {
            this._discoveryCache?.Invalidate(registrationId);
        }
        return removed;
    }

    /// <summary>
    /// Gets a worker by ID.
    /// </summary>
    public WorkerInfo? Get(string registrationId)
    {
        return this._entries.TryGetValue(registrationId, out var entry) ? entry.Info : null;
    }

    /// <summary>
    /// Marks a health check failure for a worker.
    /// </summary>
    public void MarkFailure(WorkerEntry entry, int failureThreshold)
    {
        string registrationId = entry.Info.Id;
        var newFailureCount = entry.ConsecutiveFailures + 1;

        // Don't remove the default worker on failure
        if (registrationId == DefaultWorkerId)
        {
            this._entries[registrationId] = entry with { ConsecutiveFailures = newFailureCount, IsDown = false };
            return;
        }

        if (newFailureCount >= failureThreshold)
        {
            this._entries.TryRemove(registrationId, out _);
            this._discoveryCache?.Invalidate(registrationId);
        }
        else
        {
            this._entries[registrationId] = entry with { ConsecutiveFailures = newFailureCount, IsDown = false };
            // Invalidate cache on any failure to ensure fresh discovery on next request
            this._discoveryCache?.Invalidate(registrationId);
        }
    }

    /// <summary>
    /// Marks a successful health check for a worker.
    /// </summary>
    public void MarkSuccess(WorkerEntry entry)
        => this._entries[entry.Info.Id] = entry with { ConsecutiveFailures = 0, IsDown = false };
}

/// <summary>
/// Information about a worker. Endpoints are represented as a base endpoint plus relative paths.
/// </summary>
public sealed record WorkerInfo(
    string Id,
    string HostId,
    Uri Endpoint,
    string HealthPath,
    string DiscoveryPath,
    bool IsDefault = false)
{
    /// <summary>
    /// Gets the full health check URI.
    /// </summary>
    public Uri HealthUri { get; } = new Uri(Endpoint, HealthPath);

    /// <summary>
    /// Gets the full discovery URI.
    /// </summary>
    public Uri DiscoveryUri { get; } = new Uri(Endpoint, DiscoveryPath);
}

/// <summary>
/// Entry tracking a worker's state.
/// </summary>
public sealed record WorkerEntry(WorkerInfo Info, DateTimeOffset LastHeartbeat)
{
    /// <summary>
    /// Gets the number of consecutive health check failures.
    /// </summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>
    /// Gets whether the worker is marked as down.
    /// </summary>
    public bool IsDown { get; init; }
}
