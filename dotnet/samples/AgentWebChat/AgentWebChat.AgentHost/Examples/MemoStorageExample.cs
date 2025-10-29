// Copyright (c) Microsoft. All rights reserved.

// This file demonstrates how to use the durable memo storage feature in tool calls.

using System.ComponentModel;
using AgentWebChat.AgentHost.DurableAgents;
using AgentWebChat.AgentHost.DurableAgents.Utilities;

namespace AgentWebChat.AgentHost.Examples;

/// <summary>
/// Example demonstrating how to use memo storage in AI function tools.
/// </summary>
public static class MemoStorageExample
{
    /// <summary>
    /// Example AI function that uses memo storage to maintain state across calls.
    /// </summary>
    [Description("Increments a counter and returns the new value. Demonstrates stateful tool calls using memo storage.")]
    public static async Task<string> IncrementCounterAsync(
        [Description("The name of the counter to increment")] string counterName,
        CancellationToken cancellationToken = default)
    {
        // Access the memo storage from the current tool call context
        var context = DurableFunctionInvokingChatClient.CurrentContext;
        if (context?.MemoStorage is null)
        {
            return "Memo storage is not available. Ensure DurableFunctionInvokingChatClient is configured with IMemoStorage.";
        }

        // Get the current memo (which persists across calls with the same CallId)
        Memo memo = await context.MemoStorage.GetMemoAsync(cancellationToken);

        // Read the current counter value, defaulting to 0 if not set
        int currentValue = memo.TryGetValue(counterName, out string? valueStr) && int.TryParse(valueStr, out int value)
            ? value
            : 0;

        // Increment the counter
        int newValue = currentValue + 1;

        // Update the memo
        memo[counterName] = newValue.ToString();

        try
        {
            // Save the memo with ETag-based concurrency control
            await context.MemoStorage.SetMemoAsync(memo, cancellationToken);
            return $"Counter '{counterName}' incremented to {newValue}";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ETag mismatch"))
        {
            // Handle concurrency conflict - another call modified the memo
            return $"Failed to update counter '{counterName}' due to concurrent modification. Please retry.";
        }
    }

    /// <summary>
    /// Example AI function that stores and retrieves arbitrary data.
    /// </summary>
    [Description("Stores a key-value pair in persistent storage associated with this tool call.")]
    public static async Task<string> StoreDataAsync(
        [Description("The key to store the data under")] string key,
        [Description("The value to store")] string value,
        CancellationToken cancellationToken = default)
    {
        var context = DurableFunctionInvokingChatClient.CurrentContext;
        if (context?.MemoStorage is null)
        {
            return "Memo storage is not available.";
        }

        // Get the current memo
        Memo memo = await context.MemoStorage.GetMemoAsync(cancellationToken);

        // Store the data
        memo[key] = value;

        try
        {
            // Save with concurrency control
            await context.MemoStorage.SetMemoAsync(memo, cancellationToken);
            return $"Successfully stored '{key}' with value '{value}'";
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ETag mismatch"))
        {
            return "Failed to store data due to concurrent modification. Please retry.";
        }
    }

    /// <summary>
    /// Example AI function that retrieves stored data.
    /// </summary>
    [Description("Retrieves a value from persistent storage associated with this tool call.")]
    public static async Task<string> RetrieveDataAsync(
        [Description("The key to retrieve")] string key,
        CancellationToken cancellationToken = default)
    {
        var context = DurableFunctionInvokingChatClient.CurrentContext;
        if (context?.MemoStorage is null)
        {
            return "Memo storage is not available.";
        }

        // Get the current memo
        Memo memo = await context.MemoStorage.GetMemoAsync(cancellationToken);

        // Try to retrieve the value
        if (memo.TryGetValue(key, out string? value))
        {
            return $"Found '{key}': {value}";
        }

        return $"Key '{key}' not found in storage.";
    }

    /// <summary>
    /// Example showing how to handle concurrency conflicts with retry logic.
    /// </summary>
    [Description("Demonstrates handling ETag conflicts with retry logic when updating shared state.")]
    public static async Task<string> UpdateWithRetryAsync(
        [Description("The key to update")] string key,
        [Description("The new value")] string newValue,
        CancellationToken cancellationToken = default)
    {
        var context = DurableFunctionInvokingChatClient.CurrentContext;
        if (context?.MemoStorage is null)
        {
            return "Memo storage is not available.";
        }

        // Retry up to 3 times in case of concurrency conflicts
        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Get the current memo
                Memo memo = await context.MemoStorage.GetMemoAsync(cancellationToken);

                // Update the value
                memo[key] = newValue;

                // Try to save
                await context.MemoStorage.SetMemoAsync(memo, cancellationToken);

                return $"Successfully updated '{key}' to '{newValue}' (attempt {attempt + 1})";
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("ETag mismatch") && attempt < maxRetries - 1)
            {
                // Retry on ETag conflict (unless it's the last attempt)
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), cancellationToken);
            }
        }

        return $"Failed to update '{key}' after {maxRetries} attempts due to concurrent modifications.";
    }
}
