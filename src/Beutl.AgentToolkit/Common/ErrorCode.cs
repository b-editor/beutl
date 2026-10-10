namespace Beutl.AgentToolkit.Common;

public static class ErrorCode
{
    public const string ValidationRejected = "validation_rejected";
    public const string MediaNotFound = "media_not_found";
    public const string MediaUnsupported = "media_unsupported";
    public const string UnknownType = "unknown_type";
    public const string StaleHandle = "stale_handle";
    public const string RenderingUnavailable = "rendering_unavailable";
    public const string CodecUnavailable = "codec_unavailable";
    public const string SchemaVersionMismatch = "schema_version_mismatch";
    public const string NoActiveEditorSession = "no_active_editor_session";
    public const string DestructiveIntent = "destructive_intent";
    public const string ProjectConflict = "project_conflict";
    public const string WorkspaceBusy = "workspace_busy";
    public const string AiUnavailable = "ai_unavailable";
    public const string AiGenerationFailed = "ai_generation_failed";
    public const string AiJobNotFound = "ai_job_not_found";
    public const string ExtensionToolUnavailable = "extension_tool_unavailable";
    public const string ExtensionToolFailed = "extension_tool_failed";
    public const string InstanceUnavailable = "instance_unavailable";
    public const string LiveUnavailable = "live_unavailable";
}
