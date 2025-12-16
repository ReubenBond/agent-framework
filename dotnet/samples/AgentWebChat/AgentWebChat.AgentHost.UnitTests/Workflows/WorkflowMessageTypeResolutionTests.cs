// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI.Runtime.Abstractions.Workflows;
using AgentWebChat.AgentHost.Workflows;

namespace AgentWebChat.AgentHost.UnitTests.Workflows;

/// <summary>
/// Unit tests for <see cref="WorkflowMessage"/> type serialization and deserialization,
/// particularly focusing on the type resolution mechanism that is critical for HITL workflow routing.
/// </summary>
public sealed class WorkflowMessageTypeResolutionTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region WorkflowMessage.Create Tests

    [Fact]
    public void WorkflowMessage_Create_SetsTypeName_FromActualType()
    {
        // Arrange
        var input = new MarketingContentInput
        {
            Topic = "AI Testing",
            TargetAudience = "Developers",
            Tone = "professional"
        };

        // Act
        var message = WorkflowMessage.Create(input);

        // Assert
        message.Should().NotBeNull();
        message.TypeName.Should().Contain("MarketingContentInput");
        message.TypeName.Should().Contain("AgentWebChat.AgentHost");
    }

    [Fact]
    public void WorkflowMessage_Create_SerializesData_AsJsonElement()
    {
        // Arrange
        var input = new MarketingContentInput
        {
            Topic = "AI Testing",
            TargetAudience = "Developers",
            Tone = "professional"
        };

        // Act
        var message = WorkflowMessage.Create(input);

        // Assert
        message.Data.Should().NotBeNull();
        message.Data.ValueKind.Should().Be(JsonValueKind.Object);
        message.Data.GetProperty("topic").GetString().Should().Be("AI Testing");
        message.Data.GetProperty("targetAudience").GetString().Should().Be("Developers");
        message.Data.GetProperty("tone").GetString().Should().Be("professional");
    }

    [Fact]
    public void WorkflowMessage_Create_WithAnonymousType_SetsTypeName()
    {
        // Arrange
        var input = new { topic = "Test", audience = "All" };

        // Act
        var message = WorkflowMessage.Create(input);

        // Assert
        message.Should().NotBeNull();
        message.TypeName.Should().NotBeNullOrEmpty();
        // Anonymous types have compiler-generated names
        message.TypeName.Should().Contain("<>f__AnonymousType");
    }

    [Fact]
    public void WorkflowMessage_Create_WithDictionary_SetsTypeName()
    {
        // Arrange
        var input = new Dictionary<string, object?>
        {
            ["topic"] = "Test",
            ["audience"] = "All"
        };

        // Act
        var message = WorkflowMessage.Create(input);

        // Assert
        message.Should().NotBeNull();
        message.TypeName.Should().Contain("Dictionary");
    }

    #endregion

    #region Type Deserialization Tests

    [Fact]
    public void MarketingContentInput_CanBeDeserialized_FromJsonElement()
    {
        // Arrange
        var input = new MarketingContentInput
        {
            Topic = "AI Testing",
            TargetAudience = "Developers",
            Tone = "professional"
        };
        var message = WorkflowMessage.Create(input);

        // Act
        var deserialized = message.Data.Deserialize<MarketingContentInput>(s_jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Topic.Should().Be("AI Testing");
        deserialized.TargetAudience.Should().Be("Developers");
        deserialized.Tone.Should().Be("professional");
    }

    [Fact]
    public void MarketingContentInput_CanBeDeserialized_UsingTypeResolve()
    {
        // Arrange
        var input = new MarketingContentInput
        {
            Topic = "AI Testing",
            TargetAudience = "Developers",
            Tone = "professional"
        };
        var message = WorkflowMessage.Create(input);

        // Act - Simulate type resolution like WorkflowHostService.DeserializeInput does
        Type? resolvedType = null;

        // Try Type.GetType first
        resolvedType = Type.GetType(message.TypeName!);

        // If that fails, search all loaded assemblies
        if (resolvedType is null)
        {
            var typeName = message.TypeName!.Split(',')[0].Trim();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolvedType = assembly.GetType(typeName);
                if (resolvedType is not null)
                {
                    break;
                }
            }
        }

        // Assert
        resolvedType.Should().NotBeNull();
        resolvedType.Should().Be(typeof(MarketingContentInput));

        // Verify deserialization works with resolved type
        var deserialized = message.Data.Deserialize(resolvedType!, s_jsonOptions);
        deserialized.Should().BeOfType<MarketingContentInput>();

        var typedResult = (MarketingContentInput)deserialized!;
        typedResult.Topic.Should().Be("AI Testing");
    }

    [Fact]
    public void MarketingContent_CanBeDeserialized_UsingTypeResolve()
    {
        // Arrange
        var content = new MarketingContent
        {
            Content = "Great marketing content here",
            Step = "writer",
            Version = 1,
            QualityScore = 8.5,
            ReviewerNotes = "Well written"
        };
        var message = WorkflowMessage.Create(content);

        // Act - Resolve type
        var typeName = message.TypeName!.Split(',')[0].Trim();
        Type? resolvedType = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            resolvedType = assembly.GetType(typeName);
            if (resolvedType is not null)
            {
                break;
            }
        }

        // Assert
        resolvedType.Should().NotBeNull();
        resolvedType.Should().Be(typeof(MarketingContent));

        var deserialized = message.Data.Deserialize(resolvedType!, s_jsonOptions) as MarketingContent;
        deserialized.Should().NotBeNull();
        deserialized!.Content.Should().Be("Great marketing content here");
        deserialized.Version.Should().Be(1);
    }

    [Fact]
    public void MarketingApprovalRequest_CanBeDeserialized_UsingTypeResolve()
    {
        // Arrange
        var request = new MarketingApprovalRequest
        {
            Content = "Marketing content for approval",
            ReviewerNotes = "Looks good",
            Version = 2,
            Options = ["approve", "revise", "reject"]
        };
        var message = WorkflowMessage.Create(request);

        // Act - Resolve type
        var typeName = message.TypeName!.Split(',')[0].Trim();
        Type? resolvedType = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            resolvedType = assembly.GetType(typeName);
            if (resolvedType is not null)
            {
                break;
            }
        }

        // Assert
        resolvedType.Should().NotBeNull();
        resolvedType.Should().Be(typeof(MarketingApprovalRequest));

        var deserialized = message.Data.Deserialize(resolvedType!, s_jsonOptions) as MarketingApprovalRequest;
        deserialized.Should().NotBeNull();
        deserialized!.Content.Should().Be("Marketing content for approval");
        deserialized.Version.Should().Be(2);
        deserialized.Options.Should().HaveCount(3);
    }

    [Fact]
    public void MarketingApprovalResponse_CanBeDeserialized_UsingTypeResolve()
    {
        // Arrange
        var response = new MarketingApprovalResponse
        {
            Decision = ApprovalDecision.Approve,
            Feedback = "Great work!"
        };
        var message = WorkflowMessage.Create(response);

        // Act - Resolve type
        var typeName = message.TypeName!.Split(',')[0].Trim();
        Type? resolvedType = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            resolvedType = assembly.GetType(typeName);
            if (resolvedType is not null)
            {
                break;
            }
        }

        // Assert
        resolvedType.Should().NotBeNull();
        resolvedType.Should().Be(typeof(MarketingApprovalResponse));

        var deserialized = message.Data.Deserialize(resolvedType!, s_jsonOptions) as MarketingApprovalResponse;
        deserialized.Should().NotBeNull();
        deserialized!.Decision.Should().Be(ApprovalDecision.Approve);
        deserialized.Feedback.Should().Be("Great work!");
    }

    #endregion

    #region Fallback Behavior Tests

    [Fact]
    public void Dictionary_FallbackDeserialization_WorksForUnknownTypes()
    {
        // Arrange - Simulate a message with an unresolvable type name
        var json = JsonSerializer.SerializeToElement(new { topic = "Test", audience = "All" });
        var message = new WorkflowMessage
        {
            TypeName = "NonExistent.Type.That.Cannot.Be.Found",
            Data = json
        };

        // Act - Try to resolve type (should fail)
        Type? resolvedType = Type.GetType(message.TypeName);

        // Assert - Type resolution should fail
        resolvedType.Should().BeNull();

        // But dictionary fallback should work
        var dict = message.Data.Deserialize<Dictionary<string, object?>>();
        dict.Should().NotBeNull();
        dict!["topic"].Should().NotBeNull();
    }

    #endregion

    #region ApprovalDecision Enum Tests

    [Theory]
    [InlineData(ApprovalDecision.Approve, "Approve")]
    [InlineData(ApprovalDecision.Revise, "Revise")]
    [InlineData(ApprovalDecision.Reject, "Reject")]
    public void ApprovalDecision_SerializesAsString(ApprovalDecision decision, string expected)
    {
        // Arrange
        var response = new MarketingApprovalResponse { Decision = decision };

        // Act
        var json = JsonSerializer.Serialize(response, s_jsonOptions);

        // Assert
        json.Should().Contain($"\"{expected}\"");
    }

    [Theory]
    [InlineData("Approve", ApprovalDecision.Approve)]
    [InlineData("Revise", ApprovalDecision.Revise)]
    [InlineData("Reject", ApprovalDecision.Reject)]
    public void ApprovalDecision_DeserializesFromString(string value, ApprovalDecision expected)
    {
        // Arrange
        var json = $"{{\"decision\":\"{value}\",\"feedback\":null}}";

        // Act
        var response = JsonSerializer.Deserialize<MarketingApprovalResponse>(json, s_jsonOptions);

        // Assert
        response.Should().NotBeNull();
        response!.Decision.Should().Be(expected);
    }

    #endregion
}
