// Copyright (c) Microsoft. All rights reserved.

using AgentGateway.Conversations;

namespace AgentGateway.UnitTests.Conversations;

/// <summary>
/// Tests for OrleansAgentConversationIndex implementation.
/// </summary>
[Collection(OrleansClusterCollection.Name)]
public class OrleansAgentConversationIndexTests
{
    private readonly OrleansTestClusterFixture _fixture;
    private IGrainFactory GrainFactory => this._fixture.GrainFactory;

    public OrleansAgentConversationIndexTests(OrleansTestClusterFixture fixture)
    {
        this._fixture = fixture;
    }

    private static string GetUniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    [Fact]
    public async Task AddConversationAsync_ShouldAddConversationAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act
        await index.AddConversationAsync(agentId, conversationId);

        // Assert
        var conversationIds = await index.GetConversationIdsAsync(agentId);
        conversationIds.Should().Contain(conversationId);
    }

    [Fact]
    public async Task AddConversationAsync_WithNullAgentId_ShouldThrowAsync()
    {
        // Arrange
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await index.AddConversationAsync(null!, conversationId));
    }

    [Fact]
    public async Task AddConversationAsync_WithEmptyAgentId_ShouldThrowAsync()
    {
        // Arrange
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await index.AddConversationAsync(string.Empty, conversationId));
    }

    [Fact]
    public async Task AddConversationAsync_WithNullConversationId_ShouldThrowAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await index.AddConversationAsync(agentId, null!));
    }

    [Fact]
    public async Task AddConversationAsync_WithEmptyConversationId_ShouldThrowAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await index.AddConversationAsync(agentId, string.Empty));
    }

    [Fact]
    public async Task AddConversationAsync_ShouldAddToCorrectAgentAsync()
    {
        // Arrange
        var agentId1 = GetUniqueId("agent");
        var agentId2 = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act
        await index.AddConversationAsync(agentId1, conversationId1);
        await index.AddConversationAsync(agentId2, conversationId2);

        // Assert
        var conversationIds1 = await index.GetConversationIdsAsync(agentId1);
        var conversationIds2 = await index.GetConversationIdsAsync(agentId2);

        conversationIds1.Should().Contain(conversationId1);
        conversationIds1.Should().NotContain(conversationId2);

        conversationIds2.Should().Contain(conversationId2);
        conversationIds2.Should().NotContain(conversationId1);
    }

    [Fact]
    public async Task RemoveConversationAsync_ShouldRemoveConversationAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);
        await index.AddConversationAsync(agentId, conversationId);

        // Act
        await index.RemoveConversationAsync(agentId, conversationId);

        // Assert
        var conversationIds = await index.GetConversationIdsAsync(agentId);
        conversationIds.Should().NotContain(conversationId);
    }

    [Fact]
    public async Task RemoveConversationAsync_WithNullAgentId_ShouldThrowAsync()
    {
        // Arrange
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await index.RemoveConversationAsync(null!, conversationId));
    }

    [Fact]
    public async Task RemoveConversationAsync_WithEmptyAgentId_ShouldThrowAsync()
    {
        // Arrange
        var conversationId = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await index.RemoveConversationAsync(string.Empty, conversationId));
    }

    [Fact]
    public async Task RemoveConversationAsync_WithNullConversationId_ShouldThrowAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await index.RemoveConversationAsync(agentId, null!));
    }

    [Fact]
    public async Task RemoveConversationAsync_WithEmptyConversationId_ShouldThrowAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await index.RemoveConversationAsync(agentId, string.Empty));
    }

    [Fact]
    public async Task GetConversationIdsAsync_WhenEmpty_ShouldReturnEmptyListAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act
        var conversationIds = await index.GetConversationIdsAsync(agentId);

        // Assert
        conversationIds.Should().NotBeNull();
        conversationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConversationIdsAsync_WithNullAgentId_ShouldThrowAsync()
    {
        // Arrange
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await index.GetConversationIdsAsync(null!));
    }

    [Fact]
    public async Task GetConversationIdsAsync_WithEmptyAgentId_ShouldThrowAsync()
    {
        // Arrange
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await index.GetConversationIdsAsync(string.Empty));
    }

    [Fact]
    public async Task GetConversationIdsAsync_ShouldReturnAllConversationsForAgentAsync()
    {
        // Arrange
        var agentId = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var conversationId3 = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        await index.AddConversationAsync(agentId, conversationId1);
        await index.AddConversationAsync(agentId, conversationId2);
        await index.AddConversationAsync(agentId, conversationId3);

        // Act
        var conversationIds = await index.GetConversationIdsAsync(agentId);

        // Assert
        conversationIds.Should().HaveCount(3);
        conversationIds.Should().Contain(conversationId1);
        conversationIds.Should().Contain(conversationId2);
        conversationIds.Should().Contain(conversationId3);
    }

    [Fact]
    public async Task MultipleAgents_ShouldMaintainSeparateIndexesAsync()
    {
        // Arrange
        var agentId1 = GetUniqueId("agent");
        var agentId2 = GetUniqueId("agent");
        var agentId3 = GetUniqueId("agent");
        var conversationId1 = GetUniqueId("conv");
        var conversationId2 = GetUniqueId("conv");
        var conversationId3 = GetUniqueId("conv");
        var index = new OrleansAgentConversationIndex(this.GrainFactory);

        // Act
        await index.AddConversationAsync(agentId1, conversationId1);
        await index.AddConversationAsync(agentId2, conversationId2);
        await index.AddConversationAsync(agentId3, conversationId3);

        // Assert
        var conversationIds1 = await index.GetConversationIdsAsync(agentId1);
        var conversationIds2 = await index.GetConversationIdsAsync(agentId2);
        var conversationIds3 = await index.GetConversationIdsAsync(agentId3);

        conversationIds1.Should().ContainSingle().Which.Should().Be(conversationId1);
        conversationIds2.Should().ContainSingle().Which.Should().Be(conversationId2);
        conversationIds3.Should().ContainSingle().Which.Should().Be(conversationId3);
    }
}
