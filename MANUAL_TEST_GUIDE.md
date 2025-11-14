# dot-hooks Manual Test Guide

This guide provides step-by-step instructions for manually testing the dot-hooks plugin to verify all functionality works correctly.

## Prerequisites

Before testing, ensure you have:

- [ ] .NET 10 SDK installed (`dotnet --version` should show 10.x.x)
- [ ] Claude Code CLI installed
- [ ] Git installed and configured
- [ ] PowerShell 7+ (for Windows testing)
- [ ] WSL configured (for WSL testing)

## Test Environment Setup

### 1. Create Test Project

```bash
mkdir ~/claude-test-project
cd ~/claude-test-project
git init
echo "# Test Project" > README.md
git add README.md
git commit -m "Initial commit"
```

### 2. Install dot-hooks Plugin

```bash
# Add marketplace (if not already added)
/plugin marketplace add NotMyself/claude-dotnet-marketplace

# Install dot-hooks
/plugin install dot-hooks

# Verify installation
/plugin list
```

**Expected Result**: dot-hooks appears in the plugin list

## Test Cases

## TC-01: Verify Plugin Installation

**Objective**: Confirm plugin files are correctly installed

**Steps**:
1. Check plugin directory exists
   ```bash
   ls ~/.claude/plugins/dot-hooks
   ```

**Expected Result**:
- `.claude-plugin/` directory exists
- `hooks/` directory exists with Program.cs, plugins/, etc.

**Status**: ⬜ Pass / ⬜ Fail

---

## TC-02: Test Global Plugin - HookLogger

**Objective**: Verify global plugin executes and logs events to session log file

**Steps**:
1. Start Claude Code session in test project:
   ```bash
   cd ~/claude-test-project
   claude
   ```

2. In Claude Code, request a simple action:
   ```
   Create a file called test.txt with the content "Hello World"
   ```

3. Check session log files:
   ```bash
   # List session directories
   ls .claude/state/

   # View main session log (replace <session-id> with actual ID)
   cat .claude/state/<session-id>/dot-hooks.log

   # Check per-plugin logs directory
   ls .claude/state/<session-id>/plugins/

   # View HookLogger's individual log
   cat .claude/state/<session-id>/plugins/HookLogger.log
   ```

**Expected Result**:

- Session directory created at `.claude/state/<session-id>/`
- Main log file `dot-hooks.log` exists with consolidated plugin execution flow
- `plugins/` subdirectory exists with individual plugin logs
- `plugins/HookLogger.log` contains per-plugin execution details
- Log contains timestamped entries with:
  - Session start marker with session ID and event type
  - Plugin execution entries: "Executing plugin: HookLogger"
  - Plugin completion entries with decision, continue status, and execution duration
  - Event completion marker
- Hook events triggered: `session-start`, `user-prompt-submit`, `pre-tool-use`, `post-tool-use` for Write tool
- Log format: `[YYYY-MM-DD HH:mm:ss.fff] Message`
- Per-plugin logs include event type, session ID, tool name (if applicable), and execution duration

**Example Main Log Output (dot-hooks.log)**:
```
[2025-11-14 21:29:02.077] Session: abc123, Event: session-start
[2025-11-14 21:29:02.854] Executing plugin: HookLogger
[2025-11-14 21:29:02.855] Plugin HookLogger completed: decision=approve, continue=True
[2025-11-14 21:29:02.857] Event session-start completed successfully

[2025-11-14 21:29:30.996] Session: abc123, Event: pre-tool-use
[2025-11-14 21:29:31.755] Executing plugin: HookLogger
[2025-11-14 21:29:31.756] Plugin HookLogger completed: decision=approve, continue=True
[2025-11-14 21:29:31.758] Event pre-tool-use completed successfully
```

**Example Per-Plugin Log (plugins/HookLogger.log)**:
```
[2025-11-14 21:29:02.854] Event: session-start
[2025-11-14 21:29:02.854] Session: abc123
[2025-11-14 21:29:02.855] Completed: decision=approve, continue=True, duration=1.23ms

[2025-11-14 21:29:31.755] Event: pre-tool-use
[2025-11-14 21:29:31.755] Session: abc123
[2025-11-14 21:29:31.755] Tool: Write
[2025-11-14 21:29:31.756] Completed: decision=approve, continue=True, duration=1.05ms

```

**Status**: ⬜ Pass / ⬜ Fail

**Notes**:
```
_____________________________________________________
```

---

## TC-03: Test All Hook Events

**Objective**: Verify all 9 hook events can be triggered

### 3a. SessionStart Event

**Steps**:
1. Start new Claude Code session
2. Check logs for SessionStart event

**Expected Result**: Logs show `event_type: session-start`

**Status**: ⬜ Pass / ⬜ Fail

---

### 3b. PreToolUse Event

**Steps**:
1. Ask Claude to read a file:
   ```
   Read the README.md file
   ```
2. Check logs before tool execution

**Expected Result**: Logs show `event_type: pre-tool-use`, `tool_name: Read`

**Status**: ⬜ Pass / ⬜ Fail

---

### 3c. PostToolUse Event

**Steps**:
1. After tool execution completes
2. Check logs

**Expected Result**: Logs show `event_type: post-tool-use`

**Status**: ⬜ Pass / ⬜ Fail

---

### 3d. UserPromptSubmit Event

**Steps**:
1. Submit any prompt to Claude
2. Check logs

**Expected Result**: Logs show `event_type: user-prompt-submit`

**Status**: ⬜ Pass / ⬜ Fail

---

### 3e. SessionEnd Event

**Steps**:
1. Exit Claude Code session (Ctrl+C or type `exit`)
2. Check logs

**Expected Result**: Logs show `event_type: session-end`

**Status**: ⬜ Pass / ⬜ Fail

---

## TC-04: Test User Plugin Loading

**Objective**: Verify user plugins are discovered and executed

**Steps**:

1. Create user plugin directory:
   ```bash
   mkdir -p .claude/hooks/dot-hooks
   ```

2. Create test plugin `TestPlugin.cs` (with logger support):
   ```bash
   cat > .claude/hooks/dot-hooks/TestPlugin.cs << 'EOF'
   namespace UserPlugins;

   /// <summary>
   /// Example user plugin demonstrating ILogger support.
   /// Plugins can optionally accept ILogger in constructor.
   /// </summary>
   public class TestPlugin(ILogger logger) : IHookPlugin
   {
       public string Name => "TestPlugin";

       public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
       {
           logger.LogInformation("USER PLUGIN EXECUTED: {EventType}", input.EventType);
           logger.LogInformation("Session: {SessionId}", input.SessionId);
           return Task.FromResult(HookOutput.Success());
       }
   }
   EOF
   ```

   **Note**: Plugins automatically get `using Microsoft.Extensions.Logging;` injected during compilation.

3. Start Claude session and trigger any action

4. Check console output and session log:

   ```bash
   # Console should show logger output
   # Check session log
   cat .claude/state/<session-id>/dot-hooks.log
   ```

**Expected Result**:

- Console shows logger output: `info: TestPlugin[0] USER PLUGIN EXECUTED: <event-type>`
- Session log shows both plugins executing: "Executing plugin: HookLogger" then "Executing plugin: TestPlugin"
- User plugin executes AFTER global plugin (HookLogger)
- No compilation errors in logs
- Plugin completion logged with decision and continue status

**Status**: ⬜ Pass / ⬜ Fail

**Notes**:
```
_____________________________________________________
```

---

## TC-05: Test Plugin Execution Order

**Objective**: Verify plugins execute in correct order (global → user, alphabetically)

**Steps**:

1. Create multiple user plugins:
   ```bash
   # Plugin A
   cat > .claude/hooks/dot-hooks/APlugin.cs << 'EOF'
   using DotHooks;
   public class APlugin(ILogger logger) : IHookPlugin
   {
       public string Name => "APlugin";
       public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
       {
           logger.LogInformation("Executing");
           return Task.FromResult(HookOutput.Success());
       }
   }
   EOF

   # Plugin Z
   cat > .claude/hooks/dot-hooks/ZPlugin.cs << 'EOF'
   using DotHooks;
   public class ZPlugin(ILogger logger) : IHookPlugin
   {
       public string Name => "ZPlugin";
       public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
       {
           logger.LogInformation("Executing");
           return Task.FromResult(HookOutput.Success());
       }
   }
   EOF
   ```

2. Trigger any hook event

3. Check console output order

**Expected Result**:
Order should be:
1. `[HookLogger]` (global plugin)
2. `[APlugin]` (user plugin - alphabetically first)
3. `[ZPlugin]` (user plugin - alphabetically last)

**Status**: ⬜ Pass / ⬜ Fail

---

## TC-06: Test Plugin with Context Injection

**Objective**: Verify plugins can add context for Claude

**Steps**:

1. Create context plugin:
   ```bash
   cat > .claude/hooks/dot-hooks/ContextPlugin.cs << 'EOF'
   using DotHooks;

   public class ContextPlugin : IHookPlugin
   {
       public string Name => "ContextPlugin";

       public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
       {
           if (input.EventType == "pre-tool-use")
           {
               return Task.FromResult(HookOutput.WithContext(
                   "CUSTOM CONTEXT: This is additional information for Claude"));
           }
           return Task.FromResult(HookOutput.Success());
       }
   }
   EOF
   ```

2. Trigger a tool use action

3. Observe if Claude receives the context

**Expected Result**:
- Plugin executes without errors
- Context is included in hook output (check logs)

**Status**: ⬜ Pass / ⬜ Fail

---

## TC-07: Test Plugin Compilation Errors

**Objective**: Verify plugin with compilation errors is skipped gracefully

**Steps**:

1. Create invalid plugin:
   ```bash
   cat > .claude/hooks/dot-hooks/BrokenPlugin.cs << 'EOF'
   using DotHooks;

   public class BrokenPlugin : IHookPlugin
   {
       public string Name => "BrokenPlugin";

       // Missing method implementation - will fail to compile
       // public Task<HookOutput> ExecuteAsync(...)
   }
   EOF
   ```

2. Trigger any hook event

3. Check logs for compilation errors

**Expected Result**:
- Error logged: "Compilation error in BrokenPlugin.cs"
- Other plugins still execute successfully
- Hook execution continues (does not block)

**Status**: ⬜ Pass / ⬜ Fail

---

## TC-08: Test Blocking Plugin

**Objective**: Verify plugin can block tool execution

**Steps**:

1. Create blocking plugin:

   ```bash
   cat > .claude/hooks/dot-hooks/BlockingPlugin.cs << 'EOF'
   public class BlockingPlugin(ILogger logger) : IHookPlugin
   {
       public string Name => "BlockingPlugin";

       public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
       {
           if (input.EventType == "pre-tool-use" && input.ToolName == "Write")
           {
               logger.LogWarning("BLOCKING WRITE OPERATION");
               return Task.FromResult(HookOutput.Block("Write operations are blocked for testing"));
           }
           return Task.FromResult(HookOutput.Success());
       }
   }
   EOF
   ```

2. Ask Claude to write a file

3. Check session log for blocking entry:

   ```bash
   cat .claude/state/<session-id>/dot-hooks.log
   ```

**Expected Result**:

- Tool execution is blocked
- Claude receives blocking message
- Exit code is 2 (blocking error)
- Session log contains: `Plugin BlockingPlugin BLOCKED: Write operations are blocked for testing`
- Console shows warning log: `warn: BlockingPlugin[0] BLOCKING WRITE OPERATION`

**Status**: ⬜ Pass / ⬜ Fail

**Notes**:
```
_____________________________________________________
```

---

## TC-09: Test Cross-Platform (WSL)

**Objective**: Verify plugin works in WSL environment

**Steps** (in WSL):

1. Navigate to test project:
   ```bash
   cd /mnt/c/Users/<username>/claude-test-project
   ```

2. Verify .NET 10 is available:
   ```bash
   dotnet --version
   ```

3. Start Claude Code session

4. Trigger hook events

5. Check logs

**Expected Result**:
- Plugin executes in WSL environment
- Logs are created in Linux format paths
- All hook events work correctly

**Status**: ⬜ Pass / ⬜ Fail

---

## TC-10: Test Performance

**Objective**: Verify hook execution completes within timeout

**Steps**:

1. Create multiple complex plugins (3-5 plugins)

2. Trigger hook events

3. Measure execution time (check logs for timestamps)

**Expected Result**:
- Total hook execution < 1 second for typical scenarios
- No timeout errors (30 second default timeout)
- Plugin loading and compilation < 500ms per plugin

**Status**: ⬜ Pass / ⬜ Fail

**Measured Time**: __________ ms

---

## TC-11: Test Logging Levels

**Objective**: Verify different log levels work correctly

**Steps**:

1. Check current log output level

2. Note what appears in console vs files

3. Verify structured logging format

**Expected Result**:
- Info level messages appear in both console and files
- Debug messages only in files (if enabled)
- Errors are prominently displayed

**Status**: ⬜ Pass / ⬜ Fail

---

## TC-12: Test with No User Plugins

**Objective**: Verify plugin works when no user plugins exist

**Steps**:

1. Remove all user plugins:
   ```bash
   rm -rf .claude/hooks/dot-hooks/*.cs
   ```

2. Trigger hook events

3. Check logs

**Expected Result**:
- Only global plugins execute
- No errors about missing user plugins
- Hook execution succeeds

**Status**: ⬜ Pass / ⬜ Fail

---

## TC-13: Test Session State Persistence

**Objective**: Verify session logs are properly separated

**Steps**:

1. Start Claude session 1, trigger events, exit

2. Start Claude session 2, trigger events, exit

3. Check session logs:
   ```bash
   ls -la .claude/state/session/
   ```

**Expected Result**:
- Two separate session log files exist
- Each contains only its session's events
- Session IDs are unique

**Status**: ⬜ Pass / ⬜ Fail

---

## TC-14: Test Plugin with External Dependencies

**Objective**: Verify plugins can use standard .NET libraries

**Steps**:

1. Create plugin using System.Text.Json:
   ```bash
   cat > .claude/hooks/dot-hooks/JsonPlugin.cs << 'EOF'
   using DotHooks;
   using System.Text.Json;

   public class JsonPlugin(ILogger logger) : IHookPlugin
   {
       public string Name => "JsonPlugin";

       public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
       {
           var json = JsonSerializer.Serialize(new { plugin = "JsonPlugin", event = input.EventType });
           logger.LogInformation("{Json}", json);
           return Task.FromResult(HookOutput.Success());
       }
   }
   EOF
   ```

2. Trigger hook events

**Expected Result**:
- Plugin compiles successfully
- JSON output appears in console
- No compilation errors about missing references

**Status**: ⬜ Pass / ⬜ Fail

---

## TC-15: Test Cleanup

**Objective**: Verify plugin can be cleanly uninstalled

**Steps**:

1. Uninstall plugin:
   ```bash
   /plugin uninstall dot-hooks
   ```

2. Verify removal:
   ```bash
   ls ~/.claude/plugins/
   /plugin list
   ```

3. Trigger actions in Claude (hooks should not execute)

**Expected Result**:
- Plugin directory removed
- Plugin not in list
- Hook events no longer trigger plugin execution
- User plugins in project remain (in .claude/hooks/dot-hooks/)

**Status**: ⬜ Pass / ⬜ Fail

---

## Test Summary

**Date**: __________________
**Tester**: __________________
**Environment**: Windows 11 ⬜ / WSL ⬜ / Both ⬜
**.NET Version**: __________________
**Claude Code Version**: __________________

### Results

| Test Case | Status | Notes |
|-----------|--------|-------|
| TC-01: Installation | ⬜ Pass / ⬜ Fail | |
| TC-02: Global Plugin | ⬜ Pass / ⬜ Fail | |
| TC-03: All Hook Events | ⬜ Pass / ⬜ Fail | |
| TC-04: User Plugin Loading | ⬜ Pass / ⬜ Fail | |
| TC-05: Execution Order | ⬜ Pass / ⬜ Fail | |
| TC-06: Context Injection | ⬜ Pass / ⬜ Fail | |
| TC-07: Compilation Errors | ⬜ Pass / ⬜ Fail | |
| TC-08: Blocking Plugin | ⬜ Pass / ⬜ Fail | |
| TC-09: WSL Support | ⬜ Pass / ⬜ Fail | |
| TC-10: Performance | ⬜ Pass / ⬜ Fail | |
| TC-11: Logging Levels | ⬜ Pass / ⬜ Fail | |
| TC-12: No User Plugins | ⬜ Pass / ⬜ Fail | |
| TC-13: Session Persistence | ⬜ Pass / ⬜ Fail | |
| TC-14: External Dependencies | ⬜ Pass / ⬜ Fail | |
| TC-15: Cleanup | ⬜ Pass / ⬜ Fail | |

**Total Passed**: ____ / 15
**Total Failed**: ____ / 15

### Critical Issues Found

```
_________________________________________________________
_________________________________________________________
_________________________________________________________
```

### Recommendations

```
_________________________________________________________
_________________________________________________________
_________________________________________________________
```

---

## Appendix: Useful Commands

### View Session State and Logs

```bash
# List all session directories
ls .claude/state/

# View files in a specific session
ls -la .claude/state/<session-id>/

# List per-plugin logs
ls .claude/state/<session-id>/plugins/

# View main session log (all plugins, consolidated)
cat .claude/state/<session-id>/dot-hooks.log

# View individual plugin log
cat .claude/state/<session-id>/plugins/HookLogger.log
cat .claude/state/<session-id>/plugins/TestPlugin.log

# Tail main session log (follow live)
tail -f .claude/state/<session-id>/dot-hooks.log

# Tail specific plugin log (follow live)
tail -f .claude/state/<session-id>/plugins/HookLogger.log

# Search main log for specific plugin
grep "HookLogger" .claude/state/<session-id>/dot-hooks.log

# Search main log for errors
grep "ERROR" .claude/state/<session-id>/dot-hooks.log

# Search main log for blocked operations
grep "BLOCKED" .claude/state/<session-id>/dot-hooks.log

# Search specific plugin log for errors
grep "ERROR" .claude/state/<session-id>/plugins/YourPlugin.log

# View plugin execution times
grep "duration" .claude/state/<session-id>/plugins/*.log
```

**Session Log Format**:

```text
[YYYY-MM-DD HH:mm:ss.fff] Message
```

**Example Session Log**:

```text
[2025-11-14 21:29:02.077] Session: abc123, Event: session-start
[2025-11-14 21:29:02.854] Executing plugin: HookLogger
[2025-11-14 21:29:02.855] Plugin HookLogger completed: decision=approve, continue=True
[2025-11-14 21:29:02.857] Event session-start completed successfully
```

### Clean Up Test Environment
```bash
# Remove user plugins
rm -rf .claude/hooks/dot-hooks/*

# Remove logs
rm -rf .claude/state/*

# Remove test project
cd ~
rm -rf claude-test-project
```

### Debugging
```bash
# Test plugin compilation manually
dotnet run ~/.claude/plugins/dot-hooks/hooks/Program.cs -- pre-tool-use < test-input.json

# Check .NET SDK
dotnet --info

# Verify plugin installation
find ~/.claude/plugins/dot-hooks -type f -name "*.cs"
```
