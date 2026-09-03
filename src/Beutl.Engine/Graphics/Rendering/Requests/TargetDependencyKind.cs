namespace Beutl.Graphics.Rendering.Requests;

internal enum TargetDependencyKind : byte
{
    Composite,
    Command,
    Capture,
    ScopeComposite,
}
