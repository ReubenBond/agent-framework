// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Extensions.AI;

namespace AgentGateway.Responses;

/// <summary>
/// Extension methods for converting usage details.
/// </summary>
internal static class UsageDetailsExtensions
{
    /// <summary>
    /// Converts usage details to response usage.
    /// </summary>
    /// <param name="usage">The usage details to convert.</param>
    /// <returns>The response usage, or null if the input is null.</returns>
    public static ResponseUsage? ToResponseUsage(this UsageDetails? usage)
    {
        if (usage is null)
        {
            return null;
        }

        var inputTokens = usage.InputTokenCount.HasValue ? (int)usage.InputTokenCount.Value : 0;
        var outputTokens = usage.OutputTokenCount.HasValue ? (int)usage.OutputTokenCount.Value : 0;
        var totalTokens = usage.TotalTokenCount.HasValue ? (int)usage.TotalTokenCount.Value : inputTokens + outputTokens;

        // TODO: Extract cached_tokens and reasoning_tokens when available from UsageDetails
        // For now, default to 0 as these are extended properties

        return new ResponseUsage
        {
            InputTokens = inputTokens,
            InputTokensDetails = new InputTokensDetails
            {
                CachedTokens = 0
            },
            OutputTokens = outputTokens,
            OutputTokensDetails = new OutputTokensDetails
            {
                ReasoningTokens = 0
            },
            TotalTokens = totalTokens
        };
    }
}
