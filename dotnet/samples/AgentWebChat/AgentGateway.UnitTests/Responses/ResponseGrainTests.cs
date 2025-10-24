// Copyright (c) Microsoft. All rights reserved.

using AgentGateway.Responses;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;

namespace AgentGateway.UnitTests.Responses;

/// <summary>
/// Tests for ResponseGrain implementation.
/// </summary>
[Collection(OrleansClusterCollection.Name)]
public class ResponseGrainTests
{
    private readonly OrleansTestClusterFixture _fixture;
    private IGrainFactory GrainFactory => this._fixture.GrainFactory;

    public ResponseGrainTests(OrleansTestClusterFixture fixture)
    {
        this._fixture = fixture;

        // Reset the mock before each test to ensure test isolation
        this._fixture.ResetResponseExecutorMock();

        // Setup default mock response
        this._fixture.SetupDefaultResponseExecutor();
    }

    private static string GetUniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static CreateResponse CreateTestRequest() => new()
    {
        Input = ResponseInput.FromText("Hello, how are you?"),
        Model = "gpt-4",
        Instructions = "You are a helpful assistant."
    };

    [Fact]
    public async Task CreateAsync_ShouldCreateResponseAsync()
    {
        // Arrange
        string responseId = GetUniqueId("resp");
        IResponseGrain grain = this.GrainFactory.GetGrain<IResponseGrain>(responseId);
        CreateResponse request = CreateTestRequest();

        // Act
        Response result = await grain.CreateAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(responseId);
        result.Status.Should().BeDefined();
    }

    [Fact]
    public async Task CreateAsync_WhenAlreadyExists_ShouldThrowAsync()
    {
        // Arrange
        string responseId = GetUniqueId("resp");
        IResponseGrain grain = this.GrainFactory.GetGrain<IResponseGrain>(responseId);
        CreateResponse request = CreateTestRequest();
        await grain.CreateAsync(request, CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => grain.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_WhenExists_ShouldReturnResponseAsync()
    {
        // Arrange
        string responseId = GetUniqueId("resp");
        IResponseGrain grain = this.GrainFactory.GetGrain<IResponseGrain>(responseId);
        CreateResponse request = CreateTestRequest();
        await grain.CreateAsync(request, CancellationToken.None);

        // Act
        Response? result = await grain.GetAsync();

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(responseId);
    }

    [Fact]
    public async Task GetAsync_WithoutCreate_ReturnsNullAsync()
    {
        // Arrange
        string responseId = GetUniqueId("resp");
        IResponseGrain grain = this.GrainFactory.GetGrain<IResponseGrain>(responseId);

        // Act
        Response? result = await grain.GetAsync();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListInputItemsAsync_WhenResponseExists_ShouldReturnItemsAsync()
    {
        // Arrange
        string responseId = GetUniqueId("resp");
        IResponseGrain grain = this.GrainFactory.GetGrain<IResponseGrain>(responseId);
        CreateResponse request = CreateTestRequest();
        await grain.CreateAsync(request, CancellationToken.None);

        // Act
        ListResponse<ItemResource> result = await grain.ListInputItemsAsync(5, "asc", null, null);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task ListInputItemsAsync_WhenDoesNotExist_ShouldReturnEmptyAsync()
    {
        // Arrange
        string responseId = GetUniqueId("resp");
        IResponseGrain grain = this.GrainFactory.GetGrain<IResponseGrain>(responseId);

        // Act
        ListResponse<ItemResource> result = await grain.ListInputItemsAsync(10, "asc", null, null);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task ListInputItemsAsync_WithLimit_ShouldRespectLimitAsync()
    {
        // Arrange
        string responseId = GetUniqueId("resp");
        IResponseGrain grain = this.GrainFactory.GetGrain<IResponseGrain>(responseId);
        CreateResponse request = CreateTestRequest();
        await grain.CreateAsync(request, CancellationToken.None);

        // Act
        ListResponse<ItemResource> result = await grain.ListInputItemsAsync(5, "asc", null, null);

        // Assert
        result.Data.Count.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task ListInputItemsAsync_WithOrder_ShouldApplyOrderAsync()
    {
        // Arrange
        string responseId = GetUniqueId("resp");
        IResponseGrain grain = this.GrainFactory.GetGrain<IResponseGrain>(responseId);
        CreateResponse request = CreateTestRequest();
        await grain.CreateAsync(request, CancellationToken.None);

        // Act
        ListResponse<ItemResource> ascResult = await grain.ListInputItemsAsync(10, "asc", null, null);
        ListResponse<ItemResource> descResult = await grain.ListInputItemsAsync(10, "desc", null, null);

        // Assert
        ascResult.Should().NotBeNull();
        descResult.Should().NotBeNull();
    }
}
