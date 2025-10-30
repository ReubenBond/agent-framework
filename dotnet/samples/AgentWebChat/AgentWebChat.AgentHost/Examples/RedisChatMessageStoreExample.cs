// Copyright (c) Microsoft. All rights reserved.

// This example demonstrates how to use the ConfigRolloutAgent with Redis-backed message storage
// to load and continue conversations based on ConversationId.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost.Examples;

/// <summary>
/// Example demonstrating Redis-backed chat message storage with ConfigRolloutAgent.
/// </summary>
public static class RedisChatMessageStoreExample
{
    /// <summary>
    /// Example: Starting a new configuration rollout conversation.
    /// </summary>
    public static async Task StartNewRolloutAsync(ConfigRolloutAgent agent, CancellationToken cancellationToken = default)
    {
        // Create a new thread - this will auto-generate a ConversationId and store messages in Redis
        AgentThread thread = agent.GetNewThread();

        // User's initial request
        List<ChatMessage> messages = new()
        {
            new ChatMessage(ChatRole.User, "Please update component-A to version 2.0 and component-B to version 1.5")
        };

        // Run the agent
        AgentRunResponse response = await agent.RunAsync(messages, thread, cancellationToken: cancellationToken);

        // Get the conversation ID for this conversation (can be stored/returned to the client)
        string? conversationId = ConfigRolloutAgent.GetConversationId(thread);
        Console.WriteLine($"Started rollout with ConversationId: {conversationId}");

        // Display the agent's response
        foreach (ChatMessage message in response.Messages)
        {
            if (message.Role == ChatRole.Assistant)
            {
                Console.WriteLine($"Agent: {message.Text}");
            }
        }
    }

    /// <summary>
    /// Example: Continuing an existing configuration rollout conversation by ConversationId.
    /// </summary>
    public static async Task ContinueExistingRolloutAsync(
        ConfigRolloutAgent agent,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        // Load the existing thread using the ConversationId
        // This will automatically load all previous messages from Redis
        AgentThread thread = agent.GetThreadForConversationId(conversationId);

        // Get all previous messages to include in context
        IEnumerable<ChatMessage> previousMessages = await ConfigRolloutAgent.GetMessagesAsync(thread, cancellationToken);

        Console.WriteLine($"Loaded {previousMessages.Count()} previous messages for ConversationId: {conversationId}");

        // User's follow-up message
        List<ChatMessage> newMessages = new()
        {
            new ChatMessage(ChatRole.User, "Also update component-C to version 3.0")
        };

        // Combine previous messages with new message
        List<ChatMessage> allMessages = new(previousMessages);
        allMessages.AddRange(newMessages);

        // Run the agent with full context
        AgentRunResponse response = await agent.RunAsync(allMessages, thread, cancellationToken: cancellationToken);

        // Display the agent's response
        foreach (ChatMessage message in response.Messages)
        {
            if (message.Role == ChatRole.Assistant)
            {
                Console.WriteLine($"Agent: {message.Text}");
            }
        }
    }

    /// <summary>
    /// Example: Retrieving conversation history by ConversationId.
    /// </summary>
    public static async Task GetConversationHistoryAsync(
        ConfigRolloutAgent agent,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        // Load the thread
        AgentThread thread = agent.GetThreadForConversationId(conversationId);

        // Get all messages
        IEnumerable<ChatMessage> messages = await ConfigRolloutAgent.GetMessagesAsync(thread, cancellationToken);

        Console.WriteLine($"Conversation History for ConversationId: {conversationId}");
        Console.WriteLine(new string('-', 50));

        foreach (ChatMessage message in messages)
        {
            Console.WriteLine($"[{message.Role}] {message.Text}");
        }
    }

    /// <summary>
    /// Example: Using streaming with Redis-backed storage.
    /// </summary>
    public static async Task StreamingRolloutAsync(ConfigRolloutAgent agent, CancellationToken cancellationToken = default)
    {
        // Create a new thread
        AgentThread thread = agent.GetNewThread();

        List<ChatMessage> messages = new()
        {
            new ChatMessage(ChatRole.User, "Update component-X to version 4.0")
        };

        // Use streaming to get real-time updates
        await foreach (AgentRunResponseUpdate streamUpdate in agent.RunStreamingAsync(messages, thread, cancellationToken: cancellationToken))
        {
            // Process each update - in a real scenario, you would process the streaming content
        }

        Console.WriteLine();

        // Messages are automatically stored in Redis during streaming
        string? conversationId = ConfigRolloutAgent.GetConversationId(thread);
        Console.WriteLine($"Streaming conversation saved with ConversationId: {conversationId}");
    }

    /// <summary>
    /// Example: Complete workflow showing conversation persistence.
    /// </summary>
    public static async Task CompleteWorkflowExampleAsync(
        ConfigRolloutAgent agent,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("=== Starting New Rollout ===");

        // Step 1: Start a new rollout
        AgentThread thread = agent.GetNewThread();
        List<ChatMessage> messages = new()
        {
            new ChatMessage(ChatRole.User, "Update component-A to version 2.0")
        };

        AgentRunResponse response = await agent.RunAsync(messages, thread, cancellationToken: cancellationToken);
        string? conversationId = ConfigRolloutAgent.GetConversationId(thread);

        Console.WriteLine($"Created conversation with ConversationId: {conversationId}");

        // Step 2: Simulate application restart - load the conversation from Redis
        Console.WriteLine("\n=== Simulating App Restart - Loading from Redis ===");

        AgentThread loadedThread = agent.GetThreadForConversationId(conversationId!);
        IEnumerable<ChatMessage> loadedMessages = await ConfigRolloutAgent.GetMessagesAsync(loadedThread, cancellationToken);

        Console.WriteLine($"Loaded {loadedMessages.Count()} messages from Redis");

        // Step 3: Continue the conversation
        Console.WriteLine("\n=== Continuing Conversation ===");

        List<ChatMessage> continueMessages = new(loadedMessages)
        {
            new ChatMessage(ChatRole.User, "Update component-B to version 1.5")
        };

        AgentRunResponse continueResponse = await agent.RunAsync(
            continueMessages,
            loadedThread,
            cancellationToken: cancellationToken);

        Console.WriteLine("Conversation continued successfully!");

        // Step 4: View final conversation history
        Console.WriteLine("\n=== Final Conversation History ===");

        IEnumerable<ChatMessage> finalMessages = await ConfigRolloutAgent.GetMessagesAsync(loadedThread, cancellationToken);

        foreach (ChatMessage message in finalMessages)
        {
            string messageText = message.Text ?? string.Empty;
            int maxLength = Math.Min(100, messageText.Length);
            Console.WriteLine($"[{message.Role}] {messageText.Substring(0, maxLength)}...");
        }
    }
}
