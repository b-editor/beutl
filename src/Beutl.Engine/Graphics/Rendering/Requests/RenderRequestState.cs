namespace Beutl.Graphics.Rendering.Requests;

internal enum RenderRequestState : byte
{
    Created,
    Recording,
    Recorded,
    TargetDependenciesLowered,
    MetadataResolved,
    RegionsResolved,
    CachesResolved,
    Planned,
    Executing,
    Completed,
    Failed,
    Disposed,
}
