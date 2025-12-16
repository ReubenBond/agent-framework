// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.Agents.AI.Runtime.Abstractions.Workers;

/// <summary>
/// Registration payload sent from the Worker to the Gateway using normalized schema.
/// </summary>
public sealed class WorkerRegistrationRequest
{
    [JsonPropertyName("hostId")]
    public string HostId { get; init; } = Environment.MachineName;

    // Base endpoint (scheme://host[:port])
    [JsonPropertyName("endpoint")]
    [Required(ErrorMessage = "Endpoint is required.")]
    [AbsoluteUri(ErrorMessage = "Endpoint must be a valid absolute URI.")]
    public required string Endpoint { get; init; }

    // Relative paths
    [JsonPropertyName("healthPath")]
    public string HealthPath { get; init; } = "/health";

    [JsonPropertyName("discoveryPath")]
    public string DiscoveryPath { get; init; } = "/v1/entities";
}

/// <summary>
/// Response from the Gateway after successful worker registration.
/// </summary>
public sealed class WorkerRegistrationResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("hostId")]
    public required string HostId { get; init; }

    [JsonPropertyName("registeredAt")]
    public DateTimeOffset RegisteredAt { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Metadata about a worker process.
/// </summary>
public sealed class WorkerProcessMetadata
{
    [JsonPropertyName("hostId")]
    public required string HostId { get; init; }

    [JsonPropertyName("endpoint")]
    public required string Endpoint { get; init; }

    [JsonPropertyName("healthPath")]
    public string HealthPath { get; init; } = "/health";

    [JsonPropertyName("discoveryPath")]
    public string DiscoveryPath { get; init; } = "/v1/entities";

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

/// <summary>
/// Validation attribute for absolute URIs.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class AbsoluteUriAttribute : ValidationAttribute
{
    /// <inheritdoc/>
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true; // Let [Required] handle null
        }

        if (value is not string stringValue)
        {
            return false;
        }

        return Uri.TryCreate(stringValue, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
