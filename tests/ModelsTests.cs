using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DotHooks.Tests;

[TestClass]
public class ModelsTests
{
    #region ToolEventInput/Output Tests

    [TestMethod]
    public void ToolEventInput_Deserializes_FromJson()
    {
        // Arrange
        var json = """
        {
            "session_id": "test-session",
            "transcript_path": "/path/to/transcript",
            "cwd": "/working/dir",
            "permission_mode": "ask",
            "tool_name": "Write",
            "tool_parameters": { "file_path": "/test/file.txt" }
        }
        """;

        // Act
        var input = JsonSerializer.Deserialize<ToolEventInput>(json);

        // Assert
        Assert.IsNotNull(input);
        Assert.AreEqual("test-session", input.SessionId);
        Assert.AreEqual("/path/to/transcript", input.TranscriptPath);
        Assert.AreEqual("/working/dir", input.Cwd);
        Assert.AreEqual("ask", input.PermissionMode);
        Assert.AreEqual("Write", input.ToolName);
        Assert.IsNotNull(input.ToolParameters);
    }

    [TestMethod]
    public void ToolEventInput_Serializes_ToJson()
    {
        // Arrange
        var input = new ToolEventInput
        {
            SessionId = "test-session",
            TranscriptPath = "/path/to/transcript",
            Cwd = "/working/dir",
            PermissionMode = "ask",
            ToolName = "Write",
            ToolParameters = new Dictionary<string, object> { ["file_path"] = "/test/file.txt" }
        };

        // Act
        var json = JsonSerializer.Serialize(input);
        var deserialized = JsonSerializer.Deserialize<ToolEventInput>(json);

        // Assert
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(input.SessionId, deserialized.SessionId);
        Assert.AreEqual(input.ToolName, deserialized.ToolName);
    }

    #endregion

    #region SessionEventInput/Output Tests

    [TestMethod]
    public void SessionEventInput_Deserializes_FromJson()
    {
        // Arrange
        var json = """
        {
            "session_id": "test-session",
            "transcript_path": "/path/to/transcript",
            "cwd": "/working/dir",
            "permission_mode": "ask"
        }
        """;

        // Act
        var input = JsonSerializer.Deserialize<SessionEventInput>(json);

        // Assert
        Assert.IsNotNull(input);
        Assert.AreEqual("test-session", input.SessionId);
        Assert.AreEqual("/path/to/transcript", input.TranscriptPath);
        Assert.AreEqual("/working/dir", input.Cwd);
        Assert.AreEqual("ask", input.PermissionMode);
    }

    #endregion

    #region GenericEventInput/Output Tests

    [TestMethod]
    public void GenericEventInput_Deserializes_FromJson()
    {
        // Arrange
        var json = """
        {
            "session_id": "test-session",
            "transcript_path": "/path/to/transcript",
            "cwd": "/working/dir",
            "permission_mode": "ask",
            "additional_data": { "key": "value" }
        }
        """;

        // Act
        var input = JsonSerializer.Deserialize<GenericEventInput>(json);

        // Assert
        Assert.IsNotNull(input);
        Assert.AreEqual("test-session", input.SessionId);
        Assert.IsNotNull(input.AdditionalData);
    }

    #endregion

    #region HookOutputBase Factory Methods

    [TestMethod]
    public void HookOutputBase_Success_CreatesApprovedOutput()
    {
        // Act
        var output = HookOutputBase.Success<ToolEventOutput>();

        // Assert
        Assert.AreEqual("approve", output.Decision);
        Assert.IsTrue(output.Continue);
        Assert.IsNull(output.StopReason);
    }

    [TestMethod]
    public void HookOutputBase_Block_CreatesBlockingOutput()
    {
        // Arrange
        var reason = "Test blocking reason";

        // Act
        var output = HookOutputBase.Block<SessionEventOutput>(reason);

        // Assert
        Assert.AreEqual("block", output.Decision);
        Assert.IsFalse(output.Continue);
        Assert.AreEqual(reason, output.StopReason);
    }

    [TestMethod]
    public void HookOutputBase_WithContext_AddsContextToOutput()
    {
        // Arrange
        var context = "Additional context for Claude";

        // Act
        var output = HookOutputBase.WithContext<GenericEventOutput>(context);

        // Assert
        Assert.AreEqual("approve", output.Decision);
        Assert.IsTrue(output.Continue);
        Assert.AreEqual(context, output.AdditionalContext);
    }

    #endregion

    #region Output Serialization Tests

    [TestMethod]
    public void ToolEventOutput_Serializes_WithCorrectPropertyNames()
    {
        // Arrange
        var output = new ToolEventOutput
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

    #endregion

    #region EventTypeRegistry Tests

    [TestMethod]
    public void EventTypeRegistry_GetTypes_ReturnsCorrectTypesForToolEvents()
    {
        // Act
        var (inputType, outputType) = EventTypeRegistry.GetTypes("pre-tool-use");

        // Assert
        Assert.AreEqual(typeof(ToolEventInput), inputType);
        Assert.AreEqual(typeof(ToolEventOutput), outputType);
    }

    [TestMethod]
    public void EventTypeRegistry_GetTypes_ReturnsCorrectTypesForSessionEvents()
    {
        // Act
        var (inputType, outputType) = EventTypeRegistry.GetTypes("session-start");

        // Assert
        Assert.AreEqual(typeof(SessionEventInput), inputType);
        Assert.AreEqual(typeof(SessionEventOutput), outputType);
    }

    [TestMethod]
    public void EventTypeRegistry_GetTypes_ReturnsCorrectTypesForGenericEvents()
    {
        // Act
        var (inputType, outputType) = EventTypeRegistry.GetTypes("user-prompt-submit");

        // Assert
        Assert.AreEqual(typeof(GenericEventInput), inputType);
        Assert.AreEqual(typeof(GenericEventOutput), outputType);
    }

    [TestMethod]
    public void EventTypeRegistry_IsValidEvent_ReturnsTrueForValidEvents()
    {
        // Assert
        Assert.IsTrue(EventTypeRegistry.IsValidEvent("pre-tool-use"));
        Assert.IsTrue(EventTypeRegistry.IsValidEvent("session-start"));
        Assert.IsTrue(EventTypeRegistry.IsValidEvent("user-prompt-submit"));
    }

    [TestMethod]
    public void EventTypeRegistry_IsValidEvent_ReturnsFalseForInvalidEvents()
    {
        // Assert
        Assert.IsFalse(EventTypeRegistry.IsValidEvent("invalid-event"));
        Assert.IsFalse(EventTypeRegistry.IsValidEvent(""));
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public void EventTypeRegistry_GetTypes_ThrowsForInvalidEvent()
    {
        // Act
        EventTypeRegistry.GetTypes("invalid-event");
    }

    #endregion

    #region Type Constraint Tests

    [TestMethod]
    public void ToolEventInput_InheritsFromHookInputBase()
    {
        // Arrange
        var input = new ToolEventInput { SessionId = "test" };

        // Assert
        Assert.IsInstanceOfType(input, typeof(HookInputBase<ToolEventOutput>));
    }

    [TestMethod]
    public void ToolEventOutput_InheritsFromHookOutputBase()
    {
        // Arrange
        var output = new ToolEventOutput { Decision = "approve" };

        // Assert
        Assert.IsInstanceOfType(output, typeof(HookOutputBase));
    }

    #endregion
}
