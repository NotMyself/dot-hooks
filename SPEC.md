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

The strongly-typed plugin interface uses C# generics to provide compile-time type safety for hook events:

```csharp
public interface IHookEventHandler<TInput, TOutput>
    where TInput : HookInputBase<TOutput>
    where TOutput : HookOutputBase
{
    string Name { get; }
    Task<TOutput> HandleAsync(TInput input, CancellationToken cancellationToken = default);
}
```

### Key Features

**Compile-Time Type Safety**: The constraint `where TInput : HookInputBase<TOutput>` ensures that input and output types are correctly paired. The compiler prevents type mismatches.

**Event-Specific Handling**: Plugins only execute for events they care about by implementing specific handler interfaces. No need to check `EventType` strings.

**Multiple Event Support**: A single plugin class can implement multiple `IHookEventHandler<,>` interfaces to handle different event types.

**Dependency Injection**: Plugins can request any registered service via constructor injection (not limited to `ILogger`).

### Single Event Handler Example

```csharp
public class ToolValidator(ILogger logger) :
    IHookEventHandler<ToolEventInput, ToolEventOutput>
{
    public string Name => "ToolValidator";

    public Task<ToolEventOutput> HandleAsync(ToolEventInput input, CancellationToken ct)
    {
        logger.LogInformation("Validating tool: {ToolName}", input.ToolName);

        if (input.ToolName == "Bash")
        {
            // Validation logic
        }

        return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
    }
}
```

### Multiple Event Handler Example

For plugins handling multiple events, use explicit interface implementation to avoid ambiguity:

```csharp
public class SessionLogger(ILogger logger) :
    IHookEventHandler<SessionEventInput, SessionEventOutput>,
    IHookEventHandler<ToolEventInput, ToolEventOutput>
{
    public string Name => "SessionLogger";

    // Explicit interface implementation for session events
    Task<SessionEventOutput> IHookEventHandler<SessionEventInput, SessionEventOutput>.HandleAsync(
        SessionEventInput input, CancellationToken ct)
    {
        logger.LogInformation("Session event in {Cwd}", input.Cwd);
        return Task.FromResult(HookOutputBase.Success<SessionEventOutput>());
    }

    // Explicit interface implementation for tool events
    Task<ToolEventOutput> IHookEventHandler<ToolEventInput, ToolEventOutput>.HandleAsync(
        ToolEventInput input, CancellationToken ct)
    {
        logger.LogInformation("Tool {ToolName} in session {SessionId}",
            input.ToolName, input.SessionId);
        return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
    }
}
```

### Plugin Dependency Injection

The plugin loader supports full dependency injection via constructor parameters. The DI container can inject any registered service:

**Common Services:**
- `ILogger` - Structured logging (recommended for all plugins)
- `ILoggerFactory` - Create custom loggers with specific categories
- `IConfiguration` - Access configuration settings (future enhancement)
- Custom services registered in `Program.cs`

**Examples:**

```csharp
// Logger only (most common)
public class SimplePlugin(ILogger logger) :
    IHookEventHandler<ToolEventInput, ToolEventOutput>
{
    public string Name => "SimplePlugin";
    // ...
}

// Multiple services
public class AdvancedPlugin(ILogger logger, ILoggerFactory loggerFactory) :
    IHookEventHandler<ToolEventInput, ToolEventOutput>
{
    private readonly ILogger _customLogger;

    public AdvancedPlugin(ILogger logger, ILoggerFactory loggerFactory)
    {
        _customLogger = loggerFactory.CreateLogger("Custom.Category");
    }

    public string Name => "AdvancedPlugin";
    // ...
}

// No dependencies (parameterless constructor)
public class MinimalPlugin : IHookEventHandler<SessionEventInput, SessionEventOutput>
{
    public string Name => "MinimalPlugin";

    public Task<SessionEventOutput> HandleAsync(SessionEventInput input, CancellationToken ct)
    {
        // Can still use Console.Error.WriteLine for basic logging
        return Task.FromResult(HookOutputBase.Success<SessionEventOutput>());
    }
}
```

The plugin loader automatically detects constructor parameters and resolves them from the DI container. If a service is not registered, plugin loading fails with a clear error message.

## Data Models

The strongly-typed data model uses a hierarchical structure with base types and grouped event-specific types.

### Base Types

**HookInputBase<TOutput>** - Abstract base for all input types

```csharp
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
```

**HookOutputBase** - Abstract base for all output types

```csharp
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
```

### Grouped Event Types

Events are grouped by their data requirements to minimize type proliferation while maintaining type safety.

**ToolEventInput** - For tool-related events (pre-tool-use, post-tool-use)

```csharp
public record ToolEventInput : HookInputBase<ToolEventOutput>
{
    [JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = string.Empty;

    [JsonPropertyName("tool_parameters")]
    public Dictionary<string, object>? ToolParameters { get; init; }
}

public record ToolEventOutput : HookOutputBase
{
    // Inherits all base properties
}
```

**SessionEventInput** - For session lifecycle events (session-start, session-end)

```csharp
public record SessionEventInput : HookInputBase<SessionEventOutput>
{
    // Only uses base properties - no additional fields needed
}

public record SessionEventOutput : HookOutputBase
{
    // Inherits all base properties
}
```

**GenericEventInput** - For other events (user-prompt-submit, notification, stop, subagent-stop, pre-compact)

```csharp
public record GenericEventInput : HookInputBase<GenericEventOutput>
{
    [JsonPropertyName("additional_data")]
    public Dictionary<string, object>? AdditionalData { get; init; }
}

public record GenericEventOutput : HookOutputBase
{
    // Inherits all base properties
}
```

### Event Type Mapping

| Event | Input Type | Output Type | Description |
|-------|-----------|-------------|-------------|
| `pre-tool-use` | `ToolEventInput` | `ToolEventOutput` | Before tool execution - can block |
| `post-tool-use` | `ToolEventInput` | `ToolEventOutput` | After successful tool completion |
| `session-start` | `SessionEventInput` | `SessionEventOutput` | Session initialization |
| `session-end` | `SessionEventInput` | `SessionEventOutput` | When session ends |
| `user-prompt-submit` | `GenericEventInput` | `GenericEventOutput` | When user submits prompt |
| `notification` | `GenericEventInput` | `GenericEventOutput` | When Claude sends notifications |
| `stop` | `GenericEventInput` | `GenericEventOutput` | When main agent finishes |
| `subagent-stop` | `GenericEventInput` | `GenericEventOutput` | When subagent finishes |
| `pre-compact` | `GenericEventInput` | `GenericEventOutput` | Before context compaction |

### Output Decision Values

- **"approve"** - Allow operation to proceed (default)
- **"block"** - Prevent operation (only for pre-tool-use and pre-compact)
- **"allow"** - Explicitly allow (synonym for approve)
- **"deny"** - Explicitly deny (synonym for block)
- **"ask"** - Prompt user for decision (future enhancement)

## Execution Flow

1. **Hook Trigger**: Claude Code triggers hook event
2. **Invocation**: `dotnet run ${CLAUDE_PLUGIN_ROOT}/hooks/Program.cs -- <event-name>`
3. **Event Type Mapping**: Map event name to Input/Output type pair using type registry
   - `pre-tool-use` → `(ToolEventInput, ToolEventOutput)`
   - `session-start` → `(SessionEventInput, SessionEventOutput)`
   - etc.
4. **Input Deserialization**: Read JSON from stdin and deserialize to specific Input type
5. **Plugin Discovery**:
   - Scan `${CLAUDE_PLUGIN_ROOT}/hooks/plugins/*.cs` (global plugins)
   - Scan `${cwd}/.claude/hooks/dot-hooks/*.cs` (user plugins)
6. **Compilation**: Roslyn compiles all .cs files to in-memory assemblies
7. **Handler Discovery**: Discover types implementing `IHookEventHandler<TInput, TOutput>` for the current event
   - Reflection scans for generic interface implementations
   - Extracts type parameters to match Input/Output types
   - Filters to only handlers matching the current event
8. **Dependency Injection**: For each handler:
   - Analyze constructor parameters
   - Resolve dependencies from DI container
   - Create handler instance with injected dependencies
9. **Execution**:
   - Execute global plugin handlers first (alphabetical by Name)
   - Execute user plugin handlers second (alphabetical by Name)
   - Pass strongly-typed Input to each handler
   - Collect strongly-typed Output from each handler
10. **Output Aggregation**: Combine handler outputs using base properties:
    - Concatenate `AdditionalContext` (double newline separator)
    - Concatenate `SystemMessage` (single newline separator)
    - First handler that blocks wins (stops further execution)
    - Default to "approve" if no handler blocks
11. **Output Serialization**: Write aggregated Output as JSON to stdout
12. **Logging**: Write execution details to session logs:
    - Main log: `.claude/state/<session-id>/dot-hooks.log`
    - Per-plugin logs: `.claude/state/<session-id>/plugins/<PluginName>.log`
13. **Exit**: Code 0 (success) or 2 (blocking error)

### Type Discovery Details

The plugin loader uses reflection to discover handlers for each event:

```csharp
// Pseudocode for handler discovery
var eventType = GetEventTypeFromArgs(); // "pre-tool-use"
var (inputType, outputType) = EventTypeRegistry.GetTypes(eventType); // (ToolEventInput, ToolEventOutput)

var handlerInterfaceType = typeof(IHookEventHandler<,>).MakeGenericType(inputType, outputType);

var handlers = assembly.GetTypes()
    .Where(t => t.IsClass && !t.IsAbstract)
    .Where(t => handlerInterfaceType.IsAssignableFrom(t))
    .Select(t => CreateHandlerInstance(t, serviceProvider))
    .OrderBy(h => h.Name);
```

### Execution Order

1. **Global handlers** execute first in alphabetical order by `Name` property
2. **User handlers** execute second in alphabetical order by `Name` property
3. Within each group, deterministic alphabetical ordering ensures predictable behavior
4. If any handler blocks, execution stops immediately (short-circuit evaluation)

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
  "version": "0.2.0",
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
  "version": "0.2.0",
  "description": "NotMyself Claude Code Marketplace",
  "owner": {
    "name": "Bobby Johnson",
    "email": "bobby@notmyself.io"
  },
  "plugins": [
    {
      "name": "dot-hooks",
      "source": "./",
      "version": "0.2.0",
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
/plugin marketplace add NotMyself/claude-dotnet-marketplace
```

## Release and Distribution

### Overview

dot-hooks is distributed as a Claude Code plugin via GitHub. Unlike traditional .NET projects that distribute compiled binaries, Claude Code plugins distribute **source code** that users execute directly. This affects the release process and version management.

### Key Principles

1. **Source Distribution**: Raw `.cs` files are the deliverable (no DLLs or compiled artifacts)
2. **GitHub-Based**: Users install directly from GitHub repository
3. **Marketplace System**: Updates propagate via `/plugin marketplace update`
4. **Version Synchronization**: Multiple files must stay in sync for each release
5. **Git Tags Drive Releases**: Pushing tags triggers automated release workflows

### Version Synchronization

**Critical**: Keep version numbers synchronized across all metadata files:

```bash
.claude-plugin/plugin.json         # Line 3: "version": "0.2.0"
.claude-plugin/marketplace.json    # Lines 3 and 13: "version": "0.2.0" (2 places!)
README.md                          # Line 468: Version section
```

**Automated Verification**: The release workflow includes a version consistency check that fails if versions don't match the git tag.

### GitHub Actions Workflows

#### CI Workflow (.github/workflows/ci.yml)

Runs on every push and pull request to validate code quality:

```yaml
name: CI

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore dependencies
        run: dotnet restore tests/DotHooks.Tests.csproj

      - name: Build
        run: dotnet build tests/DotHooks.Tests.csproj --configuration Release --no-restore

      - name: Test
        run: dotnet test tests/DotHooks.Tests.csproj --configuration Release --no-build --verbosity normal

      - name: Validate plugin manifest
        run: |
          # Check plugin.json is valid JSON
          jq empty .claude-plugin/plugin.json || exit 1
          jq empty .claude-plugin/marketplace.json || exit 1

          # Check versions match
          PLUGIN_VERSION=$(jq -r '.version' .claude-plugin/plugin.json)
          MARKETPLACE_VERSION=$(jq -r '.version' .claude-plugin/marketplace.json)

          if [ "$PLUGIN_VERSION" != "$MARKETPLACE_VERSION" ]; then
            echo "Version mismatch between plugin.json and marketplace.json"
            exit 1
          fi
```

#### Release Workflow (.github/workflows/release.yml)

Triggered by pushing version tags (e.g., `v0.2.0`):

```yaml
name: Release Claude Code Plugin

on:
  push:
    tags:
      - 'v*'

jobs:
  release:
    runs-on: ubuntu-latest
    permissions:
      contents: write

    steps:
      - name: Checkout code
        uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Run tests
        run: dotnet test tests/DotHooks.Tests.csproj --configuration Release

      - name: Verify version consistency
        run: |
          PLUGIN_VERSION=$(jq -r '.version' .claude-plugin/plugin.json)
          MARKETPLACE_VERSION=$(jq -r '.version' .claude-plugin/marketplace.json)
          TAG_VERSION=${GITHUB_REF#refs/tags/v}

          if [ "$PLUGIN_VERSION" != "$TAG_VERSION" ] || [ "$MARKETPLACE_VERSION" != "$TAG_VERSION" ]; then
            echo "Version mismatch!"
            echo "plugin.json: $PLUGIN_VERSION"
            echo "marketplace.json: $MARKETPLACE_VERSION"
            echo "Git tag: $TAG_VERSION"
            exit 1
          fi

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          generate_release_notes: true
          body: |
            ## Installation

            ```bash
            /plugin marketplace add NotMyself/claude-dotnet-marketplace
            ```

            ## Update Existing Installation

            ```bash
            /plugin marketplace update notmyself-marketplace
            ```
          files: |
            LICENSE
            README.md
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

### Release Process Checklist

Follow these steps for each release:

#### 1. Update Version Numbers

Edit all version references to match the new version (e.g., `0.3.0`):

```bash
# Files to update:
.claude-plugin/plugin.json (line 3)
.claude-plugin/marketplace.json (lines 3 and 13)
README.md (line 468 - Version section)
```

#### 2. Update CHANGELOG.md

Document changes following [Keep a Changelog](https://keepachangelog.com/) format:

```markdown
## [0.3.0] - 2025-XX-XX

### Added
- New feature descriptions

### Changed
- Modified behavior descriptions

### Fixed
- Bug fix descriptions

### Breaking Changes
- Breaking change descriptions and migration instructions
```

#### 3. Run Tests Locally

```bash
dotnet test tests/DotHooks.Tests.csproj --configuration Release
```

Ensure all tests pass before proceeding.

#### 4. Commit Version Changes

```bash
git add .claude-plugin/plugin.json .claude-plugin/marketplace.json README.md CHANGELOG.md
git commit -m "chore: bump version to 0.3.0"
git push origin main
```

#### 5. Create and Push Tag

```bash
# Create annotated tag
git tag -a v0.3.0 -m "Release v0.3.0"

# Push tag to trigger release workflow
git push origin v0.3.0
```

#### 6. Verify Release

1. **GitHub Actions**: Check workflow runs successfully
2. **GitHub Release**: Verify release created with correct version
3. **Release Notes**: Review auto-generated notes
4. **Version Check**: Confirm version consistency passed

### CHANGELOG.md Format

Maintain a changelog following semantic versioning principles:

```markdown
# Changelog

All notable changes to dot-hooks will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Features being worked on

## [0.2.0] - 2025-11-15

### Added
- Strongly-typed event handler system
- Event-specific input/output contracts
- Per-plugin logging infrastructure

### Changed
- Migrated from monolithic `IHookPlugin` to `IHookEventHandler<TInput, TOutput>`

### Breaking Changes
- Plugins from v0.1.0 are not compatible with v0.2.0
- See Migration Guide for upgrade instructions

## [0.1.0] - 2024-XX-XX

### Added
- Initial release
- Basic plugin framework
- HookLogger global plugin
- Runtime compilation with Roslyn

[Unreleased]: https://github.com/NotMyself/dot-hooks/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/NotMyself/dot-hooks/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/NotMyself/dot-hooks/releases/tag/v0.1.0
```

### How Users Update

When a new release is published, users update their installation:

```bash
# In Claude Code CLI
/plugin marketplace update notmyself-marketplace
```

**Important**: This pulls the latest code from the **main branch**, not necessarily tagged releases. Consider:

1. **Option A (Recommended)**: Only merge to `main` when ready to release
2. **Option B**: Use a `release` branch that users install from

### Distribution Notes

#### Source vs. Binaries

**No Build Artifacts**: Unlike NuGet packages or traditional .NET distributions, Claude Code plugins:
- Do **not** publish compiled DLLs
- Distribute raw `.cs` source files
- Rely on user's .NET runtime for JIT compilation
- Execute via `dotnet run` on user's machine

**Implications**:
- Users need .NET 10 SDK installed
- Source code is visible to users
- Runtime compilation happens on each execution (until .NET caching)
- Plugin updates are immediate (no reinstall required)

#### File-Based App Execution

The `hooks/` directory structure is executed as-is:
```
hooks/
├── Program.cs          # Entry point
├── PluginLoader.cs     # Included at runtime
├── SharedTypes.cs      # Included at runtime
├── hooks.json          # Configuration
└── plugins/            # Global plugins
    └── HookLogger.cs
```

**Note**: Currently uses .NET 10 file-based app model with `#:package` directives. Multi-file support is limited to a single execution context via implicit compilation.

#### Breaking Changes

**Document Clearly**: Since users can't easily roll back versions:
- Mark breaking changes explicitly in CHANGELOG.md
- Provide migration guides (see Migration Guide section)
- Consider semantic versioning for compatibility signals
  - MAJOR version: Breaking changes (0.x.x → 1.0.0)
  - MINOR version: New features, backward compatible (0.2.x → 0.3.0)
  - PATCH version: Bug fixes (0.2.0 → 0.2.1)

#### README Installation Instructions

Always include exact marketplace path in README:

```bash
/plugin marketplace add NotMyself/claude-dotnet-marketplace
```

**Format**: `owner/repo-name` (GitHub repository path)

### GitHub Repository Settings

Ensure proper configuration for automated releases:

1. **Actions Permissions**:
   - Settings → Actions → General
   - Workflow permissions: "Read and write permissions"
   - Allow GitHub Actions to create releases

2. **Branch Protection** (Optional but Recommended):
   - Protect `main` branch
   - Require status checks (CI workflow)
   - Require pull request reviews

3. **Secrets** (Pre-configured):
   - `GITHUB_TOKEN`: Automatically provided by GitHub Actions

### Semantic Versioning Strategy

Follow [Semantic Versioning 2.0.0](https://semver.org/):

- **0.x.x**: Prototype/proof of concept phase (current)
- **1.0.0**: Production-ready, stable API
- **1.x.x**: Backward-compatible features
- **2.0.0**: Next breaking change

**For dot-hooks**:
- v0.1.0 → v0.2.0: Breaking change (new type system)
- v0.2.0 → v0.3.0: Planned features (backward compatible)
- v0.x.x → v1.0.0: Stable API commitment

### Troubleshooting Releases

**Problem**: Version check fails during release
- **Solution**: Verify all version strings match exactly (no extra spaces)
- **Check**: Run `jq -r '.version' .claude-plugin/plugin.json` locally

**Problem**: Tests fail in CI but pass locally
- **Solution**: Ensure .NET 10 SDK is installed locally
- **Check**: Run `dotnet --version` (should be 10.0.x)

**Problem**: Release workflow doesn't trigger
- **Solution**: Verify tag format is `v*` (e.g., `v0.2.0`, not `0.2.0`)
- **Check**: Use annotated tags: `git tag -a v0.2.0 -m "message"`

**Problem**: Users report old version after update
- **Solution**: Ensure latest code is on `main` branch
- **Check**: GitHub repository default branch is `main`

## Example Plugins

### Global Plugin: HookLogger.cs

Basic logger that tracks all tool-related and session events:

```csharp
public class HookLogger(ILogger logger) :
    IHookEventHandler<ToolEventInput, ToolEventOutput>,
    IHookEventHandler<SessionEventInput, SessionEventOutput>
{
    public string Name => "HookLogger";

    Task<ToolEventOutput> IHookEventHandler<ToolEventInput, ToolEventOutput>.HandleAsync(
        ToolEventInput input, CancellationToken ct)
    {
        logger.LogInformation("Tool event: {ToolName}", input.ToolName);
        return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
    }

    Task<SessionEventOutput> IHookEventHandler<SessionEventInput, SessionEventOutput>.HandleAsync(
        SessionEventInput input, CancellationToken ct)
    {
        logger.LogInformation("Session event in: {Cwd}", input.Cwd);
        return Task.FromResult(HookOutputBase.Success<SessionEventOutput>());
    }
}
```

### User Plugin Example: DangerousCommandBlocker.cs

Blocks dangerous bash commands:

```csharp
public class DangerousCommandBlocker(ILogger logger) :
    IHookEventHandler<ToolEventInput, ToolEventOutput>
{
    public string Name => "DangerousCommandBlocker";

    private static readonly string[] DangerousPatterns =
    {
        "rm -rf /",
        "dd if=",
        "mkfs",
        ":(){:|:&};:"  // Fork bomb
    };

    public Task<ToolEventOutput> HandleAsync(ToolEventInput input, CancellationToken ct)
    {
        if (input.ToolName == "Bash" &&
            input.ToolParameters?.TryGetValue("command", out var cmdObj) == true)
        {
            var command = cmdObj?.ToString() ?? string.Empty;

            foreach (var pattern in DangerousPatterns)
            {
                if (command.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Blocked dangerous command: {Pattern}", pattern);
                    return Task.FromResult(
                        HookOutputBase.Block<ToolEventOutput>(
                            $"Dangerous command pattern detected: {pattern}"));
                }
            }
        }

        return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
    }
}
```

### User Plugin Example: ContextInjector.cs

Adds project-specific context for Claude:

```csharp
public class ContextInjector(ILogger logger) :
    IHookEventHandler<SessionEventInput, SessionEventOutput>
{
    public string Name => "ContextInjector";

    public Task<SessionEventOutput> HandleAsync(SessionEventInput input, CancellationToken ct)
    {
        var projectName = Path.GetFileName(input.Cwd);
        var context = $"Working in project: {projectName}\nCoding standards: Follow project CLAUDE.md guidelines";

        logger.LogInformation("Injecting context for project: {ProjectName}", projectName);

        return Task.FromResult(
            HookOutputBase.WithContext<SessionEventOutput>(context));
    }
}
```

## Migration Guide: v0.1.0 → v0.2.0

Version 0.2.0 introduces breaking changes to enable strongly-typed event handling. All plugins must be migrated to the new interface.

### Breaking Changes

1. **Interface Removed**: `IHookPlugin` no longer exists
2. **New Interface**: Must implement `IHookEventHandler<TInput, TOutput>`
3. **No EventType Checking**: Event routing is now type-based, not string-based
4. **Grouped Input/Output Types**: Use `ToolEventInput`, `SessionEventInput`, or `GenericEventInput`
5. **Factory Methods**: `HookOutput.Success()` → `HookOutputBase.Success<TOutput>()`

### Migration Steps

#### Step 1: Identify Events Handled

Review your v0.1.0 plugin and identify which events it processes:

**v0.1.0 Code:**
```csharp
public class MyPlugin : IHookPlugin
{
    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken ct)
    {
        if (input.EventType == "pre-tool-use")
        {
            // Handle tool event
        }

        if (input.EventType == "session-start")
        {
            // Handle session event
        }

        return Task.FromResult(HookOutput.Success());
    }
}
```

**Events Identified:** `pre-tool-use` (tool event), `session-start` (session event)

#### Step 2: Map Events to Types

Use the Event Type Mapping table to determine Input/Output types:

- `pre-tool-use` → `ToolEventInput` / `ToolEventOutput`
- `session-start` → `SessionEventInput` / `SessionEventOutput`

#### Step 3: Implement Handler Interfaces

Update your plugin to implement the appropriate handler interfaces:

**v0.2.0 Code:**
```csharp
public class MyPlugin(ILogger logger) :
    IHookEventHandler<ToolEventInput, ToolEventOutput>,
    IHookEventHandler<SessionEventInput, SessionEventOutput>
{
    public string Name => "MyPlugin";

    // Tool event handler
    Task<ToolEventOutput> IHookEventHandler<ToolEventInput, ToolEventOutput>.HandleAsync(
        ToolEventInput input, CancellationToken ct)
    {
        // Handle tool event - no EventType check needed!
        logger.LogInformation("Tool: {ToolName}", input.ToolName);
        return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
    }

    // Session event handler
    Task<SessionEventOutput> IHookEventHandler<SessionEventInput, SessionEventOutput>.HandleAsync(
        SessionEventInput input, CancellationToken ct)
    {
        // Handle session event
        logger.LogInformation("Session in: {Cwd}", input.Cwd);
        return Task.FromResult(HookOutputBase.Success<SessionEventOutput>());
    }
}
```

#### Step 4: Update Factory Methods

Replace generic factory methods with typed versions:

| v0.1.0 | v0.2.0 |
|--------|--------|
| `HookOutput.Success()` | `HookOutputBase.Success<TOutput>()` |
| `HookOutput.Block("reason")` | `HookOutputBase.Block<TOutput>("reason")` |
| `HookOutput.WithContext("context")` | `HookOutputBase.WithContext<TOutput>("context")` |

#### Step 5: Update Property Access

Input properties remain mostly the same, but type-specific properties are now strongly-typed:

| v0.1.0 | v0.2.0 |
|--------|--------|
| `input.ToolName` (nullable) | `input.ToolName` (non-null for `ToolEventInput`) |
| `input.ToolParameters` | `input.ToolParameters` (only on `ToolEventInput`) |
| Check `EventType == "pre-tool-use"` | Implement `IHookEventHandler<ToolEventInput, ToolEventOutput>` |

### Common Migration Patterns

**Pattern 1: Single Event Handler**

```csharp
// v0.1.0
public class ToolPlugin : IHookPlugin
{
    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken ct)
    {
        if (input.EventType != "pre-tool-use") return Task.FromResult(HookOutput.Success());
        // Process tool event
    }
}

// v0.2.0
public class ToolPlugin(ILogger logger) :
    IHookEventHandler<ToolEventInput, ToolEventOutput>
{
    public string Name => "ToolPlugin";

    public Task<ToolEventOutput> HandleAsync(ToolEventInput input, CancellationToken ct)
    {
        // Only called for tool events - no filtering needed!
        // Process tool event
    }
}
```

**Pattern 2: Multi-Event Handler**

```csharp
// v0.1.0
public class MultiPlugin : IHookPlugin
{
    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken ct)
    {
        switch (input.EventType)
        {
            case "pre-tool-use":
                // Handle tool
                break;
            case "session-start":
                // Handle session
                break;
        }
        return Task.FromResult(HookOutput.Success());
    }
}

// v0.2.0
public class MultiPlugin(ILogger logger) :
    IHookEventHandler<ToolEventInput, ToolEventOutput>,
    IHookEventHandler<SessionEventInput, SessionEventOutput>
{
    public string Name => "MultiPlugin";

    Task<ToolEventOutput> IHookEventHandler<ToolEventInput, ToolEventOutput>.HandleAsync(
        ToolEventInput input, CancellationToken ct)
    {
        // Handle tool
    }

    Task<SessionEventOutput> IHookEventHandler<SessionEventInput, SessionEventOutput>.HandleAsync(
        SessionEventInput input, CancellationToken ct)
    {
        // Handle session
    }
}
```

**Pattern 3: Parameterless Constructor**

```csharp
// v0.1.0
public class SimplePlugin : IHookPlugin
{
    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken ct)
    {
        Console.WriteLine("Hello");
        return Task.FromResult(HookOutput.Success());
    }
}

// v0.2.0
public class SimplePlugin : IHookEventHandler<ToolEventInput, ToolEventOutput>
{
    public string Name => "SimplePlugin";

    public Task<ToolEventOutput> HandleAsync(ToolEventInput input, CancellationToken ct)
    {
        Console.Error.WriteLine("Hello"); // Use stderr for console output
        return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
    }
}
```

### Testing Your Migration

1. **Compile Check**: Ensure your plugin compiles without errors
2. **Type Verification**: Verify Input/Output types match via compiler constraints
3. **Runtime Test**: Test with actual Claude Code session (see MANUAL_TEST_GUIDE.md)
4. **Log Review**: Check session logs to verify handlers execute correctly

### Troubleshooting

**Error: "Type 'MyPlugin' does not implement interface member 'IHookEventHandler<,>.HandleAsync'"**
- Solution: Use explicit interface implementation for multiple handlers

**Error: "The type 'MyInput' cannot be used as type parameter 'TInput'"**
- Solution: Verify Input type inherits from `HookInputBase<TOutput>` with correct Output type

**Plugin not executing**
- Check: Does your plugin implement the correct handler interface for the event?
- Check: Is `Name` property implemented and returning non-empty string?
- Check: Review session logs for compilation errors

## Testing Strategy

### Unit Tests

**PluginLoaderTests** - Plugin discovery and compilation
- Test generic interface discovery (`IHookEventHandler<,>`)
- Test type parameter extraction from implemented interfaces
- Test event-to-handler mapping
- Test DI container integration
- Test handler instance creation with dependencies
- Test compilation error handling
- Test multiple interface implementation

**ModelsTests** - Data model serialization
- Test JSON serialization/deserialization for all Input/Output types
- Test base type hierarchy
- Test type constraints (compile-time verification)
- Test factory method functionality (`Success<T>`, `Block<T>`, `WithContext<T>`)
- Test property name mapping (snake_case ↔ PascalCase)

**HandlerTests** - Handler execution
- Test single-event handlers
- Test multi-event handlers with explicit implementation
- Test handler execution order (alphabetical by Name)
- Test output aggregation
- Test blocking behavior (short-circuit evaluation)
- Test cancellation token propagation

**EventTypeRegistryTests** - Event mapping
- Test event name → type mapping
- Test all 9 events map to correct Input/Output types
- Test case sensitivity
- Test unknown event handling

**DependencyInjectionTests** - Service resolution
- Test `ILogger` injection
- Test `ILoggerFactory` injection
- Test parameterless constructor fallback
- Test missing dependency error handling
- Test constructor selection logic

### Integration Tests

**End-to-End Tests** - Full execution flow
- Test stdin JSON → handler execution → stdout JSON
- Test global + user plugin execution order
- Test session log file creation
- Test per-plugin log file creation
- Test exit code behavior (0 vs 2)

### Test Project Configuration
- Uses `<Compile Include="../hooks/**/*.cs" Exclude="../hooks/Program.cs" />` to share source files
- MSTest with Microsoft Testing Platform
- NSubstitute for mocking `ILogger`, `ILoggerFactory`, and other services
- Test fixtures for temporary plugin directories

### TDD Approach
- Write tests first for new type system features
- Implement to pass tests
- Refactor following SOLID principles
- Maintain >80% code coverage

## Future Enhancements

### Version 0.3.0 Candidates

1. **Event-Specific Aggregation**: Custom aggregation logic per event type
   - Interface: `IOutputAggregator<TOutput>`
   - Allow events to define custom output merging strategies
   - Useful for complex decision-making across multiple handlers

2. **Per-Event Plugin Configuration**: Event-specific settings
   - File: `.claude/hooks/dot-hooks/config.json`
   - Configure handlers differently for different events
   - Enable/disable specific handlers per event

3. **Handler Priority/Ordering**: Explicit execution order control
   - Attribute: `[HandlerPriority(100)]`
   - Override alphabetical ordering when needed
   - Useful for dependency chains between handlers

4. **Specific Input/Output Types**: Move beyond grouped types
   - `PreToolUseInput` instead of `ToolEventInput`
   - `PostToolUseInput` with result data
   - Event-specific properties for fine-grained control

5. **Type Discovery Caching**: Performance optimization
   - Cache type registry between invocations
   - Shared cache file in `.claude/state/`
   - Invalidate on plugin file changes

### Version 1.0.0 Candidates

6. **User Plugin Testing Framework**: Test support for user plugins
   - Base test classes for plugin testing
   - Mock Input/Output factories
   - Test execution without full hook infrastructure

7. **NuGet Package**: DotHooks.Abstractions
   - Standalone package for base types
   - User plugins reference package instead of copying code
   - Versioned interface contracts

8. **Plugin Disable/Enable**: Selective plugin control
   - Disable global plugins per-project
   - Enable/disable via configuration
   - Runtime plugin state management

9. **IConfiguration Support**: Full configuration integration
   - Inject `IConfiguration` into handlers
   - Load settings from `appsettings.json`, environment variables
   - Plugin-specific configuration sections

10. **Async Plugin Discovery**: Parallel compilation
    - Compile multiple plugins concurrently
    - Reduce startup latency for large plugin sets
    - Background compilation with caching

## Performance Considerations

The strongly-typed event system introduces additional overhead compared to v0.1.0. Performance targets and optimization strategies:

### Type System Overhead

**Generic Interface Reflection** (~50-100ms per plugin load)
- Scanning assemblies for types implementing `IHookEventHandler<,>`
- Extracting generic type parameters via reflection
- Building event → handler type mapping

**Mitigation:**
- Cache type discovery results in memory
- Reuse assembly metadata across invocations (future: persist to disk)

**DI Container Resolution** (~5-10ms per handler instance)
- Analyzing constructor parameters
- Resolving dependencies from service provider
- Instantiating handler with injected services

**Mitigation:**
- Singleton scope for handlers when safe
- Pre-resolve common dependencies (ILogger, ILoggerFactory)
- Lazy initialization of expensive services

**Type Constraint Validation** (compile-time only)
- No runtime cost - constraints enforced by C# compiler
- Prevents type mismatches before execution

**Multiple Interface Implementation** (minimal overhead)
- Virtual dispatch through interface vtable
- ~1-2ns per call (negligible)

### Event Mapping

**Registry Lookup** (~1-5ms per event)
- Map event name string to Input/Output type pair
- Dictionary lookup: O(1) complexity

**Optimization:**
- Pre-build type registry at startup
- Cache in static field for subsequent invocations

### Handler Filtering

**Interface Matching** (~10-20ms per event)
- Filter handlers to only those implementing current event's interface
- LINQ Where clause over discovered handlers

**Optimization:**
- Index handlers by Input type
- Pre-group handlers by supported events

### Comparison with v0.1.0

| Operation | v0.1.0 | v0.2.0 | Delta |
|-----------|--------|--------|-------|
| Plugin discovery | 50-100ms | 100-150ms | +50ms (type extraction) |
| Plugin compilation | 300-500ms | 300-500ms | No change |
| Plugin instantiation | 1-2ms | 6-12ms | +5-10ms (DI resolution) |
| Event routing | N/A (all plugins) | 10-20ms | +10-20ms (handler filtering) |
| Handler execution | <1ms | <1ms | No change |
| **Total per event** | **~350-600ms** | **~420-680ms** | **+70-80ms** |

### Performance Targets (Updated for v0.2.0)

- Plugin discovery: < 150ms (was <100ms)
- Roslyn compilation: < 500ms per plugin (unchanged)
- Event routing + handler filtering: < 25ms (new)
- Total hook execution: < 1.2s for typical scenarios (was <1s)
- Memory: < 120MB working set (was <100MB)

### Optimization Strategies

1. **Type Discovery Caching** (planned for v0.3.0)
   - Persist type registry to `.claude/state/type-cache.json`
   - Invalidate on plugin file modification time change
   - Reduce discovery time to ~5-10ms on cache hit

2. **Handler Indexing**
   - Build index: `Dictionary<Type, List<IHookEventHandler>>`
   - Key: Input type (e.g., `ToolEventInput`)
   - Value: List of handlers supporting that input
   - O(1) lookup instead of O(n) filtering

3. **Assembly Caching**
   - Keep compiled assemblies in memory
   - Reuse across multiple events in same session
   - Reduces Roslyn overhead for subsequent events

4. **Lazy Service Resolution**
   - Don't resolve optional dependencies until first use
   - Particularly beneficial for `ILoggerFactory`, `IConfiguration`

## Security Considerations

- Validate all file paths to prevent traversal attacks
- Sandbox plugin execution (future)
- Log all plugin executions for audit trail
- Quote shell variables properly
- Use absolute paths where possible
- Type constraints prevent injection of incompatible types (compile-time security)

## Cross-Platform Compatibility

### Windows 11
- Native .NET 10 runtime
- PowerShell 7+ for scripting

### WSL (Windows Subsystem for Linux)
- .NET 10 runtime in WSL environment
- Bash shell for hook execution
- Shared file system access

## Performance Targets

See **Performance Considerations** section for detailed v0.2.0 performance targets and optimization strategies.

**Summary for v0.2.0:**
- Plugin discovery: < 150ms (includes type extraction)
- Roslyn compilation: < 500ms per plugin
- Event routing + handler filtering: < 25ms
- Total hook execution: < 1.2s for typical scenarios
- Memory: < 120MB working set

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
- **Version**: 0.2.0 (Prototype/Proof of Concept)
- **Breaking Changes**: v0.2.0 introduces strongly-typed event handlers (incompatible with v0.1.0 plugins)
- **Created**: 2025-11-14
- **Last Updated**: 2025-11-15
