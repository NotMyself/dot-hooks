using System.Text.Json;
using DotHooks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DotHooks.Tests;

[TestClass]
public class ModelsTests
{
    [TestMethod]
    public void HookInput_Deserializes_FromJson()
    {
        // Arrange
        var json = """
        {
            "session_id": "test-session",
            "transcript_path": "/path/to/transcript",
            "cwd": "/working/dir",
            "permission_mode": "ask",
            "event_type": "pre-tool-use",
            "tool_name": "Write"
        }
        """;

        // Act
        var input = JsonSerializer.Deserialize<HookInput>(json);

        // Assert
        Assert.IsNotNull(input);
        Assert.AreEqual("test-session", input.SessionId);
        Assert.AreEqual("/path/to/transcript", input.TranscriptPath);
        Assert.AreEqual("/working/dir", input.Cwd);
        Assert.AreEqual("ask", input.PermissionMode);
        Assert.AreEqual("pre-tool-use", input.EventType);
        Assert.AreEqual("Write", input.ToolName);
    }

    [TestMethod]
    public void HookInput_Serializes_ToJson()
    {
        // Arrange
        var input = new HookInput
        {
            SessionId = "test-session",
            TranscriptPath = "/path/to/transcript",
            Cwd = "/working/dir",
            PermissionMode = "ask",
            EventType = "pre-tool-use",
            ToolName = "Write"
        };

        // Act
        var json = JsonSerializer.Serialize(input);
        var deserialized = JsonSerializer.Deserialize<HookInput>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(input.SessionId, deserialized.SessionId);
        Assert.AreEqual(input.TranscriptPath, deserialized.TranscriptPath);
        Assert.AreEqual(input.EventType, deserialized.EventType);
    }

    [TestMethod]
    public void HookOutput_Success_CreatesApprovedOutput()
    {
        // Act
        var output = HookOutput.Success();

        // Assert
        Assert.AreEqual("approve", output.Decision);
        Assert.IsTrue(output.Continue);
        Assert.IsNull(output.StopReason);
    }

    [TestMethod]
    public void HookOutput_Block_CreatesBlockingOutput()
    {
        // Arrange
        var reason = "Test blocking reason";

        // Act
        var output = HookOutput.Block(reason);

        // Assert
        Assert.AreEqual("block", output.Decision);
        Assert.IsFalse(output.Continue);
        Assert.AreEqual(reason, output.StopReason);
    }

    [TestMethod]
    public void HookOutput_WithContext_AddsContextToOutput()
    {
        // Arrange
        var context = "Additional context for Claude";

        // Act
        var output = HookOutput.WithContext(context);

        // Assert
        Assert.AreEqual("approve", output.Decision);
        Assert.IsTrue(output.Continue);
        Assert.AreEqual(context, output.AdditionalContext);
    }

    [TestMethod]
    public void HookOutput_Serializes_WithCorrectPropertyNames()
    {
        // Arrange
        var output = new HookOutput
        {
            Decision = "approve",
            Continue = true,
            SystemMessage = "Test message",
            AdditionalContext = "Test context"
        };

        // Act
        var json = JsonSerializer.Serialize(output);

        // Assert
        Assert.IsTrue(json.Contains("\"decision\":\"approve\""));
        Assert.IsTrue(json.Contains("\"continue\":true"));
        Assert.IsTrue(json.Contains("\"systemMessage\":\"Test message\""));
        Assert.IsTrue(json.Contains("\"additionalContext\":\"Test context\""));
    }
}
