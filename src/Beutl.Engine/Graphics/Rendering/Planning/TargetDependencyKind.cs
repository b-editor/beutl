namespace Beutl.Graphics.Rendering;

internal enum TargetDependencyKind : byte
{
    Composite,
    Command,
    Capture,
    ScopeComposite,
}
