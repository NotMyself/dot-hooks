#:package Microsoft.CodeAnalysis.CSharp@4.12.0
#:package System.CommandLine@2.0.0-beta4.22272.1
#:package Microsoft.Extensions.Logging@10.0.0
#:package Microsoft.Extensions.Logging.Console@10.0.0
#:package Microsoft.Extensions.DependencyInjection@10.0.0

using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
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
    builder.AddConsole(options =>
    {
        // Force all console output to stderr to keep stdout clean for HookOutput JSON
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
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
#pragma warning disable IL2026, IL3050 // Reflection-based JSON serialization required for hook I/O
var jsonOptions = new JsonSerializerOptions
{
    TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
};
#pragma warning restore IL2026, IL3050

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

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "JSON serialization needed for hook input/output")]
[UnconditionalSuppressMessage("Trimming", "IL2060:UnrecognizedReflectionPattern", Justification = "Dynamic handler invocation requires reflection")]
[UnconditionalSuppressMessage("Trimming", "IL2070:UnrecognizedReflectionPattern", Justification = "Dynamic handler invocation requires reflection")]
[UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "JSON serialization not compatible with AOT")]
async Task<int> HandleHookEventAsync(string eventName)
{
    try
    {
        logger.LogInformation("Processing hook event: {EventName}", eventName);

        // Get event type mapping
        var (inputType, outputType) = EventTypeRegistry.GetTypes(eventName);

        string inputJson;
        using (var reader = new StreamReader(Console.OpenStandardInput()))
        {
            inputJson = await reader.ReadToEndAsync();
        }

        // Deserialize to specific input type
        object? inputObj;
        try
        {
            inputObj = JsonSerializer.Deserialize(inputJson, inputType, jsonOptions);
            if (inputObj == null)
            {
                logger.LogError("Failed to deserialize hook input to {InputType}", inputType.Name);
                inputObj = Activator.CreateInstance(inputType);
            }
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize hook input");
            inputObj = Activator.CreateInstance(inputType);
        }

        // Extract common properties for logging
        var sessionIdProp = inputType.GetProperty("SessionId");
        var cwdProp = inputType.GetProperty("Cwd");
        var sessionId = sessionIdProp?.GetValue(inputObj)?.ToString() ?? string.Empty;
        var cwd = cwdProp?.GetValue(inputObj)?.ToString() ?? string.Empty;

        var userPluginPath = !string.IsNullOrEmpty(cwd)
            ? Path.Combine(cwd, ".claude", "hooks", "dot-hooks")
            : null;

        var stateDirectory = !string.IsNullOrEmpty(cwd)
            ? Path.Combine(cwd, ".claude", "state")
            : Path.Combine(Directory.GetCurrentDirectory(), ".claude", "state");

        string? sessionLogFile = null;
        string? pluginLogDirectory = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            var sessionDirectory = Path.Combine(stateDirectory, sessionId);
            Directory.CreateDirectory(sessionDirectory);
            sessionLogFile = Path.Combine(sessionDirectory, "dot-hooks.log");

            pluginLogDirectory = Path.Combine(sessionDirectory, "plugins");
            Directory.CreateDirectory(pluginLogDirectory);

            await File.AppendAllTextAsync(sessionLogFile,
                $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Session: {sessionId}, Event: {eventName}\n");
        }

        // Load handler types
        var handlerTypes = await pluginLoader.LoadHandlerTypesAsync(globalPluginPath, userPluginPath);

        // Get handlers for this specific event
        var handlers = pluginLoader.GetHandlersForEvent(handlerTypes, inputType, outputType);

        logger.LogInformation("Found {Count} handler(s) for event {EventName}", handlers.Count, eventName);

        var outputs = new List<HookOutputBase>();
        foreach (var handler in handlers)
        {
            var handlerName = pluginLoader.GetHandlerName(handler);
            string? pluginLogFile = null;
            if (pluginLogDirectory != null)
            {
                pluginLogFile = Path.Combine(pluginLogDirectory, $"{handlerName}.log");
            }

            try
            {
                var timestamp = DateTime.UtcNow;
                logger.LogDebug("Executing handler: {HandlerName}", handlerName);

                if (sessionLogFile != null)
                {
                    await File.AppendAllTextAsync(sessionLogFile,
                        $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] Executing plugin: {handlerName}\n");
                }

                if (pluginLogFile != null)
                {
                    await File.AppendAllTextAsync(pluginLogFile,
                        $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] Event: {eventName}\n");
                    await File.AppendAllTextAsync(pluginLogFile,
                        $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] Session: {sessionId}\n");

                    // Log tool name if this is a tool event
                    if (inputType == typeof(ToolEventInput))
                    {
                        var toolNameProp = inputType.GetProperty("ToolName");
                        var toolName = toolNameProp?.GetValue(inputObj)?.ToString();
                        if (!string.IsNullOrEmpty(toolName))
                        {
                            await File.AppendAllTextAsync(pluginLogFile,
                                $"[{timestamp:yyyy-MM-dd HH:mm:ss.fff}] Tool: {toolName}\n");
                        }
                    }
                }

                // Invoke HandleAsync method
                var handleMethod = handler.GetType().GetMethod("HandleAsync");
                if (handleMethod != null)
                {
                    var task = handleMethod.Invoke(handler, new[] { inputObj, CancellationToken.None }) as Task;
                    if (task != null)
                    {
                        await task;
                        var resultProperty = task.GetType().GetProperty("Result");
                        var output = resultProperty?.GetValue(task) as HookOutputBase;

                        if (output != null)
                        {
                            outputs.Add(output);

                            var completionTimestamp = DateTime.UtcNow;
                            var duration = (completionTimestamp - timestamp).TotalMilliseconds;

                            if (sessionLogFile != null)
                            {
                                await File.AppendAllTextAsync(sessionLogFile,
                                    $"[{completionTimestamp:yyyy-MM-dd HH:mm:ss.fff}] Plugin {handlerName} completed: decision={output.Decision}, continue={output.Continue}\n");
                            }

                            if (pluginLogFile != null)
                            {
                                await File.AppendAllTextAsync(pluginLogFile,
                                    $"[{completionTimestamp:yyyy-MM-dd HH:mm:ss.fff}] Completed: decision={output.Decision}, continue={output.Continue}, duration={duration:F2}ms\n\n");
                            }

                            if (!output.Continue || output.Decision == "block")
                            {
                                logger.LogWarning("Handler {HandlerName} blocked execution: {Reason}",
                                    handlerName, output.StopReason);

                                if (sessionLogFile != null)
                                {
                                    await File.AppendAllTextAsync(sessionLogFile,
                                        $"[{completionTimestamp:yyyy-MM-dd HH:mm:ss.fff}] Plugin {handlerName} BLOCKED: {output.StopReason}\n");
                                }

                                if (pluginLogFile != null)
                                {
                                    await File.AppendAllTextAsync(pluginLogFile,
                                        $"[{completionTimestamp:yyyy-MM-dd HH:mm:ss.fff}] BLOCKED: {output.StopReason}\n\n");
                                }

                                var blockingJson = JsonSerializer.Serialize(output, outputType, jsonOptions);
                                await Console.Out.WriteAsync(blockingJson);
                                return 2;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var errorTimestamp = DateTime.UtcNow;
                logger.LogError(ex, "Handler {HandlerName} threw an exception", handlerName);

                if (sessionLogFile != null)
                {
                    await File.AppendAllTextAsync(sessionLogFile,
                        $"[{errorTimestamp:yyyy-MM-dd HH:mm:ss.fff}] Plugin {handlerName} ERROR: {ex.Message}\n");
                }

                if (pluginLogFile != null)
                {
                    await File.AppendAllTextAsync(pluginLogFile,
                        $"[{errorTimestamp:yyyy-MM-dd HH:mm:ss.fff}] ERROR: {ex.Message}\n");
                    await File.AppendAllTextAsync(pluginLogFile,
                        $"[{errorTimestamp:yyyy-MM-dd HH:mm:ss.fff}] Stack trace: {ex.StackTrace}\n\n");
                }
            }
        }

        var aggregatedOutput = AggregateOutputs(outputs, outputType);
        var outputJson = JsonSerializer.Serialize(aggregatedOutput, outputType, jsonOptions);
        await Console.Out.WriteAsync(outputJson);

        logger.LogInformation("Hook event {EventName} completed successfully", eventName);

        if (sessionLogFile != null)
        {
            await File.AppendAllTextAsync(sessionLogFile,
                $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] Event {eventName} completed successfully\n\n");
        }

        return 0;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception in hook event {EventName}", eventName);
        await Console.Error.WriteLineAsync($"Error: {ex.Message}");
        return 2;
    }
}

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Dynamic type instantiation requires reflection")]
[UnconditionalSuppressMessage("Trimming", "IL2070:UnrecognizedReflectionPattern", Justification = "Dynamic type instantiation requires reflection")]
HookOutputBase AggregateOutputs(List<HookOutputBase> outputs, Type outputType)
{
    if (outputs.Count == 0)
    {
        // Create default success output
        var successMethod = typeof(HookOutputBase).GetMethod(nameof(HookOutputBase.Success))!;
        var genericSuccess = successMethod.MakeGenericMethod(outputType);
        return (HookOutputBase)genericSuccess.Invoke(null, null)!;
    }

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

    // Create output instance
    var output = Activator.CreateInstance(outputType) as HookOutputBase;
    return output! with
    {
        Decision = "approve",
        Continue = true,
        AdditionalContext = combinedContext,
        SystemMessage = combinedMessage
    };
}
// =============================================================================
// SHARED TYPES - Used by both Program.cs and tests
// =============================================================================

// Base Types
public abstract record HookInputBase<TOutput> where TOutput : HookOutputBase
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string TranscriptPath { get; init; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; init; } = string.Empty;

    [JsonPropertyName("permission_mode")]
    public string PermissionMode { get; init; } = string.Empty;
}

public abstract record HookOutputBase
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

    // Factory methods for common outputs
    public static TOutput Success<TOutput>() where TOutput : HookOutputBase, new()
        => new() { Decision = "approve", Continue = true };

    public static TOutput Block<TOutput>(string reason) where TOutput : HookOutputBase, new()
        => new() { Decision = "block", Continue = false, StopReason = reason };

    public static TOutput WithContext<TOutput>(string context) where TOutput : HookOutputBase, new()
        => new() { Decision = "approve", Continue = true, AdditionalContext = context };
}

// Event-Specific Types
public record ToolEventInput : HookInputBase<ToolEventOutput>
{
    [JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = string.Empty;

    [JsonPropertyName("tool_parameters")]
    public Dictionary<string, object>? ToolParameters { get; init; }
}

public record ToolEventOutput : HookOutputBase
{
}

public record SessionEventInput : HookInputBase<SessionEventOutput>
{
}

public record SessionEventOutput : HookOutputBase
{
}

public record GenericEventInput : HookInputBase<GenericEventOutput>
{
    [JsonPropertyName("additional_data")]
    public Dictionary<string, object>? AdditionalData { get; init; }
}

public record GenericEventOutput : HookOutputBase
{
}

// Generic Handler Interface
public interface IHookEventHandler<TInput, TOutput>
    where TInput : HookInputBase<TOutput>
    where TOutput : HookOutputBase
{
    string Name { get; }
    Task<TOutput> HandleAsync(TInput input, CancellationToken cancellationToken = default);
}

// Event Type Registry
public static class EventTypeRegistry
{
    private static readonly Dictionary<string, (Type InputType, Type OutputType)> _eventTypes = new()
    {
        // Tool events
        ["pre-tool-use"] = (typeof(ToolEventInput), typeof(ToolEventOutput)),
        ["post-tool-use"] = (typeof(ToolEventInput), typeof(ToolEventOutput)),

        // Session events
        ["session-start"] = (typeof(SessionEventInput), typeof(SessionEventOutput)),
        ["session-end"] = (typeof(SessionEventInput), typeof(SessionEventOutput)),

        // Generic events
        ["user-prompt-submit"] = (typeof(GenericEventInput), typeof(GenericEventOutput)),
        ["notification"] = (typeof(GenericEventInput), typeof(GenericEventOutput)),
        ["stop"] = (typeof(GenericEventInput), typeof(GenericEventOutput)),
        ["subagent-stop"] = (typeof(GenericEventInput), typeof(GenericEventOutput)),
        ["pre-compact"] = (typeof(GenericEventInput), typeof(GenericEventOutput))
    };

    public static (Type InputType, Type OutputType) GetTypes(string eventName)
    {
        if (_eventTypes.TryGetValue(eventName, out var types))
        {
            return types;
        }

        throw new ArgumentException($"Unknown event type: {eventName}", nameof(eventName));
    }

    public static bool IsValidEvent(string eventName) => _eventTypes.ContainsKey(eventName);
}

// =============================================================================
// PLUGIN LOADER - Used by both Program.cs and tests
// =============================================================================

public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    private readonly IServiceProvider _serviceProvider;

    public PluginLoader(ILogger<PluginLoader> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task<List<Type>> LoadHandlerTypesAsync(string globalPluginPath, string? userPluginPath = null)
    {
        var handlerTypes = new List<Type>();

        if (Directory.Exists(globalPluginPath))
        {
            _logger.LogDebug("Loading global plugins from: {Path}", globalPluginPath);
            var globalTypes = await LoadHandlerTypesFromDirectoryAsync(globalPluginPath);
            handlerTypes.AddRange(globalTypes);
        }

        if (!string.IsNullOrEmpty(userPluginPath) && Directory.Exists(userPluginPath))
        {
            _logger.LogDebug("Loading user plugins from: {Path}", userPluginPath);
            var userTypes = await LoadHandlerTypesFromDirectoryAsync(userPluginPath);
            handlerTypes.AddRange(userTypes);
        }

        _logger.LogInformation("Discovered {Count} handler type(s)", handlerTypes.Count);
        return handlerTypes;
    }

    private async Task<List<Type>> LoadHandlerTypesFromDirectoryAsync(string directory)
    {
        var handlerTypes = new List<Type>();
        var sourceFiles = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)
            .ToList();

        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                _logger.LogDebug("Compiling plugin: {File}", Path.GetFileName(sourceFile));
                var types = await CompileAndDiscoverHandlersAsync(sourceFile);
                handlerTypes.AddRange(types);
                _logger.LogInformation("Discovered {Count} handler(s) from {File}", types.Count, Path.GetFileName(sourceFile));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load plugin from {File}", sourceFile);
            }
        }

        return handlerTypes;
    }

    public List<object> GetHandlersForEvent(List<Type> handlerTypes, Type inputType, Type outputType)
    {
        var handlerInterfaceType = typeof(IHookEventHandler<,>).MakeGenericType(inputType, outputType);

        var matchingHandlers = handlerTypes
            .Where(t => handlerInterfaceType.IsAssignableFrom(t))
            .Select(t => CreateHandlerInstance(t))
            .OfType<object>() // Filter out nulls and convert to non-nullable
            .OrderBy(h => GetHandlerName(h))
            .ToList();

        return matchingHandlers;
    }

    public string GetHandlerName(object handler)
    {
        var nameProperty = handler.GetType().GetProperty("Name");
        return nameProperty?.GetValue(handler)?.ToString() ?? handler.GetType().Name;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Dynamic plugin instantiation requires reflection")]
    [UnconditionalSuppressMessage("Trimming", "IL2077:UnrecognizedReflectionPattern", Justification = "Dynamic plugin instantiation requires reflection")]
    private object? CreateHandlerInstance(Type handlerType)
    {
        try
        {
            // Get all constructors ordered by parameter count (prefer more specific)
            var constructors = handlerType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .ToList();

            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                var args = new List<object?>();
                bool canConstruct = true;

                foreach (var param in parameters)
                {
                    var service = _serviceProvider.GetService(param.ParameterType);
                    if (service == null && !param.IsOptional)
                    {
                        canConstruct = false;
                        break;
                    }
                    args.Add(service ?? param.DefaultValue);
                }

                if (canConstruct)
                {
                    return constructor.Invoke(args.ToArray());
                }
            }

            _logger.LogWarning("Could not resolve dependencies for {Type}", handlerType.Name);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create instance of {Type}", handlerType.Name);
            return null;
        }
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path", Justification = "File-based app using dotnet run, not single-file deployment")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Dynamic plugin compilation requires reflection")]
    [UnconditionalSuppressMessage("Trimming", "IL2072:UnrecognizedReflectionPattern", Justification = "Dynamic plugin instantiation requires reflection")]
    [UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern", Justification = "Dynamic plugin compilation requires reflection")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "Dynamic plugin compilation not compatible with AOT")]
    private async Task<List<Type>> CompileAndDiscoverHandlersAsync(string sourceFile)
    {
        var sourceCode = await File.ReadAllTextAsync(sourceFile);

        // Prepend implicit usings if not already present
        if (!sourceCode.Contains("using System;"))
        {
            var usings = @"using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

";
            sourceCode = usings + sourceCode;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, new CSharpParseOptions(LanguageVersion.CSharp12));

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IHookEventHandler<,>).Assembly.Location),
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

            return new List<Type>();
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(ms);

        // Discover all types implementing IHookEventHandler<,>
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(IHookEventHandler<,>)))
            .ToList();

        if (handlerTypes.Count == 0)
        {
            _logger.LogWarning("No IHookEventHandler<,> implementations found in {File}", Path.GetFileName(sourceFile));
        }

        return handlerTypes;
    }
}
