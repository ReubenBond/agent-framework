// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using AgentGateway.Entities;
using AgentGateway.Threads;

namespace AgentGateway;

/// <summary>
/// JSON serializer context for AgentGateway API types.
/// Uses source generation for improved performance and trimming support.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(ThreadInfo))]
[JsonSerializable(typeof(CreateThreadRequest))]
[JsonSerializable(typeof(ThreadListResponse))]
[JsonSerializable(typeof(ThreadData))]
[JsonSerializable(typeof(ThreadDeletionResponse))]
[JsonSerializable(typeof(ThreadMessagesResponse))]
[JsonSerializable(typeof(EntityInfo))]
[JsonSerializable(typeof(DiscoveryResponse))]
[JsonSerializable(typeof(AddEntityRequest))]
[JsonSerializable(typeof(AddEntityResponse))]
[JsonSerializable(typeof(RemoveEntityResponse))]
[JsonSerializable(typeof(EnvVarRequirement))]
[JsonSerializable(typeof(List<EntityInfo>))]
[JsonSerializable(typeof(List<ThreadData>))]

[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
public partial class GatewayJsonSerializerContext : JsonSerializerContext
{
}
