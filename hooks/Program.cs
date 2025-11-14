#:package Microsoft.CodeAnalysis.CSharp@4.12.0
#:package System.CommandLine@2.0.0-beta4.22272.1
#:package Microsoft.Extensions.Logging@10.0.0
#:package Microsoft.Extensions.Logging.Console@10.0.0
#:package Microsoft.Extensions.DependencyInjection@10.0.0

using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DotHooks;

// Setup dependency injection
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

// Get plugin root path from environment variable
var pluginRoot = Environment.GetEnvironmentVariable("CLAUDE_PLUGIN_ROOT") ?? Directory.GetCurrentDirectory();
var globalPluginPath = Path.Combine(pluginRoot, "hooks", "plugins");

// Setup root command
var rootCommand = new RootCommand("dot-hooks - Claude Code Hooks Plugin");

// Create hook event commands
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

// Parse and invoke
return await rootCommand.InvokeAsync(args);

async Task<int> HandleHookEventAsync(string eventName)
{
    try
    {
        logger.LogInformation("Processing hook event: {EventName}", eventName);

        // Read input from stdin
        string inputJson;
        using (var reader = new StreamReader(Console.OpenStandardInput()))
        {
            inputJson = await reader.ReadToEndAsync();
        }

        HookInput input;
        try
        {
            input = JsonSerializer.Deserialize<HookInput>(inputJson) ?? new HookInput();
            input = input with { EventType = eventName };
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to deserialize hook input");
            input = new HookInput { EventType = eventName };
        }

        // Determine user plugin path
        var userPluginPath = !string.IsNullOrEmpty(input.Cwd)
            ? Path.Combine(input.Cwd, ".claude", "hooks", "dot-hooks")
            : null;

        // Setup logging to file
        var logDirectory = !string.IsNullOrEmpty(input.Cwd)
            ? Path.Combine(input.Cwd, ".claude", "state")
            : Path.Combine(Directory.GetCurrentDirectory(), ".claude", "state");

        Directory.CreateDirectory(logDirectory);

        var sessionLogDirectory = Path.Combine(logDirectory, "session");
        Directory.CreateDirectory(sessionLogDirectory);

        // Load plugins
        var plugins = await pluginLoader.LoadPluginsAsync(globalPluginPath, userPluginPath);

        // Execute plugins
        var outputs = new List<HookOutput>();
        foreach (var plugin in plugins)
        {
            try
            {
                logger.LogDebug("Executing plugin: {PluginName}", plugin.Name);
                var output = await plugin.ExecuteAsync(input, CancellationToken.None);
                outputs.Add(output);

                // Check for blocking output
                if (!output.Continue || output.Decision == "block")
                {
                    logger.LogWarning("Plugin {PluginName} blocked execution: {Reason}",
                        plugin.Name, output.StopReason);

                    // Write blocking output
                    var blockingJson = JsonSerializer.Serialize(output);
                    await Console.Out.WriteAsync(blockingJson);
                    return 2; // Blocking error code
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Plugin {PluginName} threw an exception", plugin.Name);
                // Continue with other plugins
            }
        }

        // Aggregate outputs (combine all successful outputs)
        var aggregatedOutput = AggregateOutputs(outputs);

        // Write output to stdout
        var outputJson = JsonSerializer.Serialize(aggregatedOutput);
        await Console.Out.WriteAsync(outputJson);

        logger.LogInformation("Hook event {EventName} completed successfully", eventName);
        return 0; // Success
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception in hook event {EventName}", eventName);

        // Write error to stderr (will be sent to Claude)
        await Console.Error.WriteLineAsync($"Error: {ex.Message}");
        return 2; // Blocking error
    }
}

HookOutput AggregateOutputs(List<HookOutput> outputs)
{
    if (outputs.Count == 0)
        return HookOutput.Success();

    // Combine additional context from all plugins
    var allContext = outputs
        .Where(o => !string.IsNullOrEmpty(o.AdditionalContext))
        .Select(o => o.AdditionalContext)
        .ToList();

    var combinedContext = allContext.Count > 0
        ? string.Join("\n\n", allContext)
        : null;

    // Combine system messages
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
