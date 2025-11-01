// Copyright (c) Microsoft. All rights reserved.

using AgentGateway.Conversations;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;

namespace AgentGateway.UnitTests.Conversations;

/// <summary>
/// Tests for AgentConversationIndexGrain implementation.
/// </summary>
[Collection(OrleansClusterCollection.Name)]
public class AgentConversationIndexGrainTests
{
    private readonly OrleansTestClusterFixture _fixture;
    private IGrainFactory GrainFactory => this._fixture.GrainFactory;

    public AgentConversationIndexGrainTests(OrleansTestClusterFixture fixture)
    {
        this._fixture = fixture;
    }

    private static string GetUniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    [Fact]
    public async Task AddConversationAsync_ShouldAddConversationIdAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);

        // Act
        await grain.AddConversationAsync(conversationId);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync();
        conversationIds.Should().Contain(conversationId);
        conversationIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddConversationAsync_WhenCalledMultipleTimes_ShouldNotAddDuplicatesAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);

        // Act
        await grain.AddConversationAsync(conversationId);
        await grain.AddConversationAsync(conversationId);
        await grain.AddConversationAsync(conversationId);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync();
        conversationIds.Should().Contain(conversationId);
        conversationIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task AddConversationAsync_ShouldAddMultipleConversationsAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var conversationId3 = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);

        // Act
        await grain.AddConversationAsync(conversationId1);
        await grain.AddConversationAsync(conversationId2);
        await grain.AddConversationAsync(conversationId3);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync();
        conversationIds.Should().Contain(conversationId1);
        conversationIds.Should().Contain(conversationId2);
        conversationIds.Should().Contain(conversationId3);
        conversationIds.Should().HaveCount(3);
    }

    [Fact]
    public async Task RemoveConversationAsync_ShouldRemoveExistingConversationAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);
        await grain.AddConversationAsync(conversationId);

        // Act
        await grain.RemoveConversationAsync(conversationId);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync();
        conversationIds.Should().NotContain(conversationId);
        conversationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveConversationAsync_WhenConversationDoesNotExist_ShouldNotThrowAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);

        // Act
        await grain.RemoveConversationAsync(conversationId);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync();
        conversationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveConversationAsync_ShouldRemoveOnlySpecifiedConversationAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var conversationId3 = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);
        await grain.AddConversationAsync(conversationId1);
        await grain.AddConversationAsync(conversationId2);
        await grain.AddConversationAsync(conversationId3);

        // Act
        await grain.RemoveConversationAsync(conversationId2);

        // Assert
        var conversationIds = await grain.GetConversationIdsAsync();
        conversationIds.Should().Contain(conversationId1);
        conversationIds.Should().NotContain(conversationId2);
        conversationIds.Should().Contain(conversationId3);
        conversationIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetConversationIdsAsync_WhenEmpty_ShouldReturnEmptyListAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);

        // Act
        var conversationIds = await grain.GetConversationIdsAsync();

        // Assert
        conversationIds.Should().NotBeNull();
        conversationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConversationIdsAsync_ShouldReturnAllConversationsAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var conversationId3 = GetUniqueId("conv");
        var grain = this.GrainFactory.GetGrain<IAgentConversationIndexGrain>(agentId);
        await grain.AddConversationAsync(conversationId1);
        await grain.AddConversationAsync(conversationId2);
        await grain.AddConversationAsync(conversationId3);

        // Act
        var conversationIds = await grain.GetConversationIdsAsync();

        // Assert
        conversationIds.Should().HaveCount(3);
        conversationIds.Should().Contain(conversationId1);
        conversationIds.Should().Contain(conversationId2);
        conversationIds.Should().Contain(conversationId3);
    }
}
