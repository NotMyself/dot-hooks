using DotHooks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace DotHooks.Tests;

[TestClass]
public class PluginLoaderTests
{
    private ILogger<PluginLoader> _logger = null!;
    private PluginLoader _pluginLoader = null!;
    private string _testDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        _logger = Substitute.For<ILogger<PluginLoader>>();
        _pluginLoader = new PluginLoader(_logger);
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
    public async Task LoadPluginsAsync_WithNoPlugins_ReturnsEmptyList()
    {
        // Arrange
        var emptyDirectory = Path.Combine(_testDirectory, "empty");
        Directory.CreateDirectory(emptyDirectory);

        // Act
        var plugins = await _pluginLoader.LoadPluginsAsync(emptyDirectory);

        // Assert
        Assert.IsNotNull(plugins);
        Assert.AreEqual(0, plugins.Count);
    }

    [TestMethod]
    public async Task LoadPluginsAsync_WithNonExistentDirectory_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "non-existent");

        // Act
        var plugins = await _pluginLoader.LoadPluginsAsync(nonExistentPath);

        // Assert
        Assert.IsNotNull(plugins);
        Assert.AreEqual(0, plugins.Count);
    }

    [TestMethod]
    public async Task LoadPluginsAsync_WithValidPlugin_LoadsSuccessfully()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginDirectory);

        var pluginSource = """
        using DotHooks;
        using System.Threading.Tasks;

        namespace TestPlugin;

        public class TestHookPlugin : IHookPlugin
        {
            public string Name => "TestPlugin";

            public Task<HookOutput> ExecuteAsync(HookInput input, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(HookOutput.Success());
            }
        }
        """;

        var pluginPath = Path.Combine(pluginDirectory, "TestPlugin.cs");
        await File.WriteAllTextAsync(pluginPath, pluginSource);

        // Act
        var plugins = await _pluginLoader.LoadPluginsAsync(pluginDirectory);

        // Assert
        Assert.IsNotNull(plugins);
        Assert.AreEqual(1, plugins.Count);
        Assert.AreEqual("TestPlugin", plugins[0].Name);
    }

    [TestMethod]
    public async Task LoadPluginsAsync_WithInvalidPlugin_SkipsPlugin()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginDirectory);

        var invalidSource = """
        using DotHooks;

        // This is not a valid plugin - missing implementation
        namespace TestPlugin;

        public class NotAPlugin
        {
            public string Name => "Invalid";
        }
        """;

        var pluginPath = Path.Combine(pluginDirectory, "Invalid.cs");
        await File.WriteAllTextAsync(pluginPath, invalidSource);

        // Act
        var plugins = await _pluginLoader.LoadPluginsAsync(pluginDirectory);

        // Assert
        Assert.IsNotNull(plugins);
        Assert.AreEqual(0, plugins.Count);
    }

    [TestMethod]
    public async Task LoadPluginsAsync_LoadsBothGlobalAndUserPlugins()
    {
        // Arrange
        var globalDirectory = Path.Combine(_testDirectory, "global");
        var userDirectory = Path.Combine(_testDirectory, "user");
        Directory.CreateDirectory(globalDirectory);
        Directory.CreateDirectory(userDirectory);

        var globalPluginSource = """
        using DotHooks;
        using System.Threading.Tasks;

        namespace GlobalPlugin;

        public class GlobalHookPlugin : IHookPlugin
        {
            public string Name => "GlobalPlugin";

            public Task<HookOutput> ExecuteAsync(HookInput input, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(HookOutput.Success());
            }
        }
        """;

        var userPluginSource = """
        using DotHooks;
        using System.Threading.Tasks;

        namespace UserPlugin;

        public class UserHookPlugin : IHookPlugin
        {
            public string Name => "UserPlugin";

            public Task<HookOutput> ExecuteAsync(HookInput input, System.Threading.CancellationToken cancellationToken = default)
            {
                return Task.FromResult(HookOutput.Success());
            }
        }
        """;

        await File.WriteAllTextAsync(Path.Combine(globalDirectory, "Global.cs"), globalPluginSource);
        await File.WriteAllTextAsync(Path.Combine(userDirectory, "User.cs"), userPluginSource);

        // Act
        var plugins = await _pluginLoader.LoadPluginsAsync(globalDirectory, userDirectory);

        // Assert
        Assert.IsNotNull(plugins);
        Assert.AreEqual(2, plugins.Count);
        Assert.IsTrue(plugins.Any(p => p.Name == "GlobalPlugin"));
        Assert.IsTrue(plugins.Any(p => p.Name == "UserPlugin"));
    }

    [TestMethod]
    public async Task LoadPluginsAsync_LoadsPluginsInAlphabeticalOrder()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "plugins");
        Directory.CreateDirectory(pluginDirectory);

        var pluginASource = """
        using DotHooks;
        using System.Threading.Tasks;

        public class PluginA : IHookPlugin
        {
            public string Name => "A";
            public Task<HookOutput> ExecuteAsync(HookInput input, System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult(HookOutput.Success());
        }
        """;

        var pluginBSource = """
        using DotHooks;
        using System.Threading.Tasks;

        public class PluginB : IHookPlugin
        {
            public string Name => "B";
            public Task<HookOutput> ExecuteAsync(HookInput input, System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult(HookOutput.Success());
        }
        """;

        // Write in reverse order
        await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "B.cs"), pluginBSource);
        await File.WriteAllTextAsync(Path.Combine(pluginDirectory, "A.cs"), pluginASource);

        // Act
        var plugins = await _pluginLoader.LoadPluginsAsync(pluginDirectory);

        // Assert
        Assert.AreEqual(2, plugins.Count);
        Assert.AreEqual("A", plugins[0].Name); // Should be first alphabetically
        Assert.AreEqual("B", plugins[1].Name);
    }
}
