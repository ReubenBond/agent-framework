// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost;

public class ConfigRolloutAgent : AIAgent
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<ConfigRolloutAgent> _logger;

    public override string? Name => "config-rollout";

    public ConfigRolloutAgent(IChatClient chatClient, ILogger<ConfigRolloutAgent> logger)
    {
        this._chatClient = chatClient;
        this._logger = logger;
    }

    public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null)
        => new CustomAgentThread(serializedThread, jsonSerializerOptions);

    public override AgentThread GetNewThread()
    {
        return new CustomAgentThread();
    }

    public override async Task<AgentRunResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        thread ??= this.GetNewThread();

        bool done = false;

        void MarkComplete() => done = true;

        async Task UpdateComponent(string componentResourceId, string newVersion)
        {
            this._logger.LogInformation("Updating component {ComponentResourceId} to version {NewVersion}", componentResourceId, newVersion);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            this._logger.LogInformation("Component {ComponentResourceId} updated to version {NewVersion}", componentResourceId, newVersion);
        }

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(MarkComplete, "complete", "Marks the rollout as complete."),
            AIFunctionFactory.Create(UpdateComponent, "update_component", "Updates a component to a new version.")
        };

        var systemMessage = new ChatMessage(ChatRole.System, "You are a configuration rollout agent. You can update components and mark the rollout as complete. Send the user text updates as you complete each action.");
        messages = messages.Prepend(systemMessage);

        await NotifyThreadOfNewMessagesAsync(thread, messages, cancellationToken);

        var newMessages = new List<ChatMessage>();

        while (!done)
        {
            var response = await this._chatClient.GetResponseAsync(messages, new ChatOptions { Tools = tools }, cancellationToken);
            await NotifyThreadOfNewMessagesAsync(thread, response.Messages, cancellationToken);
            newMessages.AddRange(response.Messages);
        }

        return new AgentRunResponse
        {
            AgentId = this.Id,
            ResponseId = Guid.NewGuid().ToString("N"),
            Messages = newMessages
        };
    }

    public async override IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, [EnumeratorCancellation]  CancellationToken cancellationToken = default)
    {
        thread ??= this.GetNewThread();

        bool done = false;

        void MarkComplete() => done = true;

        async Task UpdateComponent(string componentResourceId, string newVersion)
        {
            this._logger.LogInformation("Updating component {ComponentResourceId} to version {NewVersion}", componentResourceId, newVersion);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            this._logger.LogInformation("Component {ComponentResourceId} updated to version {NewVersion}", componentResourceId, newVersion);
        }

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(MarkComplete, "complete", "Marks the rollout as complete."),
            AIFunctionFactory.Create(UpdateComponent, "update_component", "Updates a component to a new version.")
        };

        var systemMessage = new ChatMessage(ChatRole.System, """
            You are a configuration rollout agent. You can update components.
            Send the user text updates as you complete each action.
            When you have fully completed the rollout specified by the user, mark it as complete.
            """);
        messages = messages.Prepend(systemMessage);

        await NotifyThreadOfNewMessagesAsync(thread, messages, cancellationToken);

        while (!done)
        {
            var updates = new List<AgentRunResponseUpdate>();
            await foreach (var update in this._chatClient.GetStreamingResponseAsync(messages, new ChatOptions { Tools = tools }, cancellationToken))
            {
                var agentUpdate = new AgentRunResponseUpdate(update);
                updates.Add(agentUpdate);
                yield return agentUpdate;
            }

            var response = updates.ToAgentRunResponse();
            await NotifyThreadOfNewMessagesAsync(thread, response.Messages, cancellationToken);
        }
    }

    /// <summary>
    /// A thread type for our custom agent that only supports in memory storage of messages.
    /// </summary>
    internal sealed class CustomAgentThread : InMemoryAgentThread
    {
        internal CustomAgentThread() { }

        internal CustomAgentThread(JsonElement serializedThreadState, JsonSerializerOptions? jsonSerializerOptions = null)
            : base(serializedThreadState, jsonSerializerOptions) { }
    }
}
