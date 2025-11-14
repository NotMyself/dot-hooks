using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.Loader;

namespace DotHooks;

/// <summary>
/// Service responsible for discovering and compiling plugin source files.
/// </summary>
public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;

    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Discovers and loads plugins from specified directories.
    /// </summary>
    /// <param name="globalPluginPath">Path to global plugins directory.</param>
    /// <param name="userPluginPath">Path to user plugins directory (optional).</param>
    /// <returns>List of loaded plugin instances.</returns>
    public async Task<List<IHookPlugin>> LoadPluginsAsync(string globalPluginPath, string? userPluginPath = null)
    {
        var plugins = new List<IHookPlugin>();

        // Load global plugins
        if (Directory.Exists(globalPluginPath))
        {
            _logger.LogDebug("Loading global plugins from: {Path}", globalPluginPath);
            var globalPlugins = await LoadPluginsFromDirectoryAsync(globalPluginPath);
            plugins.AddRange(globalPlugins);
        }

        // Load user plugins
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

        // Parse the syntax tree
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, new CSharpParseOptions(LanguageVersion.CSharp12));

        // Get references from the current assembly and required dependencies
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IHookPlugin).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Linq").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location),
            MetadataReference.CreateFromFile(Assembly.Load("Microsoft.Extensions.Logging.Abstractions").Location),
        };

        // Add System.Text.Json reference if needed
        try
        {
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Text.Json").Location));
        }
        catch
        {
            // Optional dependency
        }

        // Compile the code
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

        // Load the assembly and create an instance
        ms.Seek(0, SeekOrigin.Begin);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(ms);

        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IHookPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        if (pluginType == null)
        {
            _logger.LogWarning("No IHookPlugin implementation found in {File}", Path.GetFileName(sourceFile));
            return null;
        }

        return Activator.CreateInstance(pluginType) as IHookPlugin;
    }
}
