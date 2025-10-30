// Copyright (c) Microsoft. All rights reserved.

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost;

public class ConfigRolloutAgent : DurableAgent
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<ConfigRolloutAgent> _logger;

    public override string? Name => "config-rollout";

    public ConfigRolloutAgent(
        IChatClient chatClient,
        ILogger<ConfigRolloutAgent> logger,
        Func<string, ChatMessageStore> messageStoreFactory)
        : base(messageStoreFactory)
    {
        this._chatClient = chatClient;
        this._logger = logger;
    }

    public override async Task<AgentRunResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        => await this.RunStreamingAsync(messages, thread, options, cancellationToken).ToAgentRunResponseAsync(cancellationToken);

    public async override IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, [EnumeratorCancellation]  CancellationToken cancellationToken = default)
    {
        bool done = false;

        void MarkComplete() => done = true;

        async Task UpdateComponent(string componentResourceId, string newVersion)
        {
            this._logger.LogInformation("Updating component {ComponentResourceId} to version {NewVersion}", componentResourceId, newVersion);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            this._logger.LogInformation("Component {ComponentResourceId} updated to version {NewVersion}", componentResourceId, newVersion);
        }

        var chatClientOptions = (options as ChatClientAgentRunOptions)?.ChatOptions?.Clone() ?? new();
        chatClientOptions.Tools =
        [
            AIFunctionFactory.Create(MarkComplete, "complete", "Marks the rollout as complete."),
            AIFunctionFactory.Create(UpdateComponent, "update_component", "Updates a component to a new version.")
        ];

        var systemMessage = new ChatMessage(ChatRole.System, """
            You are a configuration rollout agent. You can update components.
            Send the user text updates as you complete each action.
            When you have fully completed the rollout specified by the user, mark it as complete.
            """);
        messages = messages.Prepend(systemMessage);

        thread ??= this.GetNewThread();
        await NotifyThreadOfNewMessagesAsync(thread, messages, cancellationToken);

        while (!done)
        {
            var updates = new List<AgentRunResponseUpdate>();
            await foreach (var update in this._chatClient.GetStreamingResponseAsync(messages, chatClientOptions, cancellationToken))
            {
                var agentUpdate = new AgentRunResponseUpdate(update);
                updates.Add(agentUpdate);
                yield return agentUpdate;
            }

            var response = updates.ToAgentRunResponse();
            await NotifyThreadOfNewMessagesAsync(thread, response.Messages, cancellationToken);
        }
    }
}
