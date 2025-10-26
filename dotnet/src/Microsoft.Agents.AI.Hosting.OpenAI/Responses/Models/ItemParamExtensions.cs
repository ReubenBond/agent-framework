// Copyright (c) Microsoft. All rights reserved.

using System;

namespace Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

/// <summary>
/// Extension methods for converting ItemParam (input) to ItemResource (output).
/// </summary>
public static class ItemParamExtensions
{
    /// <summary>
    /// Converts an ItemParam (input model) to an ItemResource (output model) by adding server-generated fields.
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
                Content = userMessageParam.Content.ToItemContents(),
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            ResponsesSystemMessageItemParam systemMessageParam => new ResponsesSystemMessageItemResource
            {
                Id = generatedId,
                Content = systemMessageParam.Content.ToItemContents(),
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            ResponsesAssistantMessageItemParam assistantMessageParam => new ResponsesAssistantMessageItemResource
            {
                Id = generatedId,
                Content = assistantMessageParam.Content.ToItemContents(),
                Status = ResponsesMessageItemResourceStatus.Completed
            },
            ResponsesDeveloperMessageItemParam developerMessageParam => new ResponsesDeveloperMessageItemResource
            {
                Id = generatedId,
                Content = developerMessageParam.Content.ToItemContents(),
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
            FileSearchToolCallItemParam fileSearchParam => new FileSearchToolCallItemResource
            {
                Id = generatedId
            },
            ComputerToolCallItemParam computerCallParam => new ComputerToolCallItemResource
            {
                Id = generatedId
            },
            ComputerToolCallOutputItemParam computerOutputParam => new ComputerToolCallOutputItemResource
            {
                Id = generatedId
            },
            WebSearchToolCallItemParam webSearchParam => new WebSearchToolCallItemResource
            {
                Id = generatedId
            },
            ReasoningItemParam reasoningParam => new ReasoningItemResource
            {
                Id = generatedId
            },
            ItemReferenceItemParam itemRefParam => new ItemReferenceItemResource
            {
                Id = generatedId
            },
            ImageGenerationToolCallItemParam imageGenParam => new ImageGenerationToolCallItemResource
            {
                Id = generatedId
            },
            CodeInterpreterToolCallItemParam codeInterpreterParam => new CodeInterpreterToolCallItemResource
            {
                Id = generatedId
            },
            LocalShellToolCallItemParam localShellParam => new LocalShellToolCallItemResource
            {
                Id = generatedId
            },
            LocalShellToolCallOutputItemParam localShellOutputParam => new LocalShellToolCallOutputItemResource
            {
                Id = generatedId
            },
            MCPListToolsItemParam mcpListToolsParam => new MCPListToolsItemResource
            {
                Id = generatedId
            },
            MCPApprovalRequestItemParam mcpApprovalRequestParam => new MCPApprovalRequestItemResource
            {
                Id = generatedId
            },
            MCPApprovalResponseItemParam mcpApprovalResponseParam => new MCPApprovalResponseItemResource
            {
                Id = generatedId
            },
            MCPCallItemParam mcpCallParam => new MCPCallItemResource
            {
                Id = generatedId
            },
            // Fallback for unknown types
            _ => throw new InvalidOperationException($"Unknown ItemParam type: {param.GetType().Name}")
        };
    }
}
