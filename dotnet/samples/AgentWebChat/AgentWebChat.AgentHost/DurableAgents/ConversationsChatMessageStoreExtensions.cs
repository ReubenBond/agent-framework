// Copyright (c) Microsoft. All rights reserved.

using AgentWebChat.AgentHost.DurableAgents.Utilities;
using Microsoft.Agents.AI;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Conversations API-backed chat message storage.
/// </summary>
public static class ConversationsChatMessageStoreExtensions
{
    /// <summary>
    /// Registers a factory for creating Conversations API-backed chat message stores.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="gatewayBaseAddress">The base address of the AgentGateway hosting the Conversations API. If null, uses the HttpClient's base address.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This extension method registers a factory function that creates <see cref="ConversationsChatMessageStore"/>
    /// instances for storing chat messages using the OpenAI Conversations API exposed by the AgentGateway.
    /// </para>
    /// <para>
    /// The factory creates an HttpClient configured to call the Conversations API endpoints and returns
    /// a store instance for the specified conversation ID.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// builder.Services.AddConversationsChatMessageStore("https://gateway:5001");
    ///
    /// // Later, inject the factory:
    /// public MyAgent(Func&lt;string, ChatMessageStore&gt; messageStoreFactory)
    /// {
    ///     var store = messageStoreFactory("my-conversation-id");
    ///     // Use the store...
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddConversationsChatMessageStore(
        this IServiceCollection services,
        Uri? gatewayBaseAddress = null)
    {
        services.AddSingleton<Func<string, ChatMessageStore>>(sp =>
        {
            IHttpClientFactory httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            return conversationId =>
            {
                HttpClient httpClient = httpClientFactory.CreateClient();

                if (gatewayBaseAddress is not null)
                {
                    httpClient.BaseAddress = gatewayBaseAddress;
                }

                ConversationsApiClient apiClient = new(httpClient);

                return new ConversationsChatMessageStore(apiClient, conversationId);
            };
        });

        return services;
    }

    /// <summary>
    /// Registers a factory for creating Conversations API-backed chat message stores with a named HttpClient.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="httpClientName">The name of the HTTP client to use for API calls.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This extension method registers a factory function that creates <see cref="ConversationsChatMessageStore"/>
    /// instances using a named <see cref="HttpClient"/>. This allows you to configure the HTTP client
    /// separately, including policies like retry, timeout, and authentication.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// builder.Services.AddHttpClient("gateway", client =>
    /// {
    ///     client.BaseAddress = new Uri("https://gateway:5001");
    ///     client.Timeout = TimeSpan.FromSeconds(30);
    /// });
    ///
    /// builder.Services.AddConversationsChatMessageStoreWithHttpClient("gateway");
    /// </code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddConversationsChatMessageStoreWithHttpClient(
        this IServiceCollection services,
        string httpClientName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(httpClientName);

        services.AddSingleton<Func<string, ChatMessageStore>>(sp =>
        {
            IHttpClientFactory httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            return conversationId =>
            {
                HttpClient httpClient = httpClientFactory.CreateClient(httpClientName);

                ConversationsApiClient apiClient = new(httpClient);

                return new ConversationsChatMessageStore(apiClient, conversationId);
            };
        });

        return services;
    }
}
