// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AgentContracts;

/// <summary>
/// Registration payload sent from the Agent Host (worker) to the Agent Gateway using normalized schema.
/// </summary>
public sealed class WorkerRegistrationRequest
{
    [JsonPropertyName("hostId")]
    [Required(ErrorMessage = "HostId is required.")]
    [MinLength(1, ErrorMessage = "HostId cannot be empty.")]
    public required string HostId { get; init; }

    // Base endpoint (scheme://host[:port])
    [JsonPropertyName("endpoint")]
    [Required(ErrorMessage = "Endpoint is required.")]
    [AbsoluteUri(ErrorMessage = "Endpoint must be a valid absolute URI.")]
    public required string Endpoint { get; init; }

    // Relative paths
    [JsonPropertyName("healthPath")]
    [Required(ErrorMessage = "HealthPath is required.")]
    [MinLength(1, ErrorMessage = "HealthPath cannot be empty.")]
    public required string HealthPath { get; init; }

    [JsonPropertyName("discoveryPath")]
    [Required(ErrorMessage = "DiscoveryPath is required.")]
    [MinLength(1, ErrorMessage = "DiscoveryPath cannot be empty.")]
    public required string DiscoveryPath { get; init; }
}
