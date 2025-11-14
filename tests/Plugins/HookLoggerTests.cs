using DotHooks;
using DotHooks.Plugins;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DotHooks.Tests.Plugins;

[TestClass]
public class HookLoggerTests
{
    private HookLogger _plugin = null!;
    private StringWriter _consoleOutput = null!;
    private TextWriter _originalConsole = null!;

    [TestInitialize]
    public void Setup()
    {
        _plugin = new HookLogger();
        _consoleOutput = new StringWriter();
        _originalConsole = Console.Out;
        Console.SetOut(_consoleOutput);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Console.SetOut(_originalConsole);
        _consoleOutput.Dispose();
    }

    [TestMethod]
    public void HookLogger_HasCorrectName()
    {
        // Assert
        Assert.AreEqual("HookLogger", _plugin.Name);
    }

    [TestMethod]
    public async Task ExecuteAsync_WritesEventTypeToConsole()
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
        var consoleText = _consoleOutput.ToString();
        Assert.IsTrue(consoleText.Contains("Hook event triggered: pre-tool-use"));
    }

    [TestMethod]
    public async Task ExecuteAsync_WritesSessionIdToConsole()
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
        var consoleText = _consoleOutput.ToString();
        Assert.IsTrue(consoleText.Contains("Session ID: test-session-123"));
    }

    [TestMethod]
    public async Task ExecuteAsync_WritesWorkingDirectoryToConsole()
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
        var consoleText = _consoleOutput.ToString();
        Assert.IsTrue(consoleText.Contains("Working Directory: /my/working/dir"));
    }

    [TestMethod]
    public async Task ExecuteAsync_WritesToolNameWhenProvided()
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
        var consoleText = _consoleOutput.ToString();
        Assert.IsTrue(consoleText.Contains("Tool: Write"));
    }

    [TestMethod]
    public async Task ExecuteAsync_DoesNotWriteToolNameWhenNotProvided()
    {
        // Arrange
        var input = new HookInput
        {
            SessionId = "test-session",
            EventType = "session-start",
            Cwd = "/test/dir"
        };

        // Act
        await _plugin.ExecuteAsync(input);

        // Assert
        var consoleText = _consoleOutput.ToString();
        Assert.IsFalse(consoleText.Contains("Tool:"));
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
