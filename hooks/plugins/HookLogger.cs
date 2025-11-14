using Microsoft.Extensions.Logging;

/// <summary>
/// Example global plugin that logs hook event names using structured logging.
/// </summary>
public class HookLogger(ILogger logger) : IHookPlugin
{
    public string Name => "HookLogger";

    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Hook event triggered: {EventType}", input.EventType);
        logger.LogInformation("Session ID: {SessionId}", input.SessionId);
        logger.LogInformation("Working Directory: {Cwd}", input.Cwd);

        if (!string.IsNullOrEmpty(input.ToolName))
        {
            logger.LogInformation("Tool: {ToolName}", input.ToolName);
        }

        return Task.FromResult(HookOutput.Success());
    }
}
