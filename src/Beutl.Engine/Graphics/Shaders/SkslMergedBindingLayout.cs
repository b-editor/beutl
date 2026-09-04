namespace Beutl.Graphics.Shaders;

internal readonly record struct SkslMergedBindingLayout(
    int StageIndex,
    int BindingIndex,
    SkslBindingKind Kind,
    string OriginalName,
    string MergedName,
    string Type,
    int? ArrayExtent,
    ShaderResourceCoordinateSpace? CoordinateSpace);
