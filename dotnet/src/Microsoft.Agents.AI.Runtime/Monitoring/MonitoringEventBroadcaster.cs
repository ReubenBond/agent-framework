// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Agents.AI.Runtime.Abstractions.Monitoring;

namespace Microsoft.Agents.AI.Runtime.Monitoring;

/// <summary>
/// Broadcasts monitoring events to subscribers.
/// </summary>
public sealed class MonitoringEventBroadcaster : IMonitoringEventBroadcaster
{
    private readonly ConcurrentDictionary<string, Channel<MonitoringEvent>> _subscribers = new();

    /// <summary>
    /// Publishes a monitoring event to all subscribers.
    /// </summary>
    public void PublishEvent(MonitoringEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        foreach (var channel in this._subscribers.Values)
        {
            // Non-blocking write - if the channel is full, drop the event
            channel.Writer.TryWrite(evt);
        }
    }

    /// <summary>
    /// Publishes a workflow event.
    /// </summary>
    public void PublishWorkflowEvent(string eventType, WorkflowEventPayload payload)
    {
        this.PublishEvent(new WorkflowMonitoringEvent
        {
            EventType = eventType,
            Payload = payload
        });
    }

    /// <summary>
    /// Publishes a worker event.
    /// </summary>
    public void PublishWorkerEvent(string eventType, WorkerEventPayload payload)
    {
        this.PublishEvent(new WorkerMonitoringEvent
        {
            EventType = eventType,
            Payload = payload
        });
    }

    /// <summary>
    /// Subscribes to monitoring events.
    /// </summary>
    public async IAsyncEnumerable<MonitoringEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var subscriberId = Guid.NewGuid().ToString("N");
        var channel = Channel.CreateBounded<MonitoringEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        this._subscribers.TryAdd(subscriberId, channel);

        try
        {
#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
#pragma warning restore CA2007
            {
                yield return evt;
            }
        }
        finally
        {
            this._subscribers.TryRemove(subscriberId, out _);
            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Gets the current number of subscribers.
    /// </summary>
    public int SubscriberCount => this._subscribers.Count;
}
