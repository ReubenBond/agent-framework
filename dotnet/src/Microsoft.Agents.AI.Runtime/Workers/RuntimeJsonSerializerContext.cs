// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.DevUI.Entities;

namespace Microsoft.Agents.AI.Runtime.Workers;

/// <summary>
/// JSON serialization context for Runtime types.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiscoveryResponse))]
[JsonSerializable(typeof(EntityInfo))]
[ExcludeFromCodeCoverage]
internal sealed partial class RuntimeJsonSerializerContext : JsonSerializerContext;
