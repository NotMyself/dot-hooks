using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Serialization;

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
