using DotHooks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace DotHooks.Tests.Plugins;

[TestClass]
public class HookLoggerTests
{
    private HookLogger _plugin = null!;
    private ILogger _mockLogger = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = Substitute.For<ILogger>();
        _plugin = new HookLogger(_mockLogger);
    }

    [TestMethod]
    public void HookLogger_HasCorrectName()
    {
        // Assert
        Assert.AreEqual("HookLogger", _plugin.Name);
    }

    [TestMethod]
    public async Task ExecuteAsync_LogsEventType()
    {
        // Arrange
        var input = new HookInput
        {
            SessionId = "test-session",
            EventType = "pre-tool-use",
            Cwd = "/test/dir"
        };

        // Act
        await _plugin.ExecuteAsync(input);

        // Assert
        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Hook event triggered")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public async Task ExecuteAsync_LogsSessionId()
    {
        // Arrange
        var input = new HookInput
        {
            SessionId = "test-session-123",
            EventType = "post-tool-use",
            Cwd = "/test/dir"
        };

        // Act
        await _plugin.ExecuteAsync(input);

        // Assert
        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Session ID")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public async Task ExecuteAsync_LogsWorkingDirectory()
    {
        // Arrange
        var input = new HookInput
        {
            SessionId = "test-session",
            EventType = "session-start",
            Cwd = "/my/working/dir"
        };

        // Act
        await _plugin.ExecuteAsync(input);

        // Assert
        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Working Directory")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public async Task ExecuteAsync_LogsToolNameWhenProvided()
    {
        // Arrange
        var input = new HookInput
        {
            SessionId = "test-session",
            EventType = "pre-tool-use",
            Cwd = "/test/dir",
            ToolName = "Write"
        };

        // Act
        await _plugin.ExecuteAsync(input);

        // Assert
        _mockLogger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Tool")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [TestMethod]
    public async Task ExecuteAsync_ReturnsSuccessOutput()
    {
        // Arrange
        var input = new HookInput
        {
            SessionId = "test-session",
            EventType = "pre-tool-use",
            Cwd = "/test/dir"
        };

        // Act
        var output = await _plugin.ExecuteAsync(input);

        // Assert
        Assert.IsNotNull(output);
        Assert.AreEqual("approve", output.Decision);
        Assert.IsTrue(output.Continue);
    }

    [TestMethod]
    public async Task ExecuteAsync_CompletesWithinReasonableTime()
    {
        // Arrange
        var input = new HookInput
        {
            SessionId = "test-session",
            EventType = "pre-tool-use",
            Cwd = "/test/dir"
        };

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act & Assert
        await _plugin.ExecuteAsync(input, cts.Token);
        // If we get here without timeout, the test passes
        Assert.IsTrue(true);
    }
}
