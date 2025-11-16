# Changelog

All notable changes to dot-hooks will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2025-11-15

### Added
- Externalized configuration system using .NET 10 `IConfiguration`
- Strongly-typed settings classes (`DotHooksSettings`, `LoggingSettings`, `PathSettings`, etc.)
- Multi-layer configuration priority (base → environment → project → user → env vars)
- Project-level configuration support at `{project}/.claude/dot-hooks/appsettings.json`
- User-level configuration support at `~/.claude/dot-hooks/appsettings.user.json` and `~/.config/dot-hooks/appsettings.json`
- Environment variable configuration with `DOTHOOKS_` prefix
- Hook enable/disable feature via configuration
- Plugin enable/disable feature (global and user plugins separately)
- Configuration example files (`appsettings.project.json.example`, `appsettings.user.json.example`)
- IOptions<T> pattern for dependency injection integration
- Comprehensive configuration tests (13 new tests)
- Configuration system documentation in SPEC.md

### Changed
- Hardcoded settings now externalized to `appsettings.json`
- Logging levels configurable via settings
- Path conventions customizable via configuration
- Per-event project configuration loading

### Fixed
- All 39 tests passing including new configuration tests

## [0.2.1] - 2025-11-15

### Added
- Automated marketplace version synchronization workflow
- Marketplace polling system for automatic plugin updates

### Fixed
- Assembly loading issue causing test failures when running multiple tests
- Unique assembly names now generated for each dynamic plugin compilation

## [0.2.0] - 2025-11-15

### Added
- Strongly-typed event handler system with `IHookEventHandler<TInput, TOutput>`
- Event-specific input/output contracts (`ToolEventInput`, `SessionEventInput`, `GenericEventInput`)
- Per-plugin logging infrastructure with session and plugin-specific log files
- `EventTypeRegistry` for mapping event names to Input/Output type pairs
- Factory methods on `HookOutputBase` (`Success<T>()`, `Block<T>()`, `WithContext<T>()`)
- Dependency injection support for plugin constructors (ILogger, ILoggerFactory, etc.)
- Explicit interface implementation support for multi-event handlers
- Comprehensive test coverage for models, plugin loading, and handler execution
- GitHub Actions workflows for CI and automated releases
- Release and distribution documentation in SPEC.md

### Changed
- Migrated from monolithic `IHookPlugin` to generic `IHookEventHandler<TInput, TOutput>`
- Event routing now uses compile-time type safety instead of runtime string checking
- Plugin execution filters handlers by event type at runtime
- Logging output forced to stderr to preserve stdout for JSON responses
- Session logs now include execution duration with millisecond precision

### Breaking Changes
- Plugins from v0.1.0 are not compatible with v0.2.0
- `IHookPlugin` interface has been removed
- `HookInput` and `HookOutput` replaced by grouped event-specific types
- Plugin `ExecuteAsync` method replaced by `HandleAsync` with typed parameters
- See SPEC.md Migration Guide section for upgrade instructions

### Fixed
- Console output separation (all logs to stderr, only JSON to stdout)
- Handler discovery correctly identifies generic interface implementations
- Type constraint validation prevents mismatched Input/Output types at compile time

[Unreleased]: https://github.com/NotMyself/dot-hooks/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/NotMyself/dot-hooks/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/NotMyself/dot-hooks/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/NotMyself/dot-hooks/releases/tag/v0.2.0
