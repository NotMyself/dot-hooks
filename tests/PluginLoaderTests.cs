using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace DotHooks.Tests;

[TestClass]
public class PluginLoaderTests
{
    private ILogger<PluginLoader> _logger = null!;
    private IServiceProvider _serviceProvider = null!;
    private PluginLoader _pluginLoader = null!;
    private string _testDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _logger = Substitute.For<ILogger<PluginLoader>>();

        // Create a service provider that can return loggers
        var sp = Substitute.For<IServiceProvider>();
        sp.GetService(typeof(ILogger)).Returns(Substitute.For<ILogger>());
        _serviceProvider = sp;

        _pluginLoader = new PluginLoader(_logger, _serviceProvider);
        _testDirectory = Path.Combine(Path.GetTempPath(), $"dot-hooks-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [TestMethod]
    public async Task LoadHandlerTypesAsync_WithNoPlugins_ReturnsEmptyList()
    {
        // Arrange
        var emptyDirectory = Path.Combine(_testDirectory, "empty");
        Directory.CreateDirectory(emptyDirectory);

        // Act
        var handlerTypes = await _pluginLoader.LoadHandlerTypesAsync(emptyDirectory);

        // Assert
        Assert.IsNotNull(handlerTypes);
        Assert.AreEqual(0, handlerTypes.Count);
    }

    [TestMethod]
    public async Task LoadHandlerTypesAsync_WithNonExistentDirectory_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "non-existent");

        // Act
        var handlerTypes = await _pluginLoader.LoadHandlerTypesAsync(nonExistentPath);

        // Assert
        Assert.IsNotNull(handlerTypes);
        Assert.AreEqual(0, handlerTypes.Count);
    }

    [TestMethod]
    public async Task LoadHandlerTypesAsync_WithValidHandler_LoadsSuccessfully()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginDirectory);

        var handlerSource = """
        using System.Threading.Tasks;

        public class TestHandler : IHookEventHandler<ToolEventInput, ToolEventOutput>
        {
            public string Name => "TestHandler";

            public Task<ToolEventOutput> HandleAsync(ToolEventInput input, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
            }
        }
        """;

        var handlerPath = Path.Combine(pluginDirectory, "TestHandler.cs");
        await File.WriteAllTextAsync(handlerPath, handlerSource);

        // Act
        var handlerTypes = await _pluginLoader.LoadHandlerTypesAsync(pluginDirectory);

        // Assert
        Assert.IsNotNull(handlerTypes);
        Assert.AreEqual(1, handlerTypes.Count);
        Assert.AreEqual("TestHandler", handlerTypes[0].Name);
    }

    [TestMethod]
    public async Task LoadHandlerTypesAsync_WithInvalidHandler_SkipsHandler()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginDirectory);

        var invalidSource = """
        // This is not a valid handler - missing interface implementation
        public class NotAHandler
        {
            public string Name => "Invalid";
        }
        """;

        var handlerPath = Path.Combine(pluginDirectory, "Invalid.cs");
        await File.WriteAllTextAsync(handlerPath, invalidSource);

        // Act
        var handlerTypes = await _pluginLoader.LoadHandlerTypesAsync(pluginDirectory);

        // Assert
        Assert.IsNotNull(handlerTypes);
        Assert.AreEqual(0, handlerTypes.Count);
    }

    [TestMethod]
    public async Task LoadHandlerTypesAsync_LoadsBothGlobalAndUserHandlers()
    {
        // Arrange
        var globalDirectory = Path.Combine(_testDirectory, "global");
        var userDirectory = Path.Combine(_testDirectory, "user");
        Directory.CreateDirectory(globalDirectory);
        Directory.CreateDirectory(userDirectory);

        var globalHandlerSource = """
        using System.Threading.Tasks;

        public class GlobalHandler : IHookEventHandler<SessionEventInput, SessionEventOutput>
        {
            public string Name => "GlobalHandler";

            public Task<SessionEventOutput> HandleAsync(SessionEventInput input, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(HookOutputBase.Success<SessionEventOutput>());
            }
        }
        """;

        var userHandlerSource = """
        using System.Threading.Tasks;

        public class UserHandler : IHookEventHandler<ToolEventInput, ToolEventOutput>
        {
            public string Name => "UserHandler";

            public Task<ToolEventOutput> HandleAsync(ToolEventInput input, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
            }
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(globalDirectory, "Global.cs"), globalHandlerSource);
        await File.WriteAllTextAsync(Path.Combine(userDirectory, "User.cs"), userHandlerSource);

        // Act
        var handlerTypes = await _pluginLoader.LoadHandlerTypesAsync(globalDirectory, userDirectory);

        // Assert
        Assert.IsNotNull(handlerTypes);
        Assert.AreEqual(2, handlerTypes.Count);
        Assert.IsTrue(handlerTypes.Any(t => t.Name == "GlobalHandler"));
        Assert.IsTrue(handlerTypes.Any(t => t.Name == "UserHandler"));
    }

    [TestMethod]
    public async Task GetHandlersForEvent_FiltersHandlersByEventType()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginDirectory);

        var multiHandlerSource = """
        using System.Threading.Tasks;

        public class MultiHandler :
            IHookEventHandler<ToolEventInput, ToolEventOutput>,
            IHookEventHandler<SessionEventInput, SessionEventOutput>
        {
            public string Name => "MultiHandler";

            Task<ToolEventOutput> IHookEventHandler<ToolEventInput, ToolEventOutput>.HandleAsync(
                ToolEventInput input, System.Threading.CancellationToken ct)
            {
                return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
            }

            Task<SessionEventOutput> IHookEventHandler<SessionEventInput, SessionEventOutput>.HandleAsync(
                SessionEventInput input, System.Threading.CancellationToken ct)
            {
                return Task.FromResult(HookOutputBase.Success<SessionEventOutput>());
            }
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "Multi.cs"), multiHandlerSource);

        var handlerTypes = await _pluginLoader.LoadHandlerTypesAsync(pluginDirectory);

        // Act - Get handlers for tool events only
        var toolHandlers = _pluginLoader.GetHandlersForEvent(handlerTypes, typeof(ToolEventInput), typeof(ToolEventOutput));
        var sessionHandlers = _pluginLoader.GetHandlersForEvent(handlerTypes, typeof(SessionEventInput), typeof(SessionEventOutput));

        // Assert
        Assert.AreEqual(1, toolHandlers.Count);
        Assert.AreEqual(1, sessionHandlers.Count);
        Assert.AreEqual("MultiHandler", _pluginLoader.GetHandlerName(toolHandlers[0]));
        Assert.AreEqual("MultiHandler", _pluginLoader.GetHandlerName(sessionHandlers[0]));
    }

    [TestMethod]
    public async Task GetHandlersForEvent_OrdersHandlersAlphabetically()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginDirectory);

        var handlerASource = """
        using System.Threading.Tasks;

        public class HandlerA : IHookEventHandler<ToolEventInput, ToolEventOutput>
        {
            public string Name => "A";
            public Task<ToolEventOutput> HandleAsync(ToolEventInput input, System.Threading.CancellationToken ct)
                => Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
        }
        """;

        var handlerBSource = """
        using System.Threading.Tasks;

        public class HandlerB : IHookEventHandler<ToolEventInput, ToolEventOutput>
        {
            public string Name => "B";
            public Task<ToolEventOutput> HandleAsync(ToolEventInput input, System.Threading.CancellationToken ct)
                => Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
        }
        """;

        // Write in reverse order
        await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "B.cs"), handlerBSource);
        await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "A.cs"), handlerASource);

        var handlerTypes = await _pluginLoader.LoadHandlerTypesAsync(pluginDirectory);

        // Act
        var handlers = _pluginLoader.GetHandlersForEvent(handlerTypes, typeof(ToolEventInput), typeof(ToolEventOutput));

        // Assert
        Assert.AreEqual(2, handlers.Count);
        Assert.AreEqual("A", _pluginLoader.GetHandlerName(handlers[0])); // Should be first alphabetically
        Assert.AreEqual("B", _pluginLoader.GetHandlerName(handlers[1]));
    }

    [TestMethod]
    public async Task GetHandlersForEvent_CreatesHandlerInstancesWithDI()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginDirectory);

        var handlerSource = """
        using System.Threading.Tasks;
        using Microsoft.Extensions.Logging;

        public class DIHandler : IHookEventHandler<ToolEventInput, ToolEventOutput>
        {
            private readonly ILogger _logger;

            public DIHandler(ILogger logger)
            {
                _logger = logger;
            }

            public string Name => "DIHandler";

            public Task<ToolEventOutput> HandleAsync(ToolEventInput input, System.Threading.CancellationToken ct)
            {
                _logger.LogInformation("Test");
                return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
            }
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "DI.cs"), handlerSource);

        var handlerTypes = await _pluginLoader.LoadHandlerTypesAsync(pluginDirectory);

        // Act
        var handlers = _pluginLoader.GetHandlersForEvent(handlerTypes, typeof(ToolEventInput), typeof(ToolEventOutput));

        // Assert
        Assert.AreEqual(1, handlers.Count);
        Assert.IsNotNull(handlers[0]);
        Assert.AreEqual("DIHandler", _pluginLoader.GetHandlerName(handlers[0]));
    }
}
