// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentGateway.Entities;

/// <summary>
/// Environment variable requirement for an entity.
/// </summary>
public record EnvVarRequirement
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("required")]
    public bool Required { get; init; } = true;

    [JsonPropertyName("example")]
    public string? Example { get; init; }
}

/// <summary>
/// Entity information for discovery and detailed views.
/// Matches the Python Pydantic model from agent_framework_devui.
/// </summary>
public record EntityInfo
{
    // Core entity data (always present)
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; } // "agent", "workflow"

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("framework")]
    public required string Framework { get; init; }

    [JsonPropertyName("tools")]
    public List<JsonElement>? Tools { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];

    // Source information
    [JsonPropertyName("source")]
    public string Source { get; init; } = "directory"; // "directory", "in_memory", "remote_gallery"

    [JsonPropertyName("original_url")]
    public Uri? OriginalUrl { get; init; }

    // Environment variable requirements
    [JsonPropertyName("required_env_vars")]
    public List<EnvVarRequirement>? RequiredEnvVars { get; init; }

    // Workflow-specific fields (populated only for detailed info requests)
    [JsonPropertyName("executors")]
    public List<string>? Executors { get; init; }

    [JsonPropertyName("workflow_dump")]
    public JsonElement? WorkflowDump { get; init; }

    [JsonPropertyName("input_schema")]
    public JsonElement? InputSchema { get; init; }

    [JsonPropertyName("input_type_name")]
    public string? InputTypeName { get; init; }

    [JsonPropertyName("start_executor_id")]
    public string? StartExecutorId { get; init; }
}

/// <summary>
/// Response model for entity discovery.
/// </summary>
public record DiscoveryResponse
{
    [JsonPropertyName("entities")]
    public List<EntityInfo> Entities { get; init; } = [];
}

/// <summary>
/// Request model for adding a remote entity.
/// </summary>
public record AddEntityRequest
{
    [JsonPropertyName("url")]
    public required Uri Url { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>
/// Response model for adding an entity.
/// </summary>
public record AddEntityResponse
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("entity")]
    public required EntityInfo Entity { get; init; }
}

/// <summary>
/// Response model for removing an entity.
/// </summary>
public record RemoveEntityResponse
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }
}
