// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.AI;

namespace AgentContracts;

/// <summary>
/// Source generated JSON serialization context for the AgentHost project (normalized worker registration schema).
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    WriteIndented = false)]
// Worker registration types
[JsonSerializable(typeof(WorkerRegistrationRequest))]
[JsonSerializable(typeof(WorkerRegistrationResponse))]
[JsonSerializable(typeof(WorkerProcessMetadata))]
[JsonSerializable(typeof(AgentDiscoveryCard))]
[JsonSerializable(typeof(List<AgentDiscoveryCard>))]
[ExcludeFromCodeCoverage]
public sealed partial class AgentContractsJsonContext : JsonSerializerContext;
