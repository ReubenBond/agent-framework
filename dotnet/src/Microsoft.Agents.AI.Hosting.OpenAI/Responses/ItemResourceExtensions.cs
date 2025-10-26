// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Converters;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses;

/// <summary>
/// Extension methods for converting between ItemResource and ChatMessage.
/// </summary>
public static class ItemResourceExtensions
{
    /// <summary>
    /// Converts an ItemResource to a ChatMessage.
    /// </summary>
    /// <param name="itemResource">The ItemResource to convert.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options for deserializing function arguments.</param>
    /// <returns>A ChatMessage.</returns>
    /// <exception cref="NotSupportedException">Thrown when the ItemResource type is not supported for conversion to ChatMessage.</exception>
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Function arguments are inherently dynamic and require runtime reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "Function arguments are inherently dynamic and require runtime code generation.")]
    public static ChatMessage ToChatMessage(this ItemResource itemResource, JsonSerializerOptions? jsonSerializerOptions = null)
    {
        if (itemResource is ResponsesMessageItemResource messageItem)
        {
            var content = messageItem switch
            {
                ResponsesAssistantMessageItemResource assistant => assistant.Content,
                ResponsesUserMessageItemResource user => user.Content,
                ResponsesSystemMessageItemResource system => system.Content,
                ResponsesDeveloperMessageItemResource developer => developer.Content,
                _ => throw new NotSupportedException($"Message item type {messageItem.GetType().Name} not supported")
            };

            // Convert ItemContent to AIContent using the existing converter
            var aiContents = content
                .Select(ItemContentConverter.ToAIContent)
                .Where(c => c is not null)
                .ToList();
            return new ChatMessage(messageItem.Role, aiContents!);
        }

        if (itemResource is FunctionToolCallItemResource functionCall)
        {
            return new ChatMessage(ChatRole.Assistant, [
                functionCall.ToFunctionCallContent(jsonSerializerOptions)
            ]);
        }

        if (itemResource is FunctionToolCallOutputItemResource functionOutput)
        {
            return new ChatMessage(ChatRole.Tool, [
                functionOutput.ToFunctionResultContent()
            ]);
        }

        throw new NotSupportedException($"ItemResource type {itemResource.GetType().Name} not supported for conversion to ChatMessage");
    }

    /// <summary>
    /// Converts a ChatMessage to ItemResource objects.
    /// This method requires an IdGenerator to create unique IDs for the generated resources.
    /// </summary>
    /// <param name="message">The chat message to convert.</param>
    /// <param name="idGenerator">The ID generator to use for creating IDs.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options to use.</param>
    /// <returns>An enumerable of ItemResource objects.</returns>
    public static IEnumerable<ItemResource> ToItemResources(this ChatMessage message, IdGenerator idGenerator, JsonSerializerOptions jsonSerializerOptions)
    {
        return message.ToItemResource(idGenerator, jsonSerializerOptions);
    }

    /// <summary>
    /// Converts a ChatMessage to a single ItemResource (message type) when it contains only message content.
    /// For messages with function calls, use ToItemResources instead.
    /// </summary>
    /// <param name="message">The chat message to convert.</param>
    /// <param name="id">The ID to assign to the message resource.</param>
    /// <returns>A ResponsesMessageItemResource.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the message contains function calls or function results.</exception>
    public static ResponsesMessageItemResource ToMessageItemResource(this ChatMessage message, string id)
    {
        // Check if the message contains function calls or function results
        if (message.Contents.Any(c => c is FunctionCallContent or FunctionResultContent))
        {
            throw new InvalidOperationException("Cannot convert a ChatMessage with function calls or function results to a single MessageItemResource. Use ToItemResources instead.");
        }

        // Convert all contents to ItemContent
        var contents = message.Contents
            .Select(ItemContentConverter.ToItemContent)
            .Where(c => c is not null)
            .ToList();

        // Create the appropriate message item resource based on role
        return message.Role.Value switch
        {
            "assistant" => new ResponsesAssistantMessageItemResource
            {
                Id = id,
                Status = ResponsesMessageItemResourceStatus.Completed,
                Content = contents!
            },
            "user" => new ResponsesUserMessageItemResource
            {
                Id = id,
                Status = ResponsesMessageItemResourceStatus.Completed,
                Content = contents!
            },
            "system" => new ResponsesSystemMessageItemResource
            {
                Id = id,
                Status = ResponsesMessageItemResourceStatus.Completed,
                Content = contents!
            },
            "developer" => new ResponsesDeveloperMessageItemResource
            {
                Id = id,
                Status = ResponsesMessageItemResourceStatus.Completed,
                Content = contents!
            },
            _ => new ResponsesAssistantMessageItemResource
            {
                Id = id,
                Status = ResponsesMessageItemResourceStatus.Completed,
                Content = contents!
            }
        };
    }

    /// <summary>
    /// Converts multiple ItemResources to ChatMessages.
    /// Adjacent message items with the same role will be combined into a single ChatMessage.
    /// </summary>
    /// <param name="itemResources">The ItemResources to convert.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options for deserializing function arguments.</param>
    /// <returns>An enumerable of ChatMessages.</returns>
    public static IEnumerable<ChatMessage> ToChatMessages(this IEnumerable<ItemResource> itemResources, JsonSerializerOptions? jsonSerializerOptions = null)
    {
        var options = jsonSerializerOptions ?? OpenAIJsonUtilities.DefaultOptions;
        ChatMessage? currentMessage = null;
        var currentContents = new List<AIContent>();

        foreach (var item in itemResources)
        {
            if (item is ResponsesMessageItemResource messageItem)
            {
                // Check if we should start a new message or continue the current one
                if (currentMessage is not null && currentMessage.Role != messageItem.Role)
                {
                    // Yield the current message and start a new one
                    yield return new ChatMessage(currentMessage.Role, [.. currentContents]);
                    currentContents = [];
                }

                // Add contents from this message item
                var aiContents = messageItem switch
                {
                    ResponsesAssistantMessageItemResource assistant => assistant.Content,
                    ResponsesUserMessageItemResource user => user.Content,
                    ResponsesSystemMessageItemResource system => system.Content,
                    ResponsesDeveloperMessageItemResource developer => developer.Content,
                    _ => Array.Empty<ItemContent>()
                };

                foreach (var content in aiContents)
                {
                    if (ItemContentConverter.ToAIContent(content) is { } aiContent)
                    {
                        currentContents.Add(aiContent);
                    }
                }

                // Initialize or update current message
                currentMessage = new ChatMessage(messageItem.Role, [.. currentContents]);
            }
            else if (item is FunctionToolCallItemResource functionCall)
            {
                // Function calls are always from assistant
                if (currentMessage is not null && currentMessage.Role != ChatRole.Assistant)
                {
                    yield return new ChatMessage(currentMessage.Role, [.. currentContents]);
                    currentContents = [];
                    currentMessage = null;
                }

                currentContents.Add(functionCall.ToFunctionCallContent(options));
                currentMessage = new ChatMessage(ChatRole.Assistant, [.. currentContents]);
            }
            else if (item is FunctionToolCallOutputItemResource functionOutput)
            {
                // Function outputs are always from tool role
                if (currentMessage is not null && currentMessage.Role != ChatRole.Tool)
                {
                    yield return new ChatMessage(currentMessage.Role, [.. currentContents]);
                    currentContents = [];
                    currentMessage = null;
                }

                currentContents.Add(functionOutput.ToFunctionResultContent());
                currentMessage = new ChatMessage(ChatRole.Tool, [.. currentContents]);
            }
        }

        // Yield the last message if any
        if (currentMessage is not null && currentContents.Count > 0)
        {
            yield return new ChatMessage(currentMessage.Role, [.. currentContents]);
        }
    }

    /// <summary>
    /// Converts a FunctionToolCallItemResource to FunctionCallContent.
    /// Uses the official Microsoft.Extensions.AI pattern via CreateFromParsedArguments to properly handle parsing errors.
    /// </summary>
    /// <param name="functionCall">The function call item resource to convert.</param>
    /// <param name="jsonSerializerOptions">The JSON serializer options for deserializing function arguments. Currently ignored as we use source-generated context.</param>
    /// <returns>A FunctionCallContent with properly parsed arguments. If parsing fails, the Exception property will be set.</returns>
    public static FunctionCallContent ToFunctionCallContent(this FunctionToolCallItemResource functionCall, JsonSerializerOptions? jsonSerializerOptions = null)
    {
        // Use the same pattern as Microsoft.Extensions.AI.OpenAI's ParseCallContent method
        // This properly handles parsing errors by setting the Exception property on FunctionCallContent
        return FunctionCallContent.CreateFromParsedArguments(
            functionCall.Arguments ?? "{}",
            functionCall.CallId,
            functionCall.Name,
            static json => JsonSerializer.Deserialize(json, OpenAIJsonContext.Default.IDictionaryStringObject)!);
    }

    /// <summary>
    /// Converts a FunctionToolCallOutputItemResource to FunctionResultContent.
    /// </summary>
    /// <param name="functionOutput">The function output item resource to convert.</param>
    /// <returns>A FunctionResultContent.</returns>
    public static FunctionResultContent ToFunctionResultContent(this FunctionToolCallOutputItemResource functionOutput)
    {
        return new FunctionResultContent(functionOutput.CallId, functionOutput.Output);
    }
}
