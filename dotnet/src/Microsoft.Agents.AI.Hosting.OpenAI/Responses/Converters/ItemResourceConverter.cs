// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses.Converters;

/// <summary>
/// JSON converter for ItemResource that handles type discrimination.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class ItemResourceConverter : JsonConverter<ItemResource>
{
    public override ItemResource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Clone the reader to peek at the JSON
        Utf8JsonReader readerClone = reader;

        // Read through the JSON to find the type property
        string? type = null;

        if (readerClone.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object");
        }

        while (readerClone.Read())
        {
            if (readerClone.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (readerClone.TokenType == JsonTokenType.PropertyName)
            {
                string propertyName = readerClone.GetString()!;
                readerClone.Read(); // Move to the value

                if (propertyName == "type")
                {
                    type = readerClone.GetString();
                    break;
                }

                if (readerClone.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    // Skip nested objects/arrays
                    readerClone.Skip();
                }
            }
        }

        // Determine the concrete type based on the type discriminator and deserialize using the source generation context
        return type switch
        {
            ResponsesMessageItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.ResponsesMessageItemResource),
            FileSearchToolCallItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.FileSearchToolCallItemResource),
            FunctionToolCallItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.FunctionToolCallItemResource),
            FunctionToolCallOutputItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.FunctionToolCallOutputItemResource),
            ComputerToolCallItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.ComputerToolCallItemResource),
            ComputerToolCallOutputItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.ComputerToolCallOutputItemResource),
            WebSearchToolCallItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.WebSearchToolCallItemResource),
            ReasoningItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.ReasoningItemResource),
            ItemReferenceItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.ItemReferenceItemResource),
            ImageGenerationToolCallItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.ImageGenerationToolCallItemResource),
            CodeInterpreterToolCallItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.CodeInterpreterToolCallItemResource),
            LocalShellToolCallItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.LocalShellToolCallItemResource),
            LocalShellToolCallOutputItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.LocalShellToolCallOutputItemResource),
            MCPListToolsItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.MCPListToolsItemResource),
            MCPApprovalRequestItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.MCPApprovalRequestItemResource),
            MCPApprovalResponseItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.MCPApprovalResponseItemResource),
            MCPCallItemResource.ItemType => JsonSerializer.Deserialize(ref reader, OpenAIJsonContext.Default.MCPCallItemResource),
            _ => throw new JsonException($"Unknown item type: {type}")
        };
    }

    public override void Write(Utf8JsonWriter writer, ItemResource value, JsonSerializerOptions options)
    {
        // Directly serialize using the appropriate type info from the context
        switch (value)
        {
            case ResponsesMessageItemResource message:
                JsonSerializer.Serialize(writer, message, OpenAIJsonContext.Default.ResponsesMessageItemResource);
                break;
            case FileSearchToolCallItemResource fileSearch:
                JsonSerializer.Serialize(writer, fileSearch, OpenAIJsonContext.Default.FileSearchToolCallItemResource);
                break;
            case FunctionToolCallItemResource functionCall:
                JsonSerializer.Serialize(writer, functionCall, OpenAIJsonContext.Default.FunctionToolCallItemResource);
                break;
            case FunctionToolCallOutputItemResource functionOutput:
                JsonSerializer.Serialize(writer, functionOutput, OpenAIJsonContext.Default.FunctionToolCallOutputItemResource);
                break;
            case ComputerToolCallItemResource computerCall:
                JsonSerializer.Serialize(writer, computerCall, OpenAIJsonContext.Default.ComputerToolCallItemResource);
                break;
            case ComputerToolCallOutputItemResource computerOutput:
                JsonSerializer.Serialize(writer, computerOutput, OpenAIJsonContext.Default.ComputerToolCallOutputItemResource);
                break;
            case WebSearchToolCallItemResource webSearch:
                JsonSerializer.Serialize(writer, webSearch, OpenAIJsonContext.Default.WebSearchToolCallItemResource);
                break;
            case ReasoningItemResource reasoning:
                JsonSerializer.Serialize(writer, reasoning, OpenAIJsonContext.Default.ReasoningItemResource);
                break;
            case ItemReferenceItemResource itemReference:
                JsonSerializer.Serialize(writer, itemReference, OpenAIJsonContext.Default.ItemReferenceItemResource);
                break;
            case ImageGenerationToolCallItemResource imageGeneration:
                JsonSerializer.Serialize(writer, imageGeneration, OpenAIJsonContext.Default.ImageGenerationToolCallItemResource);
                break;
            case CodeInterpreterToolCallItemResource codeInterpreter:
                JsonSerializer.Serialize(writer, codeInterpreter, OpenAIJsonContext.Default.CodeInterpreterToolCallItemResource);
                break;
            case LocalShellToolCallItemResource localShell:
                JsonSerializer.Serialize(writer, localShell, OpenAIJsonContext.Default.LocalShellToolCallItemResource);
                break;
            case LocalShellToolCallOutputItemResource localShellOutput:
                JsonSerializer.Serialize(writer, localShellOutput, OpenAIJsonContext.Default.LocalShellToolCallOutputItemResource);
                break;
            case MCPListToolsItemResource mcpListTools:
                JsonSerializer.Serialize(writer, mcpListTools, OpenAIJsonContext.Default.MCPListToolsItemResource);
                break;
            case MCPApprovalRequestItemResource mcpApprovalRequest:
                JsonSerializer.Serialize(writer, mcpApprovalRequest, OpenAIJsonContext.Default.MCPApprovalRequestItemResource);
                break;
            case MCPApprovalResponseItemResource mcpApprovalResponse:
                JsonSerializer.Serialize(writer, mcpApprovalResponse, OpenAIJsonContext.Default.MCPApprovalResponseItemResource);
                break;
            case MCPCallItemResource mcpCall:
                JsonSerializer.Serialize(writer, mcpCall, OpenAIJsonContext.Default.MCPCallItemResource);
                break;
            default:
                throw new JsonException($"Unknown item type: {value.GetType().Name}");
        }
    }
}
