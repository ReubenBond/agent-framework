// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
using Microsoft.Extensions.AI;

namespace AgentGateway.UnitTests.Conversations;

/// <summary>
/// Tests for InMemoryConversationStorage implementation.
/// </summary>
public class InMemoryConversationStorageTests
{
    private readonly InMemoryConversationStorage _storage;

    public InMemoryConversationStorageTests()
    {
        this._storage = new InMemoryConversationStorage();
    }

    #region Conversation Tests

    [Fact]
    public async Task CreateConversationAsync_ShouldCreateConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");

        // Act
        var result = await this._storage.CreateConversationAsync(conversation);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(conversation.Id);
        result.CreatedAt.Should().Be(conversation.CreatedAt);
    }

    [Fact]
    public async Task CreateConversationAsync_WithDuplicateId_ShouldThrowAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._storage.CreateConversationAsync(conversation));
    }

    [Fact]
    public async Task GetConversationAsync_ExistingConversation_ShouldReturnConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        // Act
        var result = await this._storage.GetConversationAsync("conv-1");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("conv-1");
    }

    [Fact]
    public async Task GetConversationAsync_NonExistentConversation_ShouldReturnNullAsync()
    {
        // Act
        var result = await this._storage.GetConversationAsync("non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateConversationAsync_ExistingConversation_ShouldUpdateConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        var updatedMetadata = new Dictionary<string, string> { ["key"] = "value" };
        var updatedConversation = conversation with { Metadata = updatedMetadata };

        // Act
        var result = await this._storage.UpdateConversationAsync(updatedConversation);

        // Assert
        result.Should().NotBeNull();
        result!.Metadata.Should().ContainKey("key");

        var retrieved = await this._storage.GetConversationAsync("conv-1");
        retrieved!.Metadata.Should().ContainKey("key");
    }

    [Fact]
    public async Task UpdateConversationAsync_NonExistentConversation_ShouldReturnNullAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");

        // Act
        var result = await this._storage.UpdateConversationAsync(conversation);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteConversationAsync_ExistingConversation_ShouldDeleteConversationAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        // Act
        var result = await this._storage.DeleteConversationAsync("conv-1");

        // Assert
        result.Should().BeTrue();

        var retrieved = await this._storage.GetConversationAsync("conv-1");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteConversationAsync_NonExistentConversation_ShouldReturnFalseAsync()
    {
        // Act
        var result = await this._storage.DeleteConversationAsync("non-existent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteConversationAsync_ShouldDeleteAllMessagesAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        var message = CreateTestMessage("conv-1", "msg-1");
        await this._storage.AddItemAsync("conv-1", message);

        // Act
        await this._storage.DeleteConversationAsync("conv-1");

        // Assert
        var retrieved = await this._storage.GetItemAsync("conv-1", "msg-1");
        retrieved.Should().BeNull();
    }

    #endregion

    #region Item Tests

    [Fact]
    public async Task AddItemAsync_ShouldAddItemAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        var message = CreateTestMessage("conv-1", "msg-1");

        // Act
        var result = await this._storage.AddItemAsync("conv-1", message);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("msg-1");
    }

    [Fact]
    public async Task AddItemAsync_ToNonExistentConversation_ShouldThrowAsync()
    {
        // Arrange
        var message = CreateTestMessage("non-existent", "msg-1");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._storage.AddItemAsync("non-existent", message));
    }

    [Fact]
    public async Task AddItemAsync_WithDuplicateId_ShouldThrowAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        var message = CreateTestMessage("conv-1", "msg-1");
        await this._storage.AddItemAsync("conv-1", message);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this._storage.AddItemAsync("conv-1", message));
    }

    [Fact]
    public async Task GetItemAsync_ExistingItem_ShouldReturnItemAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        var message = CreateTestMessage("conv-1", "msg-1");
        await this._storage.AddItemAsync("conv-1", message);

        // Act
        var result = await this._storage.GetItemAsync("conv-1", "msg-1");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("msg-1");
    }

    [Fact]
    public async Task GetItemAsync_NonExistentItem_ShouldReturnNullAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        // Act
        var result = await this._storage.GetItemAsync("conv-1", "non-existent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListItemsAsync_ShouldReturnItemsInDescendingOrderAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        var msg1 = CreateTestMessage("conv-1", "msg-1", createdAt: 1000);
        var msg2 = CreateTestMessage("conv-1", "msg-2", createdAt: 2000);
        var msg3 = CreateTestMessage("conv-1", "msg-3", createdAt: 3000);

        await this._storage.AddItemAsync("conv-1", msg1);
        await this._storage.AddItemAsync("conv-1", msg2);
        await this._storage.AddItemAsync("conv-1", msg3);

        // Act
        var result = await this._storage.ListItemsAsync("conv-1", limit: 10, order: SortOrder.Descending);

        // Assert
        result.Data.Should().HaveCount(3);
        result.Data[0].Id.Should().Be("msg-3");
        result.Data[1].Id.Should().Be("msg-2");
        result.Data[2].Id.Should().Be("msg-1");
    }

    [Fact]
    public async Task ListItemsAsync_ShouldReturnItemsInAscendingOrderAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        var msg1 = CreateTestMessage("conv-1", "msg-1", createdAt: 1000);
        var msg2 = CreateTestMessage("conv-1", "msg-2", createdAt: 2000);
        var msg3 = CreateTestMessage("conv-1", "msg-3", createdAt: 3000);

        await this._storage.AddItemAsync("conv-1", msg1);
        await this._storage.AddItemAsync("conv-1", msg2);
        await this._storage.AddItemAsync("conv-1", msg3);

        // Act
        var result = await this._storage.ListItemsAsync("conv-1", limit: 10, order: SortOrder.Ascending);

        // Assert
        result.Data.Should().HaveCount(3);
        result.Data[0].Id.Should().Be("msg-1");
        result.Data[1].Id.Should().Be("msg-2");
        result.Data[2].Id.Should().Be("msg-3");
    }

    [Fact]
    public async Task ListItemsAsync_WithLimit_ShouldReturnLimitedResultsAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        for (int i = 0; i < 5; i++)
        {
            await this._storage.AddItemAsync("conv-1", CreateTestMessage("conv-1", $"msg-{i}", createdAt: i));
        }

        // Act
        var result = await this._storage.ListItemsAsync("conv-1", limit: 3);

        // Assert
        result.Data.Should().HaveCount(3);
        result.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteItemAsync_ExistingItem_ShouldDeleteItemAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        var message = CreateTestMessage("conv-1", "msg-1");
        await this._storage.AddItemAsync("conv-1", message);

        // Act
        var result = await this._storage.DeleteItemAsync("conv-1", "msg-1");

        // Assert
        result.Should().BeTrue();

        var retrieved = await this._storage.GetItemAsync("conv-1", "msg-1");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteItemAsync_NonExistentItem_ShouldReturnFalseAsync()
    {
        // Arrange
        var conversation = CreateTestConversation("conv-1");
        await this._storage.CreateConversationAsync(conversation);

        // Act
        var result = await this._storage.DeleteItemAsync("conv-1", "non-existent");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static Conversation CreateTestConversation(string id, long createdAt = 1000)
    {
        return new Conversation
        {
            Id = id,
            CreatedAt = createdAt,
            Metadata = new Dictionary<string, string>()
        };
    }

    private static ResponsesUserMessageItemResource CreateTestMessage(string conversationId, string id, long createdAt = 1000)
    {
        return new ResponsesUserMessageItemResource
        {
            Id = id,
            Content = new List<ItemContent>
            {
                new ItemContentInputText { Text = "Test message" }
            },
            Status = ResponsesMessageItemResourceStatus.Completed
        };
    }

    #endregion
}
