using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace DotHooks.Tests.Plugins;

/// <summary>
/// Tests for HookLogger plugin using PluginLoader to compile and load it dynamically.
/// This tests the real-world scenario where plugins are compiled at runtime.
/// </summary>
[TestClass]
public class HookLoggerTests
{
    private ILogger _mockLogger = null!;
    private IServiceProvider _serviceProvider = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = Substitute.For<ILogger>();

        // Create a simple service provider that returns our mock logger
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(ILogger)).Returns(_mockLogger);
        _serviceProvider = sp;
    }

    [TestMethod]
    public async Task HookLogger_CompilesSuccessfully()
    {
        // Arrange
        var pluginLoader = new PluginLoader(
            Substitute.For<ILogger<PluginLoader>>(),
            _serviceProvider);

        var pluginPath = Path.Combine("..", "..", "..", "..", "hooks", "plugins");
        var fullPath = Path.GetFullPath(pluginPath);

        // Act
        var handlerTypes = await pluginLoader.LoadHandlerTypesAsync(fullPath);

        // Assert
        Assert.IsTrue(handlerTypes.Count > 0, $"Should discover at least one handler type. Found: {handlerTypes.Count}");
        Assert.IsTrue(handlerTypes.Any(t => t.Name == "HookLogger"),
            $"Should discover HookLogger. Found types: {string.Join(", ", handlerTypes.Select(t => t.Name))}");

        // Also check what interfaces the HookLogger implements
        var hookLoggerType = handlerTypes.FirstOrDefault(t => t.Name == "HookLogger");
        if (hookLoggerType != null)
        {
            var interfaces = hookLoggerType.GetInterfaces();
            Console.WriteLine($"HookLogger implements {interfaces.Length} interfaces:");
            foreach (var iface in interfaces)
            {
                Console.WriteLine($"  - {iface.Name}");
            }
        }
    }

    [TestMethod]
    public async Task HookLogger_HandlesToolEvents()
    {
        // Arrange
        var pluginLoader = new PluginLoader(
            Substitute.For<ILogger<PluginLoader>>(),
            _serviceProvider);

        var pluginPath = Path.Combine("..", "..", "..", "..", "hooks", "plugins");
        var fullPath = Path.GetFullPath(pluginPath);
        var handlerTypes = await pluginLoader.LoadHandlerTypesAsync(fullPath);

        // Get handlers for tool events
        var handlers = pluginLoader.GetHandlersForEvent(handlerTypes, typeof(ToolEventInput), typeof(ToolEventOutput));

        Assert.IsTrue(handlers.Count > 0, $"Should have at least one tool event handler. Discovered {handlerTypes.Count} types but got {handlers.Count} handlers");

        var handler = handlers.First() as IHookEventHandler<ToolEventInput, ToolEventOutput>;
        Assert.IsNotNull(handler, "Handler should implement IHookEventHandler<ToolEventInput, ToolEventOutput>");

        // Act
        var input = new ToolEventInput
        {
            SessionId = "test-session",
            Cwd = "/test/dir",
            ToolName = "Write"
        };

        var output = await handler.HandleAsync(input);

        // Assert
        Assert.IsNotNull(output);
        Assert.AreEqual("approve", output.Decision);
        Assert.IsTrue(output.Continue);

        // Verify the handler was called (can't verify logger since it's injected by PluginLoader)
    }

    [TestMethod]
    public async Task HookLogger_HandlesSessionEvents()
    {
        // Arrange
        // Create a fresh service provider for this test
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(ILogger)).Returns(Substitute.For<ILogger>());

        var pluginLoader = new PluginLoader(
            Substitute.For<ILogger<PluginLoader>>(),
            sp);

        var pluginPath = Path.Combine("..", "..", "..", "..", "hooks", "plugins");
        var fullPath = Path.GetFullPath(pluginPath);
        var handlerTypes = await pluginLoader.LoadHandlerTypesAsync(fullPath);

        // Get handlers for session events
        var handlers = pluginLoader.GetHandlersForEvent(handlerTypes, typeof(SessionEventInput), typeof(SessionEventOutput));

        Assert.IsTrue(handlers.Count > 0, $"Should have at least one session event handler. Found {handlerTypes.Count} types, {handlers.Count} handlers");

        var handler = handlers.First() as IHookEventHandler<SessionEventInput, SessionEventOutput>;
        Assert.IsNotNull(handler, "Handler should implement IHookEventHandler<SessionEventInput, SessionEventOutput>");

        // Act
        var input = new SessionEventInput
        {
            SessionId = "test-session",
            Cwd = "/test/dir"
        };

        var output = await handler.HandleAsync(input);

        // Assert
        Assert.IsNotNull(output);
        Assert.AreEqual("approve", output.Decision);
        Assert.IsTrue(output.Continue);

        // Verify the handler was called (can't verify logger since it's injected by PluginLoader)
    }

    [TestMethod]
    public async Task HookLogger_HandlesGenericEvents()
    {
        // Arrange
        // Create a fresh service provider for this test
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(ILogger)).Returns(Substitute.For<ILogger>());

        var pluginLoader = new PluginLoader(
            Substitute.For<ILogger<PluginLoader>>(),
            sp);

        var pluginPath = Path.Combine("..", "..", "..", "..", "hooks", "plugins");
        var fullPath = Path.GetFullPath(pluginPath);
        var handlerTypes = await pluginLoader.LoadHandlerTypesAsync(fullPath);

        // Get handlers for generic events
        var handlers = pluginLoader.GetHandlersForEvent(handlerTypes, typeof(GenericEventInput), typeof(GenericEventOutput));

        Assert.IsTrue(handlers.Count > 0, $"Should have at least one generic event handler. Found {handlerTypes.Count} types, {handlers.Count} handlers");

        var handler = handlers.First() as IHookEventHandler<GenericEventInput, GenericEventOutput>;
        Assert.IsNotNull(handler, "Handler should implement IHookEventHandler<GenericEventInput, GenericEventOutput>");

        // Act
        var input = new GenericEventInput
        {
            SessionId = "test-session",
            Cwd = "/test/dir"
        };

        var output = await handler.HandleAsync(input);

        // Assert
        Assert.IsNotNull(output);
        Assert.AreEqual("approve", output.Decision);
        Assert.IsTrue(output.Continue);

        // Verify the handler was called (can't verify logger since it's injected by PluginLoader)
    }
}
