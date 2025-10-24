// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses.Converters;

/// <summary>
/// JSON converter for message content that can be either a simple string or an array of ItemContent objects.
/// </summary>
internal sealed class MessageContentConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize(ref reader, (JsonTypeInfo<List<ItemContent>>)options.GetTypeInfo(typeof(List<ItemContent>)));
        }
        else if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        throw new JsonException($"Unexpected token type for message content: {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        if (value is string stringValue)
        {
            writer.WriteStringValue(stringValue);
        }
        else if (value is JsonElement jsonElement)
        {
            jsonElement.WriteTo(writer);
        }
        else if (value is IList<ItemContent> contentList)
        {
            JsonSerializer.Serialize(writer, contentList, (JsonTypeInfo<List<ItemContent>>)options.GetTypeInfo(typeof(List<ItemContent>)));
        }
        else if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            throw new JsonException($"Unexpected content type: {value.GetType()}");
        }
    }
}
