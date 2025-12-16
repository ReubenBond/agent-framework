// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Runtime.Abstractions.Workflows;

namespace Microsoft.Agents.AI.Runtime.Abstractions.Workers;

/// <summary>
/// Interface for executing workflows on workers.
/// Used by the Gateway to dispatch workflow execution to worker instances.
/// </summary>
public interface IWorkflowExecutor
{
    /// <summary>
    /// Executes a workflow on a worker.
    /// </summary>
    /// <param name="request">The workflow execution request.</param>
    /// <param name="preferredWorkerId">Optional worker ID to use for execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the execution dispatch.</returns>
    Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowExecutionRequest request,
        string? preferredWorkerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused workflow on a worker.
    /// </summary>
    /// <param name="request">The workflow resume request.</param>
    /// <param name="preferredWorkerId">Optional worker ID to use for execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the resume dispatch.</returns>
    Task<WorkflowExecutionResult> ResumeAsync(
        WorkflowResumeRequest request,
        string? preferredWorkerId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a workflow execution dispatch.
/// </summary>
public sealed class WorkflowExecutionResult
{
    /// <summary>
    /// Whether the dispatch was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The worker ID that accepted the workflow.
    /// </summary>
    public string? WorkerId { get; init; }

    /// <summary>
    /// Error code if the dispatch failed.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Error message if the dispatch failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static WorkflowExecutionResult Succeeded(string? workerId = null)
    {
        return new WorkflowExecutionResult { Success = true, WorkerId = workerId };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static WorkflowExecutionResult Failed(string errorCode, string errorMessage)
    {
        return new WorkflowExecutionResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
