// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentWebChat.AgentHost;

/// <summary>
/// Base class for agents that support durable conversation management with persistent message storage.
/// </summary>
public abstract class DurableAgent : AIAgent
{
    private readonly Func<string, ChatMessageStore> _messageStoreFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DurableAgent"/> class.
    /// </summary>
    /// <param name="messageStoreFactory">Factory function to create message stores for conversation IDs.</param>
    protected DurableAgent(Func<string, ChatMessageStore> messageStoreFactory)
    {
        this._messageStoreFactory = messageStoreFactory;
    }

    /// <inheritdoc/>
    public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null)
        => new DurableAgentThread(serializedThread, jsonSerializerOptions, this._messageStoreFactory);

    /// <inheritdoc/>
    public override AgentThread GetNewThread()
    {
        return new DurableAgentThread(this._messageStoreFactory);
    }

    /// <summary>
    /// Gets a thread instance for the specified conversation ID, loading any existing messages from the message store.
    /// </summary>
    /// <param name="conversationId">The conversation ID identifying the conversation.</param>
    /// <returns>An agent thread configured with the message store for the given conversation ID.</returns>
    public AgentThread GetThreadForConversationId(string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        return new DurableAgentThread(this._messageStoreFactory, conversationId);
    }

    /// <summary>
    /// Gets the conversation ID for the specified thread.
    /// </summary>
    /// <param name="thread">The agent thread.</param>
    /// <returns>The conversation ID, or null if not available.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="thread"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="thread"/> is not a <see cref="DurableAgentThread"/> instance.</exception>
    public static string? GetConversationId(AgentThread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (thread is not DurableAgentThread durableThread)
        {
            throw new ArgumentException($"Thread must be a {nameof(DurableAgentThread)} instance created by {nameof(DurableAgent)}.", nameof(thread));
        }

        return durableThread.ConversationId;
    }

    /// <summary>
    /// Gets all messages stored in the thread.
    /// </summary>
    /// <param name="thread">The agent thread.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
    /// <returns>The collection of stored messages.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="thread"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="thread"/> is not a <see cref="DurableAgentThread"/> instance.</exception>
    public static async Task<IEnumerable<ChatMessage>> GetMessagesAsync(AgentThread thread, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (thread is not DurableAgentThread durableThread)
        {
            throw new ArgumentException($"Thread must be a {nameof(DurableAgentThread)} instance created by {nameof(DurableAgent)}.", nameof(thread));
        }

        return await durableThread.GetMessagesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A thread type for durable agents that supports persistent message storage.
    /// </summary>
    internal sealed class DurableAgentThread : AgentThread
    {
        private readonly Func<string, ChatMessageStore>? _messageStoreFactory;
        private ChatMessageStore _messageStore;

        internal DurableAgentThread(Func<string, ChatMessageStore> messageStoreFactory)
        {
            this._messageStoreFactory = messageStoreFactory;
            this.ConversationId = Guid.NewGuid().ToString("N");
            this._messageStore = messageStoreFactory(this.ConversationId);
        }

        internal DurableAgentThread(
            Func<string, ChatMessageStore> messageStoreFactory,
            string conversationId)
        {
            this._messageStoreFactory = messageStoreFactory;
            this.ConversationId = conversationId;
            this._messageStore = messageStoreFactory(conversationId);
        }

        internal DurableAgentThread(
            JsonElement serializedThreadState,
            JsonSerializerOptions? jsonSerializerOptions,
            Func<string, ChatMessageStore> messageStoreFactory)
        {
            this._messageStoreFactory = messageStoreFactory;

            if (serializedThreadState.ValueKind is JsonValueKind.Object &&
                serializedThreadState.TryGetProperty("conversationId", out JsonElement conversationIdElement) &&
                conversationIdElement.ValueKind is JsonValueKind.String)
            {
                this.ConversationId = conversationIdElement.GetString()!;
                if (!string.IsNullOrWhiteSpace(this.ConversationId))
                {
                    this._messageStore = messageStoreFactory(this.ConversationId);
                }
            }

            if (this.ConversationId is null || this._messageStore is null)
            {
                throw new JsonException("The serialized thread state is missing a valid 'conversationId' property.");
            }
        }

        /// <inheritdoc/>
        protected override async Task MessagesReceivedAsync(IEnumerable<ChatMessage> newMessages, CancellationToken cancellationToken = default)
        {
            // Create a message store if we don't have one yet
            if (this._messageStore is null)
            {
                this._messageStore = this._messageStoreFactory!(this.ConversationId);
            }

            // Add messages to the store
            await this._messageStore.AddMessagesAsync(newMessages, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the messages stored in the thread's message store.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.</param>
        /// <returns>The collection of stored messages, or an empty collection if no store exists.</returns>
        internal async Task<IEnumerable<ChatMessage>> GetMessagesAsync(CancellationToken cancellationToken = default)
        {
            if (this._messageStore is null)
            {
                return [];
            }

            return await this._messageStore.GetMessagesAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the conversation ID associated with this thread.
        /// </summary>
        internal string ConversationId { get; }

        /// <inheritdoc/>
        public override JsonElement Serialize(JsonSerializerOptions? jsonSerializerOptions = null)
        {
            ThreadState state = new()
            {
                ConversationId = this.ConversationId
            };

            return JsonSerializer.SerializeToElement(state, jsonSerializerOptions ?? new JsonSerializerOptions());
        }

        internal sealed class ThreadState
        {
            public string? ConversationId { get; set; }
        }
    }
}
