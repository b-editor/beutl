using SkiaSharp;

namespace Beutl.Graphics.Shaders;

// The program a shader stage runs on its own, whose text is also its program-cache key, and how its input is bound.
internal readonly record struct StandaloneShaderProgram(string ChildName, string Source, SKShaderTileMode TileMode)
{
    // A current-pixel source declares only apply, so the entry point that hands it the input's sample is added here.
    public static StandaloneShaderProgram For(ShaderDescription description)
        => description.Kind == ShaderDescriptionKind.CurrentPixel
            ? new(
                SkslSource.CurrentPixelInputName,
                SkslSource.CreateStandaloneCurrentPixelProgram(description.Source.Text),
                SKShaderTileMode.Decal)
            : new("src", description.Source.Text, description.SourceTileMode);
}
