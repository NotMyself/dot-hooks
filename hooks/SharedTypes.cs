using System.Text.Json.Serialization;

// =============================================================================
// SHARED TYPES - Used by both Program.cs and tests
// =============================================================================

public record HookInput
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("transcript_path")]
    public string TranscriptPath { get; init; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; init; } = string.Empty;

    [JsonPropertyName("permission_mode")]
    public string PermissionMode { get; init; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    [JsonPropertyName("tool_parameters")]
    public Dictionary<string, object>? ToolParameters { get; init; }

    [JsonPropertyName("additional_data")]
    public Dictionary<string, object>? AdditionalData { get; init; }
}

public record HookOutput
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

    public static HookOutput Success() => new() { Decision = "approve", Continue = true };
    public static HookOutput Block(string reason) => new() { Decision = "block", Continue = false, StopReason = reason };
    public static HookOutput WithContext(string context) => new() { Decision = "approve", Continue = true, AdditionalContext = context };
}

public interface IHookPlugin
{
    string Name { get; }
    Task<HookOutput> ExecuteAsync(HookInput input, CancellationToken cancellationToken = default);
}
