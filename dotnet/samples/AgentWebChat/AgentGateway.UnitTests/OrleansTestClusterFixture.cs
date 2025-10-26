// Copyright (c) Microsoft. All rights reserved.

using AgentContracts;
using Microsoft.Agents.AI.Hosting.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orleans.Serialization;
using Orleans.Storage;
using Orleans.TestingHost;

namespace AgentGateway.UnitTests;

/// <summary>
/// xUnit collection fixture for Orleans test cluster.
/// This ensures a single cluster instance is shared across all tests in the collection.
/// </summary>
public sealed class OrleansTestClusterFixture : IAsyncLifetime
{
    /// <summary>
    /// Gets the Orleans test cluster instance.
    /// </summary>
    public InProcessTestCluster Cluster { get; private set; } = null!;

    /// <summary>
    /// Gets the grain factory for creating grain references.
    /// </summary>
    public IGrainFactory GrainFactory => this.Cluster.Client;

    /// <summary>
    /// Gets the mock IChatClient instance shared across all tests.
    /// Reset this mock between tests using ResetChatClientMock().
    /// </summary>
    public Mock<IChatClient> ChatClientMock { get; private set; } = null!;

    /// <summary>
    /// Resets the mock IChatClient to clear any previous setups or verifications.
    /// Call this in test constructors or setup methods to ensure test isolation.
    /// </summary>
    public void ResetChatClientMock()
    {
        this.ChatClientMock.Reset();
    }

    /// <summary>
    /// Sets up a default chat response for the mock IChatClient.
    /// This provides a basic streaming response that will satisfy most tests.
    /// </summary>
    public void SetupDefaultChatResponse()
    {
        this.ChatClientMock
            .Setup(x => x.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((IEnumerable<ChatMessage> messages, ChatOptions options, CancellationToken ct) => GetTestStreamingResponseAsync());
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> GetTestStreamingResponseAsync()
    {
        // Yield a text update
        yield return new ChatResponseUpdate
        {
            Contents = [new TextContent("This is a test response.")],
            Role = ChatRole.Assistant
        };

        // Yield a final update with usage details
        yield return new ChatResponseUpdate
        {
            Contents = [new UsageContent(new UsageDetails
            {
                InputTokenCount = 10,
                OutputTokenCount = 5,
                TotalTokenCount = 15
            })],
            FinishReason = ChatFinishReason.Stop
        };

        await Task.CompletedTask;
    }

    public async Task InitializeAsync()
    {
        // Create the shared mock IChatClient
        this.ChatClientMock = new Mock<IChatClient>();

        var builder = new InProcessTestClusterBuilder();
        builder.ConfigureHost(hostBuilder =>
        {
            // Configure System.Text.Json serialization for all Microsoft.Agents.* and AgentGateway types
            // This uses AgentGatewayJsonUtilities which chains together all the necessary type resolvers
            // including OpenAIJsonUtilities (OpenAI Hosting types), AIJsonUtilities (Microsoft.Extensions.AI), and grain states
            hostBuilder.Services.AddSerializer(serializerBuilder =>
            {
                // Support all Microsoft.Agents.*, AgentContracts, and AgentGateway types using AgentGatewayJsonUtilities
                // which includes proper type resolver chaining for:
                // - Grain state types (ConversationState, ResponseState, AgentConversationIndexState)
                // - OpenAI Hosting types (Conversation, ItemResource, Response, etc.)
                // - Microsoft.Extensions.AI types (AIContent, ChatMessage, etc.)
                // - AgentContracts types (via AgentContractsJsonUtilities)
                serializerBuilder.AddJsonSerializer(
                    isSupported: type => type.Namespace?.StartsWith("Microsoft.Agents", StringComparison.Ordinal) == true ||
                                        type.Namespace?.StartsWith("AgentContracts", StringComparison.Ordinal) == true ||
                                        type.Namespace?.StartsWith("AgentGateway", StringComparison.Ordinal) == true,
                    jsonSerializerOptions: AgentGateway.AgentGatewayJsonUtilities.DefaultOptions);
            });
        })
        .ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.UseInMemoryReminderService();

            // Register System.Text.Json-based grain storage serializer
            siloBuilder.Services.AddSingleton<IGrainStorageSerializer>(sp =>
                new AgentGateway.Utilities.SystemTextJsonGrainStorageSerializer(AgentGateway.AgentGatewayJsonUtilities.DefaultOptions));

            // Register the shared mock IChatClient for testing
            siloBuilder.Services.AddSingleton(_ => this.ChatClientMock.Object);
        });
        this.Cluster = builder.Build();
        await this.Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (this.Cluster != null)
        {
            await this.Cluster.StopAllSilosAsync();
            this.Cluster.Dispose();
        }
    }
}

/// <summary>
/// xUnit collection definition for Orleans test cluster.
/// All test classes that need an Orleans cluster should use this collection.
/// </summary>
[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "This is the standard xUnit collection pattern")]
public sealed class OrleansClusterCollection : ICollectionFixture<OrleansTestClusterFixture>
{
    public const string Name = nameof(OrleansClusterCollection);
}
