// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Hosting.OpenAI.Conversations.Models;
using Microsoft.Agents.AI.Hosting.OpenAI.Responses.Models;
using AgentGateway.Responses;

namespace AgentGateway.UnitTests.Responses;

/// <summary>
/// Tests for ResponsesService.
/// </summary>
/// <remarks>
/// To configure the mock IChatClient for tests that require LLM responses:
/// <code>
/// this._fixture.ChatClientMock.Setup(x => x.CompleteAsync(...))
///     .ReturnsAsync(new ChatCompletion(...));
/// </code>
/// The mock is automatically reset before each test via ResetChatClientMock() in the constructor.
/// </remarks>
[Collection(OrleansClusterCollection.Name)]
public class ResponsesServiceTests
{
    private readonly OrleansTestClusterFixture _fixture;
    private readonly ResponsesService _service;

    public ResponsesServiceTests(OrleansTestClusterFixture fixture)
    {
        this._fixture = fixture;
        this._service = new ResponsesService(fixture.GrainFactory);

        // Reset the mock before each test to ensure test isolation
        this._fixture.ResetChatClientMock();

        // Setup default mock response
        this._fixture.SetupDefaultChatResponse();
    }

    private static CreateResponse CreateTestRequest() => new()
    {
        Input = ResponseInput.FromText("Hello, how are you?"),
        Model = "gpt-4",
        Instructions = "You are a helpful assistant."
    };

    [Fact]
    public void Constructor_WithValidGrainFactory_Succeeds()
    {
        // Act
        ResponsesService service = new(this._fixture.GrainFactory);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullGrainFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        FluentActions.Invoking(() => new ResponsesService(null!))
            .Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("grainFactory");
    }

    [Fact]
    public async Task CreateResponseAsync_ShouldCreateResponseAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();

        // Act
        Response result = await this._service.CreateResponseAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().StartWith("resp_");
        result.Status.Should().BeDefined();
    }

    [Fact]
    public async Task CreateResponseAsync_MultipleTimes_CreatesUniqueResponsesAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();

        // Act
        Response result1 = await this._service.CreateResponseAsync(request, CancellationToken.None);
        Response result2 = await this._service.CreateResponseAsync(request, CancellationToken.None);

        // Assert
        result1.Id.Should().NotBe(result2.Id);
    }

    [Fact]
    public async Task GetResponseAsync_WhenExists_ShouldReturnResponseAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();
        Response created = await this._service.CreateResponseAsync(request, CancellationToken.None);

        // Act
        Response? result = await this._service.GetResponseAsync(created.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetResponseAsync_WhenDoesNotExist_ShouldReturnNullAsync()
    {
        // Act
        Response? result = await this._service.GetResponseAsync("resp_nonexistent", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListResponseInputItemsAsync_ShouldReturnItemsAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();
        Response created = await this._service.CreateResponseAsync(request, CancellationToken.None);

        // Act
        ListResponse<ItemResource> result = await this._service.ListResponseInputItemsAsync(
            created.Id,
            10,
            "asc",
            null,
            null,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateResponseStreamingAsync_ShouldReturnStreamAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();

        // Act
        IAsyncEnumerable<StreamingResponseEvent> stream = this._service.CreateResponseStreamingAsync(request, CancellationToken.None);

        // Assert
        stream.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateResponseStreamingAsync_ShouldEmitProperEventSequenceAsync()
    {
        // Arrange
        CreateResponse request = CreateTestRequest();

        // Act
        var events = new List<StreamingResponseEvent>();
        await foreach (var evt in this._service.CreateResponseStreamingAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().NotBeEmpty();

        // First event should be response.created
        events[0].Should().BeOfType<StreamingResponseCreated>();
        events[0].Type.Should().Be("response.created");
        events[0].SequenceNumber.Should().Be(1);

        // Last event should be response.completed
        events[^1].Should().BeOfType<StreamingResponseCompleted>();
        events[^1].Type.Should().Be("response.completed");

        // Verify the completed response has output
        var completedEvent = (StreamingResponseCompleted)events[^1];
        completedEvent.Response.Status.Should().Be(ResponseStatus.Completed);
        completedEvent.Response.Output.Should().NotBeEmpty();

        // Sequence numbers should be sequential
        for (int i = 0; i < events.Count; i++)
        {
            events[i].SequenceNumber.Should().Be(i + 1, $"event at index {i} should have sequence number {i + 1}");
        }
    }
}
