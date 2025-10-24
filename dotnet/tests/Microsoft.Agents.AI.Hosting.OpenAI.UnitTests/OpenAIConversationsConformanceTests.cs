// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Hosting.OpenAI.Conversations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Agents.AI.Hosting.OpenAI.UnitTests;

/// <summary>
/// Conformance tests for OpenAI Conversations API implementation behavior.
/// Tests use real API traces to ensure our implementation produces responses
/// that match OpenAI's wire format when processing actual requests through the server.
/// For pure serialization/deserialization tests, see OpenAIConversationsSerializationTests.
/// </summary>
public sealed class OpenAIConversationsConformanceTests : IAsyncDisposable
{
    private const string TracesBasePath = "ConformanceTraces/Conversations";
    private WebApplication? _app;
    private HttpClient? _httpClient;

    /// <summary>
    /// Loads a JSON file from the conformance traces directory.
    /// </summary>
    private static string LoadTraceFile(string relativePath)
    {
        var fullPath = System.IO.Path.Combine(TracesBasePath, relativePath);

        if (!System.IO.File.Exists(fullPath))
        {
            throw new System.IO.FileNotFoundException($"Conformance trace file not found: {fullPath}");
        }

        return System.IO.File.ReadAllText(fullPath);
    }

    /// <summary>
    /// Loads a JSON document from the conformance traces directory.
    /// </summary>
    private static JsonDocument LoadTraceDocument(string relativePath)
    {
        var json = LoadTraceFile(relativePath);
        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Asserts that a JSON element exists (property is present, value can be null).
    /// </summary>
    private static void AssertJsonPropertyExists(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out _))
        {
            throw new Xunit.Sdk.XunitException($"Expected property '{propertyName}' not found in JSON");
        }
    }

    /// <summary>
    /// Asserts that a JSON element has a specific string value.
    /// </summary>
    private static void AssertJsonPropertyEquals(JsonElement element, string propertyName, string expectedValue)
    {
        AssertJsonPropertyExists(element, propertyName);
        var actualValue = element.GetProperty(propertyName).GetString();

        if (actualValue != expectedValue)
        {
            throw new Xunit.Sdk.XunitException($"Property '{propertyName}': expected '{expectedValue}', got '{actualValue}'");
        }
    }

    /// <summary>
    /// Asserts that a JSON element has a specific boolean value.
    /// </summary>
    private static void AssertJsonPropertyEquals(JsonElement element, string propertyName, bool expectedValue)
    {
        AssertJsonPropertyExists(element, propertyName);
        var actualValue = element.GetProperty(propertyName).GetBoolean();

        if (actualValue != expectedValue)
        {
            throw new Xunit.Sdk.XunitException($"Property '{propertyName}': expected {expectedValue}, got {actualValue}");
        }
    }

    /// <summary>
    /// Creates a test server with Conversations API.
    /// </summary>
    private async Task<HttpClient> CreateTestServerAsync(string agentName, string instructions, string responseText)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        IChatClient mockChatClient = new TestHelpers.SimpleMockChatClient(responseText);
        builder.Services.AddKeyedSingleton("chat-client", mockChatClient);
        builder.AddAIAgent(agentName, instructions, chatClientServiceKey: "chat-client");
        builder.Services.AddInMemoryConversationStorage();
        builder.AddOpenAIResponses();

        this._app = builder.Build();
        AIAgent agent = this._app.Services.GetRequiredKeyedService<AIAgent>(agentName);
        this._app.MapConversations();
        this._app.MapOpenAIResponses(agent);

        await this._app.StartAsync();

        TestServer testServer = this._app.Services.GetRequiredService<IServer>() as TestServer
            ?? throw new InvalidOperationException("TestServer not found");

        this._httpClient = testServer.CreateClient();
        return this._httpClient;
    }

    /// <summary>
    /// Creates a test server with a stateful mock that returns different responses for each call.
    /// </summary>
    private async Task<HttpClient> CreateTestServerWithStatefulMockAsync(string agentName, string instructions, string[] responseTexts)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        IChatClient mockChatClient = new TestHelpers.StatefulMockChatClient(responseTexts);
        builder.Services.AddKeyedSingleton("chat-client", mockChatClient);
        builder.AddAIAgent(agentName, instructions, chatClientServiceKey: "chat-client");
        builder.Services.AddInMemoryConversationStorage();
        builder.AddOpenAIResponses();

        this._app = builder.Build();
        AIAgent agent = this._app.Services.GetRequiredKeyedService<AIAgent>(agentName);
        this._app.MapConversations();
        this._app.MapOpenAIResponses(agent);

        await this._app.StartAsync();

        TestServer testServer = this._app.Services.GetRequiredService<IServer>() as TestServer
            ?? throw new InvalidOperationException("TestServer not found");

        this._httpClient = testServer.CreateClient();
        return this._httpClient;
    }

    /// <summary>
    /// Sends a POST request with JSON content to the test server.
    /// </summary>
    private static async Task<HttpResponseMessage> SendPostRequestAsync(HttpClient client, string path, string requestJson)
    {
        StringContent content = new(requestJson, Encoding.UTF8, "application/json");
        return await client.PostAsync(new Uri(path, UriKind.Relative), content);
    }

    /// <summary>
    /// Sends a GET request to the test server.
    /// </summary>
    private static async Task<HttpResponseMessage> SendGetRequestAsync(HttpClient client, string path)
    {
        return await client.GetAsync(new Uri(path, UriKind.Relative));
    }

    /// <summary>
    /// Sends a DELETE request to the test server.
    /// </summary>
    private static async Task<HttpResponseMessage> SendDeleteRequestAsync(HttpClient client, string path)
    {
        return await client.DeleteAsync(new Uri(path, UriKind.Relative));
    }

    /// <summary>
    /// Parses the response JSON and returns a JsonDocument.
    /// </summary>
    private static async Task<JsonDocument> ParseResponseAsync(HttpResponseMessage response)
    {
        string responseJson = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(responseJson);
    }

    [Fact]
    public async Task BasicConversationCreateAsync()
    {
        // Arrange
        string requestJson = LoadTraceFile("basic/create_conversation_request.json");
        using var expectedResponseDoc = LoadTraceDocument("basic/create_conversation_response.json");
        var expectedResponse = expectedResponseDoc.RootElement;

        HttpClient client = await this.CreateTestServerAsync("basic-agent", "You are a helpful assistant.", "The capital of France is Paris.");

        // Act
        HttpResponseMessage httpResponse = await SendPostRequestAsync(client, "/v1/conversations", requestJson);
        using var responseDoc = await ParseResponseAsync(httpResponse);
        var response = responseDoc.RootElement;

        // Parse the request
        using var requestDoc = JsonDocument.Parse(requestJson);
        var request = requestDoc.RootElement;

        // Assert - Request has metadata
        AssertJsonPropertyExists(request, "metadata");
        var requestMetadata = request.GetProperty("metadata");
        Assert.Equal(JsonValueKind.Object, requestMetadata.ValueKind);

        // Assert - Response metadata
        AssertJsonPropertyExists(response, "id");
        AssertJsonPropertyEquals(response, "object", "conversation");
        AssertJsonPropertyExists(response, "created_at");
        var id = response.GetProperty("id").GetString();
        Assert.NotNull(id);
        Assert.StartsWith("conv_", id);
        var createdAt = response.GetProperty("created_at").GetInt64();
        Assert.True(createdAt > 0, "created_at should be a positive unix timestamp");

        // Assert - Response preserves metadata
        AssertJsonPropertyExists(response, "metadata");
        var responseMetadata = response.GetProperty("metadata");
        Assert.Equal(JsonValueKind.Object, responseMetadata.ValueKind);
    }

    [Fact]
    public async Task BasicConversationWithMessagesAsync()
    {
        // Arrange
        string createRequestJson = LoadTraceFile("basic/create_conversation_request.json");
        string firstMessageRequestJson = LoadTraceFile("basic/first_message_request.json");
        string secondMessageRequestJson = LoadTraceFile("basic/second_message_request.json");
        using var firstMessageExpectedDoc = LoadTraceDocument("basic/first_message_response.json");
        using var secondMessageExpectedDoc = LoadTraceDocument("basic/second_message_response.json");

        // Get expected response texts
        string firstExpectedText = firstMessageExpectedDoc.RootElement.GetProperty("output")[0]
            .GetProperty("content")[0]
            .GetProperty("text").GetString()!;
        string secondExpectedText = secondMessageExpectedDoc.RootElement.GetProperty("output")[0]
            .GetProperty("content")[0]
            .GetProperty("text").GetString()!;

        // Create a stateful mock that returns different responses for each call
        HttpClient client = await this.CreateTestServerWithStatefulMockAsync(
            "basic-agent",
            "You are a helpful assistant.",
            [firstExpectedText, secondExpectedText]);

        // Act - Create conversation
        HttpResponseMessage createResponse = await SendPostRequestAsync(client, "/v1/conversations", createRequestJson);
        using var createDoc = await ParseResponseAsync(createResponse);
        var conversation = createDoc.RootElement;
        string conversationId = conversation.GetProperty("id").GetString()!;

        // Act - Send first message (using Responses API with conversation parameter)
        // Note: Update the request JSON with the actual conversation ID
        using var firstMsgDoc = JsonDocument.Parse(firstMessageRequestJson);
        var firstMsgRequest = JsonSerializer.Serialize(new
        {
            model = firstMsgDoc.RootElement.GetProperty("model").GetString(),
            conversation = conversationId,
            input = firstMsgDoc.RootElement.GetProperty("input").GetString(),
            max_output_tokens = firstMsgDoc.RootElement.GetProperty("max_output_tokens").GetInt32()
        });

        HttpResponseMessage firstMsgResponse = await SendPostRequestAsync(client, "/basic-agent/v1/responses", firstMsgRequest);
        using var firstMsgResponseDoc = await ParseResponseAsync(firstMsgResponse);
        var firstResponse = firstMsgResponseDoc.RootElement;

        // Assert - First response has conversation reference
        AssertJsonPropertyExists(firstResponse, "conversation");
        var conversationRef = firstResponse.GetProperty("conversation");

        // The conversation reference can be either a string (just the ID) or an object with an id property
        if (conversationRef.ValueKind == JsonValueKind.String)
        {
            var refId = conversationRef.GetString();
            Assert.Equal(conversationId, refId);
        }
        else if (conversationRef.ValueKind == JsonValueKind.Object)
        {
            AssertJsonPropertyEquals(conversationRef, "id", conversationId);
        }
        else
        {
            throw new Xunit.Sdk.XunitException($"Expected conversation to be either a string or an object, but got {conversationRef.ValueKind}");
        }

        // Assert - First response has output
        AssertJsonPropertyExists(firstResponse, "output");
        var firstOutput = firstResponse.GetProperty("output");
        Assert.True(firstOutput.GetArrayLength() > 0);

        // Assert - First response status is completed
        AssertJsonPropertyEquals(firstResponse, "status", "completed");

        // Act - Send second message
        using var secondMsgDoc = JsonDocument.Parse(secondMessageRequestJson);
        var secondMsgRequest = JsonSerializer.Serialize(new
        {
            model = secondMsgDoc.RootElement.GetProperty("model").GetString(),
            conversation = conversationId,
            input = secondMsgDoc.RootElement.GetProperty("input").GetString(),
            max_output_tokens = secondMsgDoc.RootElement.GetProperty("max_output_tokens").GetInt32()
        });

        HttpResponseMessage secondMsgResponse = await SendPostRequestAsync(client, "/basic-agent/v1/responses", secondMsgRequest);
        using var secondMsgResponseDoc = await ParseResponseAsync(secondMsgResponse);
        var secondResponse = secondMsgResponseDoc.RootElement;

        // Assert - Second response has conversation reference
        AssertJsonPropertyExists(secondResponse, "conversation");
        var secondConversationRef = secondResponse.GetProperty("conversation");

        if (secondConversationRef.ValueKind == JsonValueKind.String)
        {
            var refId = secondConversationRef.GetString();
            Assert.Equal(conversationId, refId);
        }
        else if (secondConversationRef.ValueKind == JsonValueKind.Object)
        {
            AssertJsonPropertyEquals(secondConversationRef, "id", conversationId);
        }
        else
        {
            throw new Xunit.Sdk.XunitException($"Expected conversation to be either a string or an object, but got {secondConversationRef.ValueKind}");
        }

        // Assert - Second response has output
        AssertJsonPropertyExists(secondResponse, "output");
        var secondOutput = secondResponse.GetProperty("output");
        Assert.True(secondOutput.GetArrayLength() > 0);

        // Assert - Second response status is completed
        AssertJsonPropertyEquals(secondResponse, "status", "completed");
    }

    [Fact]
    public async Task CreateConversationWithItemsAsync()
    {
        // Arrange
        string requestJson = LoadTraceFile("create_with_items/create_request.json");
        using var expectedResponseDoc = LoadTraceDocument("create_with_items/create_response.json");

        HttpClient client = await this.CreateTestServerAsync("items-agent", "You are a helpful assistant.", "Test response");

        // Act
        HttpResponseMessage httpResponse = await SendPostRequestAsync(client, "/v1/conversations", requestJson);
        using var responseDoc = await ParseResponseAsync(httpResponse);
        var response = responseDoc.RootElement;

        // Parse the request
        using var requestDoc = JsonDocument.Parse(requestJson);
        var request = requestDoc.RootElement;

        // Assert - Request has items array
        AssertJsonPropertyExists(request, "items");
        var requestItems = request.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, requestItems.ValueKind);
        Assert.True(requestItems.GetArrayLength() > 0);

        // Assert - Response has conversation structure
        AssertJsonPropertyExists(response, "id");
        AssertJsonPropertyEquals(response, "object", "conversation");
        AssertJsonPropertyExists(response, "created_at");
        AssertJsonPropertyExists(response, "metadata");
    }

    [Fact]
    public async Task AddItemsToConversationAsync()
    {
        // Arrange
        string createRequestJson = LoadTraceFile("basic/create_conversation_request.json");
        string addItemsRequestJson = LoadTraceFile("add_items/request.json");
        using var expectedResponseDoc = LoadTraceDocument("add_items/response.json");

        HttpClient client = await this.CreateTestServerAsync("add-items-agent", "You are a helpful assistant.", "Test response");

        // Act - Create conversation first
        HttpResponseMessage createResponse = await SendPostRequestAsync(client, "/v1/conversations", createRequestJson);
        using var createDoc = await ParseResponseAsync(createResponse);
        var conversation = createDoc.RootElement;
        string conversationId = conversation.GetProperty("id").GetString()!;

        // Act - Add items
        HttpResponseMessage addItemsResponse = await SendPostRequestAsync(client, $"/v1/conversations/{conversationId}/items", addItemsRequestJson);
        using var addItemsDoc = await ParseResponseAsync(addItemsResponse);
        var response = addItemsDoc.RootElement;

        // Parse the request
        using var requestDoc = JsonDocument.Parse(addItemsRequestJson);
        var request = requestDoc.RootElement;

        // Assert - Request has items array
        AssertJsonPropertyExists(request, "items");
        var requestItems = request.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, requestItems.ValueKind);
        var itemCount = requestItems.GetArrayLength();
        Assert.True(itemCount > 0);

        // Assert - Response has data array with created items
        AssertJsonPropertyExists(response, "data");
        var responseData = response.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, responseData.ValueKind);
        Assert.Equal(itemCount, responseData.GetArrayLength());

        // Assert - Each item has required fields
        foreach (var item in responseData.EnumerateArray())
        {
            AssertJsonPropertyExists(item, "id");
            AssertJsonPropertyEquals(item, "type", "message");
            AssertJsonPropertyExists(item, "content");
            AssertJsonPropertyExists(item, "role");
            var itemId = item.GetProperty("id").GetString();
            Assert.NotNull(itemId);
            Assert.StartsWith("msg_", itemId);
        }
    }

    [Fact]
    public async Task ListItemsInConversationAsync()
    {
        // Arrange
        string createRequestJson = LoadTraceFile("basic/create_conversation_request.json");
        using var expectedResponseDoc = LoadTraceDocument("list_items/response.json");

        HttpClient client = await this.CreateTestServerAsync("list-items-agent", "You are a helpful assistant.", "The capital of France is Paris.");

        // Act - Create conversation
        HttpResponseMessage createResponse = await SendPostRequestAsync(client, "/v1/conversations", createRequestJson);
        using var createDoc = await ParseResponseAsync(createResponse);
        var conversation = createDoc.RootElement;
        string conversationId = conversation.GetProperty("id").GetString()!;

        // Note: In a real test, we would send messages through the Responses API first
        // For now, we're just testing the list endpoint structure

        // Act - List items
        HttpResponseMessage listResponse = await SendGetRequestAsync(client, $"/v1/conversations/{conversationId}/items");
        using var listDoc = await ParseResponseAsync(listResponse);
        var response = listDoc.RootElement;

        // Assert - Response has list structure
        AssertJsonPropertyEquals(response, "object", "list");
        AssertJsonPropertyExists(response, "data");
        AssertJsonPropertyExists(response, "first_id");
        AssertJsonPropertyExists(response, "last_id");
        AssertJsonPropertyExists(response, "has_more");

        var data = response.GetProperty("data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
    }

    [Fact]
    public async Task RetrieveConversationAsync()
    {
        // Arrange
        string createRequestJson = LoadTraceFile("basic/create_conversation_request.json");
        using var expectedResponseDoc = LoadTraceDocument("retrieve_conversation/response.json");

        HttpClient client = await this.CreateTestServerAsync("retrieve-agent", "You are a helpful assistant.", "Test response");

        // Act - Create conversation
        HttpResponseMessage createResponse = await SendPostRequestAsync(client, "/v1/conversations", createRequestJson);
        using var createDoc = await ParseResponseAsync(createResponse);
        var createdConversation = createDoc.RootElement;
        string conversationId = createdConversation.GetProperty("id").GetString()!;

        // Act - Retrieve conversation
        HttpResponseMessage retrieveResponse = await SendGetRequestAsync(client, $"/v1/conversations/{conversationId}");
        using var retrieveDoc = await ParseResponseAsync(retrieveResponse);
        var response = retrieveDoc.RootElement;

        // Assert - Response has conversation structure
        AssertJsonPropertyExists(response, "id");
        AssertJsonPropertyEquals(response, "object", "conversation");
        AssertJsonPropertyExists(response, "created_at");
        AssertJsonPropertyExists(response, "metadata");
        var id = response.GetProperty("id").GetString();
        Assert.Equal(conversationId, id);
    }

    [Fact]
    public async Task RetrieveItemAsync()
    {
        // Arrange
        string createRequestJson = LoadTraceFile("create_with_items/create_request.json");
        using var expectedResponseDoc = LoadTraceDocument("retrieve_item/response.json");

        HttpClient client = await this.CreateTestServerAsync("retrieve-item-agent", "You are a helpful assistant.", "Test response");

        // Act - Create conversation with items
        HttpResponseMessage createResponse = await SendPostRequestAsync(client, "/v1/conversations", createRequestJson);
        using var createDoc = await ParseResponseAsync(createResponse);
        var conversation = createDoc.RootElement;
        string conversationId = conversation.GetProperty("id").GetString()!;

        // Act - List items to get an item ID
        HttpResponseMessage listResponse = await SendGetRequestAsync(client, $"/v1/conversations/{conversationId}/items");
        using var listDoc = await ParseResponseAsync(listResponse);
        var listResult = listDoc.RootElement;
        var items = listResult.GetProperty("data");
        Assert.True(items.GetArrayLength() > 0, "Should have at least one item");
        string itemId = items[0].GetProperty("id").GetString()!;

        // Act - Retrieve specific item
        HttpResponseMessage retrieveResponse = await SendGetRequestAsync(client, $"/v1/conversations/{conversationId}/items/{itemId}");
        using var retrieveDoc = await ParseResponseAsync(retrieveResponse);
        var response = retrieveDoc.RootElement;

        // Assert - Response has item structure
        AssertJsonPropertyExists(response, "id");
        AssertJsonPropertyEquals(response, "type", "message");
        AssertJsonPropertyExists(response, "content");
        AssertJsonPropertyExists(response, "role");
        var id = response.GetProperty("id").GetString();
        Assert.Equal(itemId, id);
    }

    [Fact]
    public async Task UpdateConversationAsync()
    {
        // Arrange
        string createRequestJson = LoadTraceFile("basic/create_conversation_request.json");
        string updateRequestJson = LoadTraceFile("update_conversation/request.json");
        using var expectedResponseDoc = LoadTraceDocument("update_conversation/response.json");

        HttpClient client = await this.CreateTestServerAsync("update-agent", "You are a helpful assistant.", "Test response");

        // Act - Create conversation
        HttpResponseMessage createResponse = await SendPostRequestAsync(client, "/v1/conversations", createRequestJson);
        using var createDoc = await ParseResponseAsync(createResponse);
        var conversation = createDoc.RootElement;
        string conversationId = conversation.GetProperty("id").GetString()!;

        // Act - Update conversation
        HttpResponseMessage updateResponse = await SendPostRequestAsync(client, $"/v1/conversations/{conversationId}", updateRequestJson);
        using var updateDoc = await ParseResponseAsync(updateResponse);
        var response = updateDoc.RootElement;

        // Parse the request
        using var requestDoc = JsonDocument.Parse(updateRequestJson);
        var request = requestDoc.RootElement;

        // Assert - Request has metadata
        AssertJsonPropertyExists(request, "metadata");
        var requestMetadata = request.GetProperty("metadata");

        // Assert - Response preserves updated metadata
        AssertJsonPropertyExists(response, "id");
        AssertJsonPropertyEquals(response, "object", "conversation");
        AssertJsonPropertyExists(response, "metadata");
        var responseMetadata = response.GetProperty("metadata");

        // Verify metadata was updated
        foreach (var prop in requestMetadata.EnumerateObject())
        {
            Assert.True(responseMetadata.TryGetProperty(prop.Name, out var value));
            Assert.Equal(prop.Value.GetString(), value.GetString());
        }
    }

    [Fact]
    public async Task DeleteConversationAsync()
    {
        // Arrange
        string createRequestJson = LoadTraceFile("basic/create_conversation_request.json");
        using var expectedResponseDoc = LoadTraceDocument("delete_conversation/response.json");

        HttpClient client = await this.CreateTestServerAsync("delete-agent", "You are a helpful assistant.", "Test response");

        // Act - Create conversation
        HttpResponseMessage createResponse = await SendPostRequestAsync(client, "/v1/conversations", createRequestJson);
        using var createDoc = await ParseResponseAsync(createResponse);
        var conversation = createDoc.RootElement;
        string conversationId = conversation.GetProperty("id").GetString()!;

        // Act - Delete conversation
        HttpResponseMessage deleteResponse = await SendDeleteRequestAsync(client, $"/v1/conversations/{conversationId}");
        using var deleteDoc = await ParseResponseAsync(deleteResponse);
        var response = deleteDoc.RootElement;

        // Assert - Delete response structure
        AssertJsonPropertyExists(response, "id");
        AssertJsonPropertyEquals(response, "object", "conversation.deleted");
        AssertJsonPropertyEquals(response, "deleted", true);
        var id = response.GetProperty("id").GetString();
        Assert.Equal(conversationId, id);

        // Assert - Conversation is actually deleted
        HttpResponseMessage retrieveResponse = await SendGetRequestAsync(client, $"/v1/conversations/{conversationId}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, retrieveResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteItemAsync()
    {
        // Arrange
        string createRequestJson = LoadTraceFile("create_with_items/create_request.json");
        using var expectedResponseDoc = LoadTraceDocument("delete_item/response.json");

        HttpClient client = await this.CreateTestServerAsync("delete-item-agent", "You are a helpful assistant.", "Test response");

        // Act - Create conversation with items
        HttpResponseMessage createResponse = await SendPostRequestAsync(client, "/v1/conversations", createRequestJson);
        using var createDoc = await ParseResponseAsync(createResponse);
        var conversation = createDoc.RootElement;
        string conversationId = conversation.GetProperty("id").GetString()!;

        // Act - List items to get an item ID
        HttpResponseMessage listResponse = await SendGetRequestAsync(client, $"/v1/conversations/{conversationId}/items");
        using var listDoc = await ParseResponseAsync(listResponse);
        var listResult = listDoc.RootElement;
        var items = listResult.GetProperty("data");
        Assert.True(items.GetArrayLength() > 0, "Should have at least one item");
        string itemId = items[0].GetProperty("id").GetString()!;

        // Act - Delete item
        HttpResponseMessage deleteResponse = await SendDeleteRequestAsync(client, $"/v1/conversations/{conversationId}/items/{itemId}");
        using var deleteDoc = await ParseResponseAsync(deleteResponse);
        var response = deleteDoc.RootElement;

        // Assert - Delete response structure
        AssertJsonPropertyExists(response, "id");
        AssertJsonPropertyEquals(response, "object", "conversation.item.deleted");
        AssertJsonPropertyEquals(response, "deleted", true);
        var id = response.GetProperty("id").GetString();
        Assert.Equal(itemId, id);

        // Assert - Item is actually deleted
        HttpResponseMessage retrieveResponse = await SendGetRequestAsync(client, $"/v1/conversations/{conversationId}/items/{itemId}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, retrieveResponse.StatusCode);
    }

    [Fact]
    public async Task ErrorConversationNotFoundAsync()
    {
        // Arrange
        using var expectedResponseDoc = LoadTraceDocument("error_conversation_not_found/response.json");
        const string nonExistentConversationId = "conv_nonexistent123456789";

        HttpClient client = await this.CreateTestServerAsync("error-agent", "You are a helpful assistant.", "Test response");

        // Act
        HttpResponseMessage response = await SendGetRequestAsync(client, $"/v1/conversations/{nonExistentConversationId}");
        using var responseDoc = await ParseResponseAsync(response);
        var responseJson = responseDoc.RootElement;

        // Assert - Response is 404
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        // Assert - Error response structure
        AssertJsonPropertyExists(responseJson, "error");
        var error = responseJson.GetProperty("error");
        AssertJsonPropertyExists(error, "message");
        AssertJsonPropertyExists(error, "type");
        var errorMessage = error.GetProperty("message").GetString();
        Assert.NotNull(errorMessage);
        Assert.Contains("not found", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ErrorItemNotFoundAsync()
    {
        // Arrange
        string createRequestJson = LoadTraceFile("basic/create_conversation_request.json");
        using var expectedResponseDoc = LoadTraceDocument("error_item_not_found/response.json");
        const string nonExistentItemId = "msg_nonexistent123456789";

        HttpClient client = await this.CreateTestServerAsync("error-item-agent", "You are a helpful assistant.", "Test response");

        // Act - Create conversation
        HttpResponseMessage createResponse = await SendPostRequestAsync(client, "/v1/conversations", createRequestJson);
        using var createDoc = await ParseResponseAsync(createResponse);
        var conversation = createDoc.RootElement;
        string conversationId = conversation.GetProperty("id").GetString()!;

        // Act - Try to retrieve non-existent item
        HttpResponseMessage response = await SendGetRequestAsync(client, $"/v1/conversations/{conversationId}/items/{nonExistentItemId}");
        using var responseDoc = await ParseResponseAsync(response);
        var responseJson = responseDoc.RootElement;

        // Assert - Response is 404
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);

        // Assert - Error response structure
        AssertJsonPropertyExists(responseJson, "error");
        var error = responseJson.GetProperty("error");
        AssertJsonPropertyExists(error, "message");
        AssertJsonPropertyExists(error, "type");
        var errorMessage = error.GetProperty("message").GetString();
        Assert.NotNull(errorMessage);
        Assert.Contains("not found", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ErrorInvalidJsonAsync()
    {
        // Arrange
        string invalidJson = LoadTraceFile("error_invalid_json/request.txt");
        using var expectedResponseDoc = LoadTraceDocument("error_invalid_json/response.json");

        HttpClient client = await this.CreateTestServerAsync("error-json-agent", "You are a helpful assistant.", "Test response");

        // Act
        StringContent content = new(invalidJson, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync(new Uri("/v1/conversations", UriKind.Relative), content);

        // Assert - Response is 400
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);

        // Note: The exact error structure may vary based on ASP.NET Core's JSON parsing error handling
    }

    public async ValueTask DisposeAsync()
    {
        this._httpClient?.Dispose();
        if (this._app != null)
        {
            await this._app.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
