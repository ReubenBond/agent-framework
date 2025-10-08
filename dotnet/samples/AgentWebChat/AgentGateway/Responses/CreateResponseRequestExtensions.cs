// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Agents.AI;

namespace AgentGateway.Responses;

/// <summary>
/// Extension methods for converting create response requests.
/// </summary>
internal static class CreateResponseExtensions
{
    /// <summary>
    /// Creates run options from a create response request.
    /// </summary>
    /// <param name="request">The request to convert.</param>
    /// <returns>The chat client agent run options.</returns>
    public static ChatClientAgentRunOptions ToRunOptions(this CreateResponse request)
    {
        var chatOptions = new Microsoft.Extensions.AI.ChatOptions();

        if (request.Model is not null)
        {
            chatOptions.ModelId = request.Model;
        }

        if (request.MaxOutputTokens.HasValue)
        {
            chatOptions.MaxOutputTokens = request.MaxOutputTokens.Value;
        }

        if (request.Temperature.HasValue)
        {
            chatOptions.Temperature = (float)request.Temperature.Value;
        }

        if (request.TopP.HasValue)
        {
            chatOptions.TopP = (float)request.TopP.Value;
        }

        // Note: TopLogprobs, SafetyIdentifier, PromptCacheKey, and other advanced options
        // are not currently supported in ChatOptions and would need to be added to the underlying API

        return new ChatClientAgentRunOptions
        {
            ChatOptions = chatOptions
        };
    }
}
