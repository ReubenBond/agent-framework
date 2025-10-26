// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

/// <summary>
/// Extension methods for converting ItemParam (input) to ItemResource (output).
/// </summary>
public static class ItemParamExtensions
{
    /// <summary>
    /// Converts an ItemParam (input model) to an ItemResource (output model) by adding server-generated fields.
    /// This is a simplified implementation that handles the most common message types used in conversations.
    /// For other types, it performs a round-trip through JSON serialization to preserve all fields.
    /// </summary>
    /// <param name="param">The input item parameter.</param>
    /// <returns>An ItemResource with a generated ID.</returns>
    public static ItemResource ToItemResource(this ItemParam param)
    {
        ArgumentNullException.ThrowIfNull(param);

        string generatedId = $"msg_{Guid.NewGuid():N}";

        return param switch
        {
            ResponsesUserMessageItemParam userMessageParam => new ResponsesUserMessageItemResource
            {
                Id = generatedId,
                Content = NormalizeContent(userMessageParam.Content, isInput: true),
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            ResponsesSystemMessageItemParam systemMessageParam => new ResponsesSystemMessageItemResource
            {
                Id = generatedId,
                Content = NormalizeContent(systemMessageParam.Content, isInput: true),
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            ResponsesAssistantMessageItemParam assistantMessageParam => new ResponsesAssistantMessageItemResource
            {
                Id = generatedId,
                Content = NormalizeContent(assistantMessageParam.Content, isInput: false),
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            ResponsesDeveloperMessageItemParam developerMessageParam => new ResponsesDeveloperMessageItemResource
            {
                Id = generatedId,
                Content = NormalizeContent(developerMessageParam.Content, isInput: true),
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            FunctionToolCallItemParam functionCallParam => new FunctionToolCallItemResource
            {
                Id = generatedId,
                Name = functionCallParam.Name,
                CallId = functionCallParam.CallId,
                Arguments = functionCallParam.Arguments,
                Status = FunctionToolCallItemResourceStatus.Completed
            },
            FunctionToolCallOutputItemParam functionOutputParam => new FunctionToolCallOutputItemResource
            {
                Id = generatedId,
                CallId = functionOutputParam.CallId,
                Output = functionOutputParam.Output
            },
            // For all other types, do a round-trip JSON serialization to convert Param to Resource
            // and inject the generated ID
            _ => ConvertViaJsonSerialization(param, generatedId)
        };
    }

    /// <summary>
    /// Normalizes message content from string or IList&lt;ItemContent&gt; to IList&lt;ItemContent&gt;.
    /// </summary>
    /// <param name="content">The content which can be a string or IList&lt;ItemContent&gt;.</param>
    /// <param name="isInput">Whether this is input content (true) or output content (false).</param>
    /// <returns>A list of ItemContent objects.</returns>
    private static IList<ItemContent> NormalizeContent(object content, bool isInput)
    {
        if (content is string textContent)
        {
            // Convert string to appropriate ItemContent type
            ItemContent itemContent = isInput
                ? new ItemContentInputText { Text = textContent }
                : new ItemContentOutputText { Text = textContent, Annotations = [] };

            return new List<ItemContent> { itemContent };
        }
        else if (content is IList<ItemContent> contentList)
        {
            return contentList;
        }
        else
        {
            throw new InvalidOperationException($"Unexpected content type: {content?.GetType().Name ?? "null"}");
        }
    }

    private static ItemResource ConvertViaJsonSerialization(ItemParam param, string generatedId)
    {
        // Serialize the param to JSON
        string json = JsonSerializer.Serialize(param, OpenAIJsonContext.Default.ItemParam);

        // Parse as JsonDocument and add the ID field
        using var doc = JsonDocument.Parse(json);
        using var stream = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", generatedId);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        // Deserialize as ItemResource
        stream.Position = 0;
        var resource = JsonSerializer.Deserialize(stream, OpenAIJsonContext.Default.ItemResource);
        return resource ?? throw new InvalidOperationException($"Failed to convert {param.GetType().Name} to ItemResource");
    }
}
