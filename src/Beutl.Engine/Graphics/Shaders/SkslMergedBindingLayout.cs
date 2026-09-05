namespace Beutl.Graphics.Shaders;

internal readonly record struct SkslMergedBindingLayout(
    int StageIndex,
    int BindingIndex,
    SkslBindingKind Kind,
    string MergedName);
