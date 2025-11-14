# dot-hooks - Claude Code Hooks Plugin Specification

## Overview
dot-hooks is a .NET 10 CLI-based Claude Code plugin that provides a framework for executing custom hooks during Claude Code sessions. It supports both global plugins (shipped with the plugin) and user-defined plugins (per-project customization).

## Architecture

### Execution Model
- **Runtime**: File-based .NET 10 app using `dotnet run Program.cs` (JIT compilation)
- **Plugin System**: Roslyn-based dynamic compilation of C# source files
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **CLI Framework**: System.CommandLine
- **Logging**: Microsoft.Extensions.Logging with structured output

### Plugin Types
1. **Global Plugins**: Shipped with dot-hooks in `hooks/plugins/*.cs`
   - Always enabled
   - Execute first (alphabetical order)
   - Examples: logging, session tracking

2. **User Plugins**: Per-project customization in `.claude/hooks/dot-hooks/*.cs`
   - Optional, project-specific
   - Execute after global plugins (alphabetical order)
   - No testing support initially (future enhancement)

## Project Structure

```
dot-hooks/
├── .claude-plugin/
│   ├── plugin.json          # Plugin metadata
│   └── marketplace.json     # GitHub marketplace configuration
├── hooks/
│   ├── Program.cs           # File-based entry point (#:package directives)
│   ├── PluginLoader.cs      # Roslyn compilation service
│   ├── IHookPlugin.cs       # Plugin interface contract
│   ├── Models.cs            # HookInput/Output record types
│   ├── hooks.json           # Claude Code hook event configuration
│   └── plugins/             # Global plugins directory
│       └── HookLogger.cs    # Example: logs hook event names
├── tests/
│   ├── DotHooks.Tests.csproj  # MSTest project
│   ├── PluginLoaderTests.cs
│   ├── ModelsTests.cs
│   └── Plugins/
│       └── HookLoggerTests.cs
├── .gitignore
├── LICENSE                  # MIT License
├── README.md
├── CLAUDE.md               # Project requirements (existing)
└── SPEC.md                 # This file
```

## User Project Structure (Example)

```
user-project/
├── .claude/
│   ├── hooks/
│   │   └── dot-hooks/
│   │       └── UserHookLogger.cs  # User's custom plugin
│   └── state/
│       └── <session-id>/          # Session-specific directory
│           ├── dot-hooks.log      # Main session log (all plugins)
│           └── plugins/           # Per-plugin execution logs
│               ├── HookLogger.log
│               └── UserHookLogger.log
├── src/
└── ...
```

## Technology Stack

### Runtime
- .NET 10
- C# 14
- Windows 11
- WSL (Windows Subsystem for Linux)
- PowerShell 7+ (pwsh.exe)

### Dependencies (Program.cs #:package directives)
- `Microsoft.CodeAnalysis.CSharp` - Roslyn compiler for plugin compilation
- `System.CommandLine` - CLI argument parsing
- `Microsoft.Extensions.Logging` - Structured logging
- `Microsoft.Extensions.Logging.Console` - Console log output
- `Microsoft.Extensions.DependencyInjection` - DI container

### Testing Stack
- Microsoft Testing Platform
- MSTest (test framework)
- NSubstitute (mocking)

### Methodology
- Test Driven Development (TDD)
- SOLID Principles

## Hook Events

All 9 Claude Code hook events are supported:

1. **PreToolUse** - Before tool execution (can block)
2. **PostToolUse** - After successful tool completion
3. **UserPromptSubmit** - When user submits a prompt
4. **Notification** - When Claude sends notifications
5. **Stop** - When main agent finishes
6. **SubagentStop** - When subagent finishes
7. **SessionStart** - Session initialization
8. **SessionEnd** - When session ends
9. **PreCompact** - Before context compaction

## Plugin Interface

```csharp
public interface IHookPlugin
{
    string Name { get; }
    Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default);
}
```

### Plugin Logging

Plugins can optionally accept an `ILogger` instance via constructor injection:

```csharp
// With logger (recommended)
public class MyPlugin(ILogger logger) : IHookPlugin
{
    public string Name => "MyPlugin";

    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing {EventType}", input.EventType);
        return Task.FromResult(HookOutput.Success());
    }
}

// Without logger (fallback)
public class SimplePlugin : IHookPlugin
{
    public string Name => "SimplePlugin";

    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken)
    {
        return Task.FromResult(HookOutput.Success());
    }
}
```

The plugin loader automatically detects and injects loggers using `ILoggerFactory.CreateLogger(Type)`.

## Data Models

### HookInput
- `SessionId` - Current session identifier
- `TranscriptPath` - Path to session transcript
- `Cwd` - Current working directory
- `PermissionMode` - Permission level
- Event-specific fields (tool name, parameters, etc.)

### HookOutput
- `Decision` - "approve", "block", "allow", "deny", "ask"
- `Continue` - Boolean continuation flag
- `StopReason` - Blocking message
- `SystemMessage` - Warning/info to Claude
- `AdditionalContext` - Context injection for Claude

## Execution Flow

1. **Hook Trigger**: Claude Code triggers hook event
2. **Invocation**: `dotnet run ${CLAUDE_PLUGIN_ROOT}/hooks/Program.cs -- <event-name>`
3. **Input**: Read JSON from stdin (HookInput)
4. **Plugin Discovery**:
   - Scan `${CLAUDE_PLUGIN_ROOT}/hooks/plugins/*.cs` (global)
   - Scan `${cwd}/.claude/hooks/dot-hooks/*.cs` (user)
5. **Compilation**: Roslyn compiles all .cs files to in-memory assemblies
6. **Loading**: Discover types implementing `IHookPlugin`
7. **Execution**:
   - Inject plugins into DI container
   - Execute global plugins (alphabetical)
   - Execute user plugins (alphabetical)
8. **Aggregation**: Combine plugin outputs
9. **Output**: Write JSON to stdout (HookOutput)
10. **Logging**: Write to `.claude/state/` directories
11. **Exit**: Code 0 (success) or 2 (blocking error)

## Logging Strategy

### Console Output Separation

**Critical**: Console logging is written to **stderr only**. Stdout is reserved exclusively for HookOutput JSON responses to Claude Code.

```csharp
services.AddLogging(builder =>
{
    builder.AddConsole(options =>
    {
        // Force all console output to stderr
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
    builder.SetMinimumLevel(LogLevel.Information);
});
```

### Session Log Files

Each Claude Code session creates two types of log files:

#### 1. Main Session Log (`dot-hooks.log`)
- **Location**: `<project>/.claude/state/<session-id>/dot-hooks.log`
- **Purpose**: Consolidated view of all hook events and plugin executions
- **Format**: UTC timestamps in ISO format `[YYYY-MM-DD HH:mm:ss.fff]`
- **Contents**:
  - Session start/end markers
  - Hook event triggers
  - Plugin execution sequence
  - Plugin completion status (decision, continue)
  - Blocking events
  - Errors and exceptions

**Example**:
```
[2025-11-14 21:29:02.077] Session: abc123, Event: session-start
[2025-11-14 21:29:02.854] Executing plugin: HookLogger
[2025-11-14 21:29:02.855] Plugin HookLogger completed: decision=approve, continue=True
[2025-11-14 21:29:02.857] Event session-start completed successfully
```

#### 2. Per-Plugin Logs (`plugins/<PluginName>.log`)
- **Location**: `<project>/.claude/state/<session-id>/plugins/<PluginName>.log`
- **Purpose**: Individual plugin execution history and performance tracking
- **Format**: UTC timestamps in ISO format `[YYYY-MM-DD HH:mm:ss.fff]`
- **Contents**:
  - Event type and session ID
  - Tool name (when applicable)
  - Execution duration in milliseconds
  - Plugin-specific decisions
  - Full error details with stack traces
  - Blocking reasons

**Example**:
```
[2025-11-14 21:29:31.755] Event: pre-tool-use
[2025-11-14 21:29:31.755] Session: abc123
[2025-11-14 21:29:31.755] Tool: Write
[2025-11-14 21:29:31.756] Completed: decision=approve, continue=True, duration=1.05ms

```

### Log Levels
- **Trace**: Not used (all logs go to stderr)
- **Debug**: Plugin discovery, compilation details
- **Information**: Hook execution, plugin results, normal operations
- **Warning**: Non-critical issues, plugin blocking decisions
- **Error**: Failures, exceptions, blocking conditions

### Performance Metrics

Per-plugin logs automatically track execution duration:
- Start timestamp captured before `ExecuteAsync` call
- End timestamp captured after completion
- Duration calculated: `(endTime - startTime).TotalMilliseconds`
- Logged with 2 decimal precision: `duration=1.05ms`

## Marketplace Configuration

### Plugin Metadata (.claude-plugin/plugin.json)
```json
{
  "name": "dot-hooks",
  "version": "1.0.0",
  "description": ".NET CLI tool for Claude Code hooks on Windows and WSL",
  "author": {
    "name": "Bobby Johnson",
    "email": "bobby@notmyself.io"
  },
  "repository": "https://github.com/NotMyself/dot-hooks",
  "license": "MIT",
  "keywords": ["hooks", "dotnet", "windows", "wsl", "automation"],
  "hooks": "./hooks/hooks.json"
}
```

### Marketplace Configuration (.claude-plugin/marketplace.json)
```json
{
  "name": "notmyself-marketplace",
  "version": "1.0.0",
  "description": "NotMyself Claude Code Marketplace",
  "owner": {
    "name": "Bobby Johnson",
    "email": "bobby@notmyself.io"
  },
  "plugins": [
    {
      "name": "dot-hooks",
      "source": "./",
      "version": "1.0.0",
      "description": ".NET CLI tool for Claude Code hooks",
      "author": {
        "name": "Bobby Johnson"
      },
      "homepage": "https://github.com/NotMyself/dot-hooks",
      "repository": "https://github.com/NotMyself/dot-hooks",
      "license": "MIT",
      "keywords": ["hooks", "dotnet", "windows", "wsl"]
    }
  ]
}
```

## Installation

Users install from GitHub marketplace:
```bash
/plugin marketplace add NotMyself/dot-hooks
```

## Example Plugins

### Global Plugin: HookLogger.cs
Logs the name of each hook event being executed.

### User Plugin: UserHookLogger.cs (README example)
Demonstrates user plugin development with custom logging that identifies execution from user's project directory.

## Testing Strategy

### Unit Tests
- **PluginLoaderTests**: Test plugin discovery and Roslyn compilation
- **ModelsTests**: Test JSON serialization/deserialization
- **HookLoggerTests**: Test example plugin functionality

### Test Project Configuration
- Uses `<Compile Include="../hooks/**/*.cs" Exclude="../hooks/Program.cs" />` to share source files
- MSTest with Microsoft Testing Platform
- NSubstitute for mocking dependencies

### TDD Approach
- Write tests first
- Implement to pass tests
- Refactor following SOLID principles

## Future Enhancements

1. **User Plugin Testing**: Support for testing user-space plugins
2. **Plugin Ordering**: Configuration-based execution order
3. **Plugin Disable**: Selective global plugin disabling
4. **Plugin Discovery Caching**: Performance optimization
5. **Configuration File**: `.claude/hooks/dot-hooks/config.json` for plugin settings
6. **NuGet Package**: DotHooks.Abstractions for easier user plugin development

## Security Considerations

- Validate all file paths to prevent traversal attacks
- Sandbox plugin execution (future)
- Log all plugin executions for audit trail
- Quote shell variables properly
- Use absolute paths where possible

## Cross-Platform Compatibility

### Windows 11
- Native .NET 10 runtime
- PowerShell 7+ for scripting

### WSL (Windows Subsystem for Linux)
- .NET 10 runtime in WSL environment
- Bash shell for hook execution
- Shared file system access

## Performance Targets

- Plugin discovery: < 100ms
- Roslyn compilation: < 500ms per plugin
- Total hook execution: < 1s for typical scenarios
- Memory: < 100MB working set

## Error Handling

### Exit Codes
- `0`: Success
- `2`: Blocking error (stderr sent to Claude)
- Other: Non-blocking error (logged but doesn't block)

### Error Scenarios
- Missing dependencies: Log and continue with available plugins
- Compilation errors: Log detailed error, skip plugin
- Runtime exceptions: Catch, log, return error in HookOutput
- Invalid JSON: Log and return default safe output

## License

MIT License - See LICENSE file for full text.

## Metadata

- **Author**: Bobby Johnson (bobby@notmyself.io)
- **Repository**: https://github.com/NotMyself/dot-hooks
- **Version**: 1.0.0
- **Created**: 2025-11-14
- **Last Updated**: 2025-11-14
