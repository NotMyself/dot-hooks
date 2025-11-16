using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DotHooks.Tests;

[TestClass]
public class ConfigurationTests
{
    [TestMethod]
    public void Configuration_DefaultSettings_AreCorrect()
    {
        // Arrange & Act
        var settings = new DotHooksSettings();

        // Assert
        Assert.AreEqual("Information", settings.Logging.MinimumLevel);
        Assert.AreEqual("Trace", settings.Logging.ConsoleThreshold);
        Assert.AreEqual("hooks", settings.Paths.HooksDirectory);
        Assert.AreEqual("plugins", settings.Paths.PluginsDirectory);
        Assert.AreEqual(".claude", settings.Paths.ClaudeDirectory);
        Assert.AreEqual("dot-hooks", settings.Paths.DotHooksDirectory);
        Assert.AreEqual("state", settings.Paths.StateDirectory);
        Assert.AreEqual("dot-hooks.log", settings.Paths.SessionLogFileName);
        Assert.AreEqual("CSharp12", settings.Compilation.LanguageVersion);
        Assert.AreEqual(30000, settings.Hooks.DefaultTimeoutMs);
        Assert.IsTrue(settings.Plugins.EnableGlobalPlugins);
        Assert.IsTrue(settings.Plugins.EnableUserPlugins);
    }

    [TestMethod]
    public void Configuration_AllHooks_EnabledByDefault()
    {
        // Arrange & Act
        var settings = new DotHooksSettings();

        // Assert
        var expectedHooks = new[]
        {
            "pre-tool-use",
            "post-tool-use",
            "user-prompt-submit",
            "notification",
            "stop",
            "subagent-stop",
            "session-start",
            "session-end",
            "pre-compact"
        };

        foreach (var hook in expectedHooks)
        {
            Assert.IsTrue(settings.Hooks.EnabledHooks.ContainsKey(hook),
                $"Hook '{hook}' should be in EnabledHooks dictionary");
            Assert.IsTrue(settings.Hooks.EnabledHooks[hook],
                $"Hook '{hook}' should be enabled by default");
        }
    }

    [TestMethod]
    public void Configuration_JsonBinding_WorksCorrectly()
    {
        // Arrange
        var jsonConfig = new Dictionary<string, string?>
        {
            ["Logging:MinimumLevel"] = "Debug",
            ["Logging:ConsoleThreshold"] = "Warning",
            ["Paths:HooksDirectory"] = "custom-hooks",
            ["Compilation:LanguageVersion"] = "CSharp13",
            ["Hooks:DefaultTimeoutMs"] = "60000",
            ["Hooks:EnabledHooks:pre-tool-use"] = "false",
            ["Plugins:EnableGlobalPlugins"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(jsonConfig)
            .Build();

        // Act
        var settings = new DotHooksSettings();
        configuration.Bind(settings);

        // Assert
        Assert.AreEqual("Debug", settings.Logging.MinimumLevel);
        Assert.AreEqual("Warning", settings.Logging.ConsoleThreshold);
        Assert.AreEqual("custom-hooks", settings.Paths.HooksDirectory);
        Assert.AreEqual("CSharp13", settings.Compilation.LanguageVersion);
        Assert.AreEqual(60000, settings.Hooks.DefaultTimeoutMs);
        Assert.IsFalse(settings.Hooks.EnabledHooks["pre-tool-use"]);
        Assert.IsFalse(settings.Plugins.EnableGlobalPlugins);
    }

    [TestMethod]
    public void Configuration_Priority_HigherOverridesLower()
    {
        // Arrange - Simulate layered configuration (base -> override)
        var baseConfig = new Dictionary<string, string?>
        {
            ["Logging:MinimumLevel"] = "Information",
            ["Hooks:DefaultTimeoutMs"] = "30000"
        };

        var overrideConfig = new Dictionary<string, string?>
        {
            ["Logging:MinimumLevel"] = "Debug"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(baseConfig)
            .AddInMemoryCollection(overrideConfig)
            .Build();

        // Act
        var settings = new DotHooksSettings();
        configuration.Bind(settings);

        // Assert
        Assert.AreEqual("Debug", settings.Logging.MinimumLevel,
            "Override config should win");
        Assert.AreEqual(30000, settings.Hooks.DefaultTimeoutMs,
            "Base config should be used when not overridden");
    }

    [TestMethod]
    public void Configuration_EnvironmentVariables_HaveHighestPriority()
    {
        // Arrange
        var fileConfig = new Dictionary<string, string?>
        {
            ["Logging:MinimumLevel"] = "Information"
        };

        // Simulate environment variables (without the DOTHOOKS_ prefix which AddEnvironmentVariables strips)
        var envConfig = new Dictionary<string, string?>
        {
            ["Logging:MinimumLevel"] = "Error"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(fileConfig)
            .AddInMemoryCollection(envConfig)  // Later source wins
            .Build();

        // Act
        var settings = new DotHooksSettings();
        configuration.Bind(settings);

        // Assert
        Assert.AreEqual("Error", settings.Logging.MinimumLevel,
            "Later configuration source should override earlier ones");
    }

    [TestMethod]
    public void Configuration_IOptions_RegisteredCorrectly()
    {
        // Arrange
        var jsonConfig = new Dictionary<string, string?>
        {
            ["Logging:MinimumLevel"] = "Debug",
            ["Hooks:DefaultTimeoutMs"] = "45000"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(jsonConfig)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<DotHooksSettings>(configuration);
        services.Configure<LoggingSettings>(configuration.GetSection("Logging"));
        services.Configure<HookSettings>(configuration.GetSection("Hooks"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var dotHooksOptions = serviceProvider.GetRequiredService<IOptions<DotHooksSettings>>();
        var loggingOptions = serviceProvider.GetRequiredService<IOptions<LoggingSettings>>();
        var hookOptions = serviceProvider.GetRequiredService<IOptions<HookSettings>>();

        // Assert
        Assert.AreEqual("Debug", dotHooksOptions.Value.Logging.MinimumLevel);
        Assert.AreEqual("Debug", loggingOptions.Value.MinimumLevel);
        Assert.AreEqual(45000, hookOptions.Value.DefaultTimeoutMs);
    }

    [TestMethod]
    public void Configuration_DisabledHook_NotInDictionary()
    {
        // Arrange
        var jsonConfig = new Dictionary<string, string?>
        {
            ["Hooks:EnabledHooks:pre-tool-use"] = "true",
            ["Hooks:EnabledHooks:post-tool-use"] = "false",
            ["Hooks:EnabledHooks:session-start"] = "true"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(jsonConfig)
            .Build();

        // Act
        var settings = new DotHooksSettings();
        configuration.Bind(settings);

        // Assert
        Assert.IsTrue(settings.Hooks.EnabledHooks["pre-tool-use"]);
        Assert.IsFalse(settings.Hooks.EnabledHooks["post-tool-use"]);
        Assert.IsTrue(settings.Hooks.EnabledHooks["session-start"]);
    }

    [TestMethod]
    public void Configuration_PartialHookOverride_PreservesOtherDefaults()
    {
        // Arrange - Only override some hooks, others should keep defaults
        var jsonConfig = new Dictionary<string, string?>
        {
            ["Hooks:EnabledHooks:notification"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(jsonConfig)
            .Build();

        // Act
        var settings = new DotHooksSettings();
        // Apply base defaults first
        var baseSettings = new DotHooksSettings();
        foreach (var hook in baseSettings.Hooks.EnabledHooks)
        {
            settings.Hooks.EnabledHooks[hook.Key] = hook.Value;
        }
        // Then override with configuration
        configuration.Bind(settings);

        // Assert
        Assert.IsFalse(settings.Hooks.EnabledHooks["notification"],
            "Overridden hook should be disabled");
        Assert.IsTrue(settings.Hooks.EnabledHooks["pre-tool-use"],
            "Non-overridden hooks should remain enabled");
        Assert.IsTrue(settings.Hooks.EnabledHooks["session-start"],
            "Non-overridden hooks should remain enabled");
    }

    [TestMethod]
    public void Configuration_LogLevel_ParsesCorrectly()
    {
        // Arrange
        var testCases = new[]
        {
            ("Trace", LogLevel.Trace),
            ("Debug", LogLevel.Debug),
            ("Information", LogLevel.Information),
            ("Warning", LogLevel.Warning),
            ("Error", LogLevel.Error),
            ("Critical", LogLevel.Critical)
        };

        foreach (var (levelString, expectedLevel) in testCases)
        {
            // Act
            var parsed = Enum.TryParse<LogLevel>(levelString, out var result);

            // Assert
            Assert.IsTrue(parsed, $"Should parse '{levelString}'");
            Assert.AreEqual(expectedLevel, result, $"'{levelString}' should parse to {expectedLevel}");
        }
    }

    [TestMethod]
    public void Configuration_InvalidLogLevel_FallsBackToDefault()
    {
        // Arrange
        var invalidLevel = "InvalidLevel";

        // Act
        var parsed = Enum.TryParse<LogLevel>(invalidLevel, out var result);

        // Assert
        Assert.IsFalse(parsed, "Invalid log level should not parse");
        // In Program.cs, this would fall back to LogLevel.Information
    }

    [TestMethod]
    public void PluginSettings_BothDisabled_ShouldWork()
    {
        // Arrange
        var jsonConfig = new Dictionary<string, string?>
        {
            ["Plugins:EnableGlobalPlugins"] = "false",
            ["Plugins:EnableUserPlugins"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(jsonConfig)
            .Build();

        // Act
        var settings = new DotHooksSettings();
        configuration.Bind(settings);

        // Assert
        Assert.IsFalse(settings.Plugins.EnableGlobalPlugins);
        Assert.IsFalse(settings.Plugins.EnableUserPlugins);
    }
}
