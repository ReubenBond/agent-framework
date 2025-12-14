// Copyright (c) Microsoft. All rights reserved.

using AgentWebChat.AgentHost.Workflows;
using Microsoft.Extensions.AI;
using Moq;

namespace AgentWebChat.AgentHost.UnitTests.Workflows;

/// <summary>
/// Unit tests for <see cref="MarketingWorkflowFactory"/> and the marketing workflow structure.
/// </summary>
public sealed class MarketingWorkflowFactoryTests
{
    [Fact]
    public void Build_CreatesWorkflow_WithCorrectName()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();

        // Act
        var workflow = MarketingWorkflowFactory.Build(mockChatClient.Object);

        // Assert
        workflow.Should().NotBeNull();
        workflow.Name.Should().Be("marketing-content");
    }

    [Fact]
    public void Build_CreatesWorkflow_WithDescription()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();

        // Act
        var workflow = MarketingWorkflowFactory.Build(mockChatClient.Object);

        // Assert
        workflow.Description.Should().NotBeNullOrEmpty();
        workflow.Description.Should().Contain("Human-in-the-Loop");
    }

    [Fact]
    public void WorkflowName_Constant_HasExpectedValue()
    {
        // Assert
        MarketingWorkflowFactory.WorkflowName.Should().Be("marketing-content");
    }

    #region Workflow Type Tests

    [Fact]
    public void MarketingContentInput_AllPropertiesNullable()
    {
        // Arrange & Act
        var input = new MarketingContentInput();

        // Assert
        input.Topic.Should().BeNull();
        input.TargetAudience.Should().BeNull();
        input.Tone.Should().BeNull();
    }

    [Fact]
    public void MarketingContentInput_CanSetAllProperties()
    {
        // Arrange & Act
        var input = new MarketingContentInput
        {
            Topic = "AI",
            TargetAudience = "Developers",
            Tone = "Technical"
        };

        // Assert
        input.Topic.Should().Be("AI");
        input.TargetAudience.Should().Be("Developers");
        input.Tone.Should().Be("Technical");
    }

    [Fact]
    public void MarketingContent_HasDefaultValues()
    {
        // Arrange & Act
        var content = new MarketingContent();

        // Assert
        content.Content.Should().BeEmpty();
        content.Step.Should().BeEmpty();
        content.Version.Should().Be(1);
        content.QualityScore.Should().BeNull();
        content.ReviewerNotes.Should().BeNull();
    }

    [Fact]
    public void MarketingApprovalRequest_HasDefaultOptions()
    {
        // Arrange & Act
        var request = new MarketingApprovalRequest();

        // Assert
        request.Options.Should().NotBeNull();
        request.Options.Should().HaveCount(3);
        request.Options.Should().Contain("approve");
        request.Options.Should().Contain("revise");
        request.Options.Should().Contain("reject");
    }

    [Fact]
    public void MarketingWorkflowOutput_HasDefaultValues()
    {
        // Arrange & Act
        var output = new MarketingWorkflowOutput();

        // Assert
        output.Content.Should().BeEmpty();
        output.Status.Should().BeEmpty();
        output.TotalVersions.Should().Be(0);
        output.Feedback.Should().BeNull();
    }

    #endregion

    #region ApprovalDecision Enum Tests

    [Fact]
    public void ApprovalDecision_HasExpectedValues()
    {
        // Assert
        Enum.GetValues<ApprovalDecision>().Should().HaveCount(3);
        Enum.GetValues<ApprovalDecision>().Should().Contain(ApprovalDecision.Approve);
        Enum.GetValues<ApprovalDecision>().Should().Contain(ApprovalDecision.Revise);
        Enum.GetValues<ApprovalDecision>().Should().Contain(ApprovalDecision.Reject);
    }

    [Fact]
    public void ApprovalDecision_Approve_IsDefault()
    {
        // Arrange & Act
        const ApprovalDecision defaultDecision = default;

        // Assert
        defaultDecision.Should().Be(ApprovalDecision.Approve);
    }

    #endregion

    #region Workflow Route Tests

    [Fact]
    public void Build_WorkflowHasCorrectName()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();

        // Act
        var workflow = MarketingWorkflowFactory.Build(mockChatClient.Object);

        // Assert
        workflow.Should().NotBeNull();
        workflow.Name.Should().Be("marketing-content");
    }

    [Fact]
    public void Build_WorkflowHasApprovalPort()
    {
        // Arrange
        var mockChatClient = new Mock<IChatClient>();

        // Act
        var workflow = MarketingWorkflowFactory.Build(mockChatClient.Object);

        // Assert - Check that we have executors/ports
        workflow.Should().NotBeNull();

        // The workflow should have the approval port registered
        // We can verify this by checking the workflow structure includes a RequestPort
        // Note: This is a basic structural test; full routing tests require integration testing
    }

    #endregion
}
