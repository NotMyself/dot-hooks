# dot-hooks

A .NET 10 CLI-based plugin framework for Claude Code hooks that enables extensible automation during Claude Code sessions. Works seamlessly on both Windows 11 and WSL.

## Features

- **Plugin Architecture**: Extensible framework supporting global and user-defined plugins
- **Runtime Compilation**: Dynamic plugin loading using Roslyn - no build step required
- **Cross-Platform**: Runs on Windows 11 and WSL environments
- **File-Based Execution**: Uses `dotnet run` for JIT compilation
- **All Hook Events**: Supports all 9 Claude Code hook events
- **Structured Logging**: Session and general logs for debugging and auditing
- **Test-Driven**: Full MSTest coverage with Microsoft Testing Platform

## Installation

Install from GitHub marketplace:

```bash
/plugin marketplace add NotMyself/claude-dotnet-marketplace
```

## Requirements

- .NET 10 SDK
- Windows 11 or WSL
- Claude Code CLI

## Quick Start

After installation, dot-hooks will automatically execute on Claude Code hook events. The example `HookLogger` global plugin logs hook activity to console and files.

### View Logs

Session state and logs are stored in your project:

```
.claude/state/<session-id>/
├── dot-hooks.log              # Main session log (all plugins)
└── plugins/
    ├── HookLogger.log         # Per-plugin execution log
    └── YourPlugin.log         # Individual plugin logs
```

**Log Types:**
- **Main Session Log** (`dot-hooks.log`): Consolidated view of all hook events and plugin executions
- **Per-Plugin Logs** (`plugins/*.log`): Individual plugin execution history with event context, execution time, and errors

All logs use UTC timestamps in ISO format: `[YYYY-MM-DD HH:mm:ss.fff]`

**Important:** Console logging goes to stderr only. Stdout is reserved for HookOutput JSON responses.

## Architecture

### Plugin Types

1. **Global Plugins** - Shipped with dot-hooks, always enabled
   - Located in `hooks/plugins/`
   - Execute first (alphabetical order)
   - Examples: logging, session tracking

2. **User Plugins** - Project-specific customization
   - Located in `<project>/.claude/hooks/dot-hooks/`
   - Execute after global plugins (alphabetical order)
   - Compiled dynamically at runtime

### Hook Events Supported

All 9 Claude Code hook events:

- `PreToolUse` - Before tool execution (can block)
- `PostToolUse` - After successful tool completion
- `UserPromptSubmit` - When user submits prompt
- `Notification` - When Claude sends notifications
- `Stop` - When main agent finishes
- `SubagentStop` - When subagent finishes
- `SessionStart` - Session initialization
- `SessionEnd` - When session ends
- `PreCompact` - Before context compaction

## Creating User Plugins

### 1. Create Plugin Directory

In your project root:

```bash
mkdir -p .claude/hooks/dot-hooks
```

### 2. Create Your Plugin

Create a `.cs` file in `.claude/hooks/dot-hooks/`. Example `UserToolLogger.cs`:

```csharp
namespace MyProject.Hooks;

/// <summary>
/// User plugin that logs tool events with project context.
/// Plugins implement IHookEventHandler for each event type they handle.
/// </summary>
public class UserToolLogger(ILogger logger) :
    IHookEventHandler<ToolEventInput, ToolEventOutput>
{
    public string Name => "UserToolLogger";

    public Task<ToolEventOutput> HandleAsync(ToolEventInput input, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Tool event from user directory");
        logger.LogInformation("Tool: {ToolName}", input.ToolName);
        logger.LogInformation("Project: {Cwd}", input.Cwd);

        // Add custom logic here
        if (input.ToolName == "Write")
        {
            logger.LogInformation("About to write a file!");
        }

        return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
    }
}
```

**Note**: Plugins can request any registered service via constructor injection (ILogger, ILoggerFactory, etc.). Parameterless constructors also work.

### 3. Plugin Interface

All plugins must implement `IHookPlugin`:

```csharp
public interface IHookPlugin
{
    string Name { get; }
    Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default);
}
```

### 4. Plugin Logging

Plugins can optionally receive an `ILogger` instance for structured logging:

**With Logger (Recommended):**
```csharp
public class MyPlugin(ILogger logger) : IHookPlugin
{
    public string Name => "MyPlugin";

    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing event: {EventType}", input.EventType);
        logger.LogDebug("Session: {SessionId}", input.SessionId);
        return Task.FromResult(HookOutput.Success());
    }
}
```

**Without Logger:**
```csharp
public class SimplePlugin : IHookPlugin
{
    public string Name => "SimplePlugin";

    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken)
    {
        Console.WriteLine("Processing event");
        return Task.FromResult(HookOutput.Success());
    }
}
```

Available log levels: `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`, `LogCritical`

### 5. Input and Output Models

**HookInput** - Data received from Claude Code:

```csharp
public record HookInput
{
    public string SessionId { get; init; }
    public string TranscriptPath { get; init; }
    public string Cwd { get; init; }
    public string PermissionMode { get; init; }
    public string EventType { get; init; }
    public string? ToolName { get; init; }
    public Dictionary<string, object>? ToolParameters { get; init; }
    // ... additional fields
}
```

**HookOutput** - Response sent back to Claude Code:

```csharp
public record HookOutput
{
    public string? Decision { get; init; }        // "approve", "block", "allow", "deny", "ask"
    public bool Continue { get; init; }           // Continue execution?
    public string? StopReason { get; init; }      // Blocking message
    public string? SystemMessage { get; init; }   // Warning/info to Claude
    public string? AdditionalContext { get; init; } // Context for Claude
}
```

**Helper Methods:**

```csharp
// Approve and continue
return HookOutput.Success();

// Block execution
return HookOutput.Block("File too large");

// Add context for Claude
return HookOutput.WithContext("User has admin privileges");
```

## Plugin Examples

### Example 1: File Size Validator

Blocks file writes over 1MB:

```csharp
using DotHooks;

public class FileSizeValidator : IHookPlugin
{
    public string Name => "FileSizeValidator";

    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
    {
        if (input.EventType == "pre-tool-use" && input.ToolName == "Write")
        {
            // Check file size from tool parameters
            if (input.ToolParameters?.TryGetValue("content", out var content) == true)
            {
                var contentStr = content?.ToString() ?? "";
                var sizeInBytes = System.Text.Encoding.UTF8.GetByteCount(contentStr);

                if (sizeInBytes > 1_000_000) // 1MB
                {
                    return Task.FromResult(HookOutput.Block(
                        $"File size ({sizeInBytes:N0} bytes) exceeds 1MB limit"));
                }
            }
        }

        return Task.FromResult(HookOutput.Success());
    }
}
```

### Example 2: Git Branch Protector

Warns when working on main branch:

```csharp
using DotHooks;
using System.Diagnostics;

public class GitBranchProtector : IHookPlugin
{
    public string Name => "GitBranchProtector";

    public async Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
    {
        if (input.EventType == "pre-tool-use" &&
            (input.ToolName == "Write" || input.ToolName == "Edit"))
        {
            var branchName = await GetCurrentBranchAsync(input.Cwd);

            if (branchName == "main" || branchName == "master")
            {
                return HookOutput.WithContext(
                    $"⚠️ Warning: You are making changes on the '{branchName}' branch!");
            }
        }

        return HookOutput.Success();
    }

    private async Task<string> GetCurrentBranchAsync(string cwd)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "branch --show-current",
                    WorkingDirectory = cwd,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var branch = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return branch.Trim();
        }
        catch
        {
            return "unknown";
        }
    }
}
```

### Example 3: Session Notifier

Logs session start/end:

```csharp
using DotHooks;

public class SessionNotifier : IHookPlugin
{
    public string Name => "SessionNotifier";

    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
    {
        if (input.EventType == "session-start")
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"[SessionNotifier] Session started at {timestamp}");
            Console.WriteLine($"[SessionNotifier] Session ID: {input.SessionId}");
            Console.WriteLine($"[SessionNotifier] Project: {Path.GetFileName(input.Cwd)}");
        }
        else if (input.EventType == "session-end")
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Console.WriteLine($"[SessionNotifier] Session ended at {timestamp}");
        }

        return Task.FromResult(HookOutput.Success());
    }
}
```

## Development

### Building

This is a file-based .NET 10 app - no build step needed for execution. For testing:

```bash
dotnet test tests/DotHooks.Tests.csproj
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity detailed

# Run specific test
dotnet test --filter "FullyQualifiedName~HookLoggerTests"
```

### Project Structure

```
dot-hooks/
├── .claude-plugin/          # Plugin metadata
├── hooks/                   # Main source
│   ├── Program.cs          # Entry point
│   ├── PluginLoader.cs     # Roslyn compilation
│   ├── IHookPlugin.cs      # Plugin interface
│   ├── Models.cs           # Data models
│   ├── hooks.json          # Hook configuration
│   └── plugins/            # Global plugins
│       └── HookLogger.cs
├── tests/                   # MSTest tests
└── SPEC.md                 # Detailed specification
```

## Technology Stack

- .NET 10
- C# 14
- Microsoft.CodeAnalysis.CSharp (Roslyn)
- System.CommandLine
- Microsoft.Extensions.Logging
- Microsoft.Extensions.DependencyInjection
- MSTest + Microsoft Testing Platform
- NSubstitute

## Methodology

- Test Driven Development (TDD)
- SOLID Principles

## Logging

Session state directory:
- **Session directory**: `<project>/.claude/state/<session-id>/` - Each session gets its own directory where plugins can write logs and state files

Adjust log level by modifying `Program.cs`:

```csharp
builder.SetMinimumLevel(LogLevel.Debug); // Information, Debug, Trace, etc.
```

## Troubleshooting

### Plugin Not Loading

1. Check file is in correct directory: `.claude/hooks/dot-hooks/*.cs`
2. Verify file implements `IHookPlugin` interface
3. Check logs in `.claude/state/<session-id>/`
4. Ensure .NET 10 SDK is installed

### Compilation Errors

Check logs for Roslyn compilation errors:

```bash
# List session directories
ls .claude/state/

# Check latest session for errors (plugins write to console which Claude captures)
```

Common issues:
- Missing `using DotHooks;` statement
- Incorrect interface implementation
- Syntax errors in C# code

### Hook Not Triggering

1. Verify hook is configured in `hooks/hooks.json`
2. Check timeout setting (default 30 seconds)
3. Ensure `CLAUDE_PLUGIN_ROOT` environment variable is set
4. Review Claude Code hook configuration

## Contributing

Contributions welcome! Please:

1. Follow TDD methodology
2. Maintain SOLID principles
3. Add tests for new features
4. Update documentation

## License

MIT License - see [LICENSE](LICENSE) file

## Author

Bobby Johnson (bobby@notmyself.io)

## Links

- Repository: https://github.com/NotMyself/dot-hooks
- Issues: https://github.com/NotMyself/dot-hooks/issues
- Claude Code Docs: https://docs.claude.com/

## Version

0.2.0 (Prototype/Proof of Concept)

**Breaking Changes**: v0.2.0 introduces strongly-typed event handlers. Plugins from v0.1.0 are not compatible. See SPEC.md Migration Guide for upgrade instructions.
