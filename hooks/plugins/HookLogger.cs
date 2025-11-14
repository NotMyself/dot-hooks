/// <summary>
/// Example global plugin that logs hook event names.
/// </summary>
public class HookLogger : IHookPlugin
{
    public string Name => "HookLogger";

    public Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[HookLogger] Hook event triggered: {input.EventType}");
        Console.WriteLine($"[HookLogger] Session ID: {input.SessionId}");
        Console.WriteLine($"[HookLogger] Working Directory: {input.Cwd}");

        if (!string.IsNullOrEmpty(input.ToolName))
        {
            Console.WriteLine($"[HookLogger] Tool: {input.ToolName}");
        }

        return Task.FromResult(HookOutput.Success());
    }
}
