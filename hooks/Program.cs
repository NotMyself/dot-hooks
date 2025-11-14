#:package Microsoft.CodeAnalysis.CSharp@4.12.0
#:package System.CommandLine@2.0.0-beta4.22272.1
#:package Microsoft.Extensions.Logging@10.0.0
#:package Microsoft.Extensions.Logging.Console@10.0.0
#:package Microsoft.Extensions.DependencyInjection@10.0.0

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;

// =============================================================================
// MAIN PROGRAM (must be first - top-level statements)
// =============================================================================

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
services.AddSingleton<PluginLoader>();

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
var pluginLoader = serviceProvider.GetRequiredService<PluginLoader>();

// Determine plugin root - if env var not set and we're in hooks dir, go up one level
var pluginRoot = Environment.GetEnvironmentVariable("CLAUDE_PLUGIN_ROOT");
if (string.IsNullOrEmpty(pluginRoot))
{
    var currentDir = Directory.GetCurrentDirectory();
    // If current directory ends with "hooks", go up one level
    pluginRoot = Path.GetFileName(currentDir) == "hooks"
        ? Path.GetDirectoryName(currentDir) ?? currentDir
        : currentDir;
}

var globalPluginPath = Path.Combine(pluginRoot, "hooks", "plugins");

// JSON serializer options with reflection enabled
var jsonOptions = new JsonSerializerOptions
{
    TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
};

var rootCommand = new RootCommand("dot-hooks - Claude Code Hooks Plugin");

var hookEvents = new[]
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

foreach (var eventName in hookEvents)
{
    var command = new Command(eventName, $"Handle {eventName} hook event");
    command.SetHandler(async () => await HandleHookEventAsync(eventName));
    rootCommand.AddCommand(command);
}

return await rootCommand.InvokeAsync(args);

async Task<int> HandleHookEventAsync(string eventName)
{
    try
    {
        logger.LogInformation("Processing hook event: {EventName}", eventName);

        string inputJson;
        using (var reader = new StreamReader(Console.OpenStandardInput()))
        {
            inputJson = await reader.ReadToEndAsync();
        }

        HookInput input;
        try
        {
            input = JsonSerializer.Deserialize<HookInput>(inputJson, jsonOptions) ?? new HookInput();
            input = input with { EventType = eventName };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize hook input");
            input = new HookInput { EventType = eventName };
        }

        var userPluginPath = !string.IsNullOrEmpty(input.Cwd)
            ? Path.Combine(input.Cwd, ".claude", "hooks", "dot-hooks")
            : null;

        var stateDirectory = !string.IsNullOrEmpty(input.Cwd)
            ? Path.Combine(input.Cwd, ".claude", "state")
            : Path.Combine(Directory.GetCurrentDirectory(), ".claude", "state");

        if (!string.IsNullOrEmpty(input.SessionId))
        {
            var sessionDirectory = Path.Combine(stateDirectory, input.SessionId);
            Directory.CreateDirectory(sessionDirectory);
        }

        var plugins = await pluginLoader.LoadPluginsAsync(globalPluginPath, userPluginPath);

        var outputs = new List<HookOutput>();
        foreach (var plugin in plugins)
        {
            try
            {
                logger.LogDebug("Executing plugin: {PluginName}", plugin.Name);
                var output = await plugin.ExecuteAsync(input, CancellationToken.None);
                outputs.Add(output);

                if (!output.Continue || output.Decision == "block")
                {
                    logger.LogWarning("Plugin {PluginName} blocked execution: {Reason}",
                        plugin.Name, output.StopReason);

                    var blockingJson = JsonSerializer.Serialize(output, jsonOptions);
                    await Console.Out.WriteAsync(blockingJson);
                    return 2;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Plugin {PluginName} threw an exception", plugin.Name);
            }
        }

        var aggregatedOutput = AggregateOutputs(outputs);
        var outputJson = JsonSerializer.Serialize(aggregatedOutput, jsonOptions);
        await Console.Out.WriteAsync(outputJson);

        logger.LogInformation("Hook event {EventName} completed successfully", eventName);
        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception in hook event {EventName}", eventName);
        await Console.Error.WriteLineAsync($"Error: {ex.Message}");
        return 2;
    }
}

HookOutput AggregateOutputs(List<HookOutput> outputs)
{
    if (outputs.Count == 0)
        return HookOutput.Success();

    var allContext = outputs
        .Where(o => !string.IsNullOrEmpty(o.AdditionalContext))
        .Select(o => o.AdditionalContext)
        .ToList();

    var combinedContext = allContext.Count > 0
        ? string.Join("\n\n", allContext)
        : null;

    var allMessages = outputs
        .Where(o => !string.IsNullOrEmpty(o.SystemMessage))
        .Select(o => o.SystemMessage)
        .ToList();

    var combinedMessage = allMessages.Count > 0
        ? string.Join("\n", allMessages)
        : null;

    return new HookOutput
    {
        Decision = "approve",
        Continue = true,
        AdditionalContext = combinedContext,
        SystemMessage = combinedMessage
    };
}

// =============================================================================
// MODELS (after top-level statements)
// =============================================================================

public record HookInput
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string TranscriptPath { get; init; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; init; } = string.Empty;

    [JsonPropertyName("permission_mode")]
    public string PermissionMode { get; init; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    [JsonPropertyName("tool_parameters")]
    public Dictionary<string, object>? ToolParameters { get; init; }

    [JsonPropertyName("additional_data")]
    public Dictionary<string, object>? AdditionalData { get; init; }
}

public record HookOutput
{
    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("continue")]
    public bool Continue { get; init; } = true;

    [JsonPropertyName("stopReason")]
    public string? StopReason { get; init; }

    [JsonPropertyName("systemMessage")]
    public string? SystemMessage { get; init; }

    [JsonPropertyName("additionalContext")]
    public string? AdditionalContext { get; init; }

    public static HookOutput Success() => new() { Decision = "approve", Continue = true };
    public static HookOutput Block(string reason) => new() { Decision = "block", Continue = false, StopReason = reason };
    public static HookOutput WithContext(string context) => new() { Decision = "approve", Continue = true, AdditionalContext = context };
}

// =============================================================================
// PLUGIN INTERFACE
// =============================================================================

public interface IHookPlugin
{
    string Name { get; }
    Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default);
}

// =============================================================================
// PLUGIN LOADER
// =============================================================================

public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public PluginLoader(ILogger<PluginLoader> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<List<IHookPlugin>> LoadPluginsAsync(string globalPluginPath, string? userPluginPath = null)
    {
        var plugins = new List<IHookPlugin>();

        if (Directory.Exists(globalPluginPath))
        {
            _logger.LogDebug("Loading global plugins from: {Path}", globalPluginPath);
            var globalPlugins = await LoadPluginsFromDirectoryAsync(globalPluginPath);
            plugins.AddRange(globalPlugins);
        }

        if (!string.IsNullOrEmpty(userPluginPath) && Directory.Exists(userPluginPath))
        {
            _logger.LogDebug("Loading user plugins from: {Path}", userPluginPath);
            var userPlugins = await LoadPluginsFromDirectoryAsync(userPluginPath);
            plugins.AddRange(userPlugins);
        }

        _logger.LogInformation("Loaded {Count} plugin(s)", plugins.Count);
        return plugins;
    }

    private async Task<List<IHookPlugin>> LoadPluginsFromDirectoryAsync(string directory)
    {
        var plugins = new List<IHookPlugin>();
        var sourceFiles = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)
            .ToList();

        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                _logger.LogDebug("Compiling plugin: {File}", Path.GetFileName(sourceFile));
                var plugin = await CompileAndLoadPluginAsync(sourceFile);
                if (plugin != null)
                {
                    plugins.Add(plugin);
                    _logger.LogInformation("Loaded plugin: {Name}", plugin.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin from {File}", sourceFile);
            }
        }

        return plugins;
    }

    private async Task<IHookPlugin?> CompileAndLoadPluginAsync(string sourceFile)
    {
        var sourceCode = await File.ReadAllTextAsync(sourceFile);

        // Prepend implicit usings if not already present
        if (!sourceCode.Contains("using System;"))
        {
            var usings = @"using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

";
            sourceCode = usings + sourceCode;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, new CSharpParseOptions(LanguageVersion.CSharp12));

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IHookPlugin).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ILogger).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Linq").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location),
        };

        try
        {
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Text.Json").Location));
        }
        catch { }

        var compilation = CSharpCompilation.Create(
            assemblyName: Path.GetFileNameWithoutExtension(sourceFile),
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var failures = result.Diagnostics.Where(diagnostic =>
                diagnostic.IsWarningAsError ||
                diagnostic.Severity == DiagnosticSeverity.Error);

            foreach (var diagnostic in failures)
            {
                _logger.LogError("Compilation error in {File}: {Error}",
                    Path.GetFileName(sourceFile), diagnostic.GetMessage());
            }

            return null;
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(ms);

        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IHookPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        if (pluginType == null)
        {
            _logger.LogWarning("No IHookPlugin implementation found in {File}", Path.GetFileName(sourceFile));
            return null;
        }

        // Try to create plugin with logger if constructor accepts it
        var pluginLogger = _loggerFactory.CreateLogger(pluginType);
        var loggerConstructor = pluginType.GetConstructor(new[] { typeof(ILogger) });

        if (loggerConstructor != null)
        {
            return loggerConstructor.Invoke(new object[] { pluginLogger }) as IHookPlugin;
        }

        // Fall back to parameterless constructor
        return Activator.CreateInstance(pluginType) as IHookPlugin;
    }
}
