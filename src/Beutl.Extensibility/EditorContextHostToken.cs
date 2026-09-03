namespace Beutl.Extensibility;

/// <summary>Opaque identity carried by a host-bound editor-context close capability.</summary>
/// <remarks>
/// Host tokens are compared by reference. A host creates one token and retains it for its
/// lifetime; contexts created by that host must expose the same instance through their close
/// capability. The public constructor allows independent host implementations to establish their
/// own identity without exposing any meaningful token value.
/// </remarks>
public sealed class EditorContextHostToken
{
}
