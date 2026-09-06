using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Colors a render metadata callback may read.
/// </summary>
/// <remarks>
/// SkiaSharp declares the members of <see cref="SKColors"/> as assignable static fields, so a callback that
/// reads one answers differently once anything assigns to it while keeping the identity the compiled plan is
/// keyed by. These copies cannot be reassigned, which is what BESG004 asks for.
/// </remarks>
internal static class StableColors
{
    public static readonly SKColor White = SKColors.White;

    public static readonly SKColor Black = SKColors.Black;
}
