namespace DotHooks;

/// <summary>
/// Interface for hook plugins that execute during Claude Code sessions.
/// </summary>
public interface IHookPlugin
{
    /// <summary>
    /// Gets the unique name of the plugin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the plugin logic for a hook event.
    /// </summary>
    /// <param name="input">The hook input containing event details.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The hook output with plugin response.</returns>
    Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default);
}
