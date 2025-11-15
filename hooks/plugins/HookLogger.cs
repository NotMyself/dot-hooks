using Microsoft.Extensions.Logging;

/// <summary>
/// Example global plugin that logs hook events using structured logging.
/// Handles both tool events and session events.
/// </summary>
public class HookLogger(ILogger logger) :
    IHookEventHandler<ToolEventInput, ToolEventOutput>,
    IHookEventHandler<SessionEventInput, SessionEventOutput>,
    IHookEventHandler<GenericEventInput, GenericEventOutput>
{
    public string Name => "HookLogger";

    // Tool event handler (pre-tool-use, post-tool-use)
    Task<ToolEventOutput> IHookEventHandler<ToolEventInput, ToolEventOutput>.HandleAsync(
        ToolEventInput input, CancellationToken ct)
    {
        logger.LogInformation("Tool event: {ToolName}", input.ToolName);
        logger.LogInformation("Session ID: {SessionId}", input.SessionId);
        logger.LogInformation("Working Directory: {Cwd}", input.Cwd);

        return Task.FromResult(HookOutputBase.Success<ToolEventOutput>());
    }

    // Session event handler (session-start, session-end)
    Task<SessionEventOutput> IHookEventHandler<SessionEventInput, SessionEventOutput>.HandleAsync(
        SessionEventInput input, CancellationToken ct)
    {
        logger.LogInformation("Session event");
        logger.LogInformation("Session ID: {SessionId}", input.SessionId);
        logger.LogInformation("Working Directory: {Cwd}", input.Cwd);

        return Task.FromResult(HookOutputBase.Success<SessionEventOutput>());
    }

    // Generic event handler (user-prompt-submit, notification, stop, subagent-stop, pre-compact)
    Task<GenericEventOutput> IHookEventHandler<GenericEventInput, GenericEventOutput>.HandleAsync(
        GenericEventInput input, CancellationToken ct)
    {
        logger.LogInformation("Generic event");
        logger.LogInformation("Session ID: {SessionId}", input.SessionId);
        logger.LogInformation("Working Directory: {Cwd}", input.Cwd);

        return Task.FromResult(HookOutputBase.Success<GenericEventOutput>());
    }
}
