namespace Beutl.Graphics.Effects;

internal sealed record SkslMergedBindingLayout(
    int StageIndex,
    int BindingIndex,
    SkslBindingKind Kind,
    string OriginalName,
    string MergedName,
    string Type,
    int? ArrayExtent,
    ShaderResourceCoordinateSpace? CoordinateSpace);
