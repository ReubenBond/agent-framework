// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using AgentContracts;

namespace AgentGateway;

/// <summary>
/// Tracks Worker instances registered with the gateway.
/// </summary>
public sealed class WorkerRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly WorkerDiscoveryCache? _discoveryCache;

    public WorkerRegistry(WorkerDiscoveryCache? discoveryCache = null)
    {
        this._discoveryCache = discoveryCache;
    }

    public IReadOnlyCollection<WorkerInfo> ActiveWorkers => this._entries.Values
        .Where(e => !e.IsDown)
        .Select(e => e.Info)
        .ToList();

    /// <summary>
    /// Upserts a worker registration. If registrationId is supplied it is used as the key (allowing stable ids, eg instanceId) otherwise a new id is generated.
    /// Accepts a normalized representation: base endpoint plus relative health/discovery paths.
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
            static (id, state) => new Entry(new WorkerInfo(
                id,
                state.HostId,
                new Uri(state.Endpoint, UriKind.Absolute),
                state.HealthPath,
                state.DiscoveryPath), DateTimeOffset.UtcNow),
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

    public bool Remove(string registrationId)
    {
        var removed = this._entries.TryRemove(registrationId, out _);
        if (removed)
        {
            this._discoveryCache?.Invalidate(registrationId);
        }
        return removed;
    }

    public IEnumerable<Entry> Entries => this._entries.Values;

    public void MarkFailure(Entry entry, int failureThreshold)
    {
        string registrationId = entry.Info.Id;
        var newFailureCount = entry.ConsecutiveFailures + 1;
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

    public void MarkSuccess(Entry entry)
        => this._entries[entry.Info.Id] = entry with { ConsecutiveFailures = 0, IsDown = false };

    /// <summary>
    /// Info about a worker. Endpoints are represented as a base endpoint plus relative paths.
    /// </summary>
    public sealed record WorkerInfo(
        string Id,
        string HostId,
        Uri Endpoint,
        string HealthPath,
        string DiscoveryPath)
    {
        public Uri HealthUri { get; } = new Uri(Endpoint, HealthPath);
        public Uri DiscoveryUri { get; } = new Uri(Endpoint, DiscoveryPath);
    }

    public sealed record Entry(WorkerInfo Info, DateTimeOffset LastHeartbeat)
    {
        public int ConsecutiveFailures { get; init; }
        public bool IsDown { get; init; }
    }
}
