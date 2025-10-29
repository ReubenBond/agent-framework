// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentWebChat.AgentHost.DurableAgents;
using AgentWebChat.AgentHost.DurableAgents.Utilities;
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
            var context = DurableFunctionInvokingChatClient.CurrentContext;
            if (context?.MemoStorage is null)
            {
                this._logger.LogWarning("Memo storage not available. Updating component immediately.");
                this._logger.LogInformation("Updating component {ComponentResourceId} to version {NewVersion}", componentResourceId, newVersion);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                this._logger.LogInformation("Component {ComponentResourceId} updated to version {NewVersion}", componentResourceId, newVersion);
                return;
            }

            // Get memo to track first call time
            Memo memo = await context.MemoStorage.GetMemoAsync(cancellationToken);

            if (!memo.TryGetValue("firstCallTime", out string? firstCallTimeStr))
            {
                // First time this function is called - record the time
                DateTimeOffset firstCallTime = DateTimeOffset.UtcNow;
                memo["firstCallTime"] = firstCallTime.ToString("O");
                memo["componentResourceId"] = componentResourceId;
                memo["newVersion"] = newVersion;

                await context.MemoStorage.SetMemoAsync(memo, cancellationToken);

                this._logger.LogInformation("First call to update component {ComponentResourceId} to version {NewVersion}. Waiting 1 minute before proceeding.", componentResourceId, newVersion);
                throw new InvalidOperationException($"Update for {componentResourceId} scheduled. Please wait 1 minute and call this function again.");
            }

            // Parse the first call time
            DateTimeOffset recordedFirstCallTime = DateTimeOffset.Parse(firstCallTimeStr);
            TimeSpan elapsed = DateTimeOffset.UtcNow - recordedFirstCallTime;

            if (elapsed < TimeSpan.FromMinutes(1))
            {
                TimeSpan remaining = TimeSpan.FromMinutes(1) - elapsed;
                this._logger.LogInformation("Update for component {ComponentResourceId} not ready yet. {RemainingSeconds} seconds remaining.", componentResourceId, (int)remaining.TotalSeconds);
                throw new InvalidOperationException($"Update for {componentResourceId} not ready yet. Please wait {(int)remaining.TotalSeconds} more seconds.");
            }

            // Enough time has passed - perform the update
            this._logger.LogInformation("Updating component {ComponentResourceId} to version {NewVersion} (1 minute wait period satisfied)", componentResourceId, newVersion);
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
            var context = DurableFunctionInvokingChatClient.CurrentContext;
            if (context?.MemoStorage is null)
            {
                this._logger.LogWarning("Memo storage not available. Updating component immediately.");
                this._logger.LogInformation("Updating component {ComponentResourceId} to version {NewVersion}", componentResourceId, newVersion);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                this._logger.LogInformation("Component {ComponentResourceId} updated to version {NewVersion}", componentResourceId, newVersion);
                return;
            }

            var callId = context.CallContent.CallId;

            // Get memo to track first call time
            Memo memo = await context.MemoStorage.GetMemoAsync(cancellationToken);

            if (!memo.TryGetValue("firstCallTime", out string? firstCallTimeStr))
            {
                // First time this function is called - record the time
                DateTimeOffset firstCallTime = DateTimeOffset.UtcNow;
                memo["firstCallTime"] = firstCallTime.ToString("O");
                memo["componentResourceId"] = componentResourceId;
                memo["newVersion"] = newVersion;

                await context.MemoStorage.SetMemoAsync(memo, cancellationToken);

                this._logger.LogInformation("{CallId}: First call to update component {ComponentResourceId} to version {NewVersion}. Waiting 1 minute before proceeding.", callId, componentResourceId, newVersion);
                throw new InvalidOperationException($"Update for {componentResourceId} scheduled. Please wait 1 minute and call this function again.");
            }

            // Parse the first call time
            DateTimeOffset recordedFirstCallTime = DateTimeOffset.Parse(firstCallTimeStr);
            TimeSpan elapsed = DateTimeOffset.UtcNow - recordedFirstCallTime;

            if (elapsed < TimeSpan.FromMinutes(1))
            {
                TimeSpan remaining = TimeSpan.FromMinutes(1) - elapsed;
                this._logger.LogInformation("{CallId}: Update for component {ComponentResourceId} not ready yet. {RemainingSeconds} seconds remaining.", callId, componentResourceId, (int)remaining.TotalSeconds);
                throw new InvalidOperationException($"Update for {componentResourceId} not ready yet. Please wait {(int)remaining.TotalSeconds} more seconds.");
            }

            // Enough time has passed - perform the update
            this._logger.LogInformation("{CallId}: Updating component {ComponentResourceId} to version {NewVersion} (1 minute wait period satisfied)", callId, componentResourceId, newVersion);
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
