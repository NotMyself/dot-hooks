using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.Loader;

// =============================================================================
// PLUGIN LOADER - Used by both Program.cs and tests
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

    [UnconditionalSuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path", Justification = "File-based app using dotnet run, not single-file deployment")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "Dynamic plugin compilation requires reflection")]
    [UnconditionalSuppressMessage("Trimming", "IL2072:UnrecognizedReflectionPattern", Justification = "Dynamic plugin instantiation requires reflection")]
    [UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern", Justification = "Dynamic plugin compilation requires reflection")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "Dynamic plugin compilation not compatible with AOT")]
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
