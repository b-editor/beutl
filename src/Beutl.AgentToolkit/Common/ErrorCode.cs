namespace Beutl.AgentToolkit.Common;

public static class ErrorCode
{
    public const string WorkspaceBoundary = "workspace_boundary";
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
}
