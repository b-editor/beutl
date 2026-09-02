using Beutl.Graphics.Shaders;

namespace Beutl.Graphics.Effects;

/// <summary>
/// Provides the shared <see cref="ShaderDescriptionKind.CurrentPixel"/> stage that reproduces
/// <c>SKColorFilter.CreateColorMatrix</c>, so color-matrix filters stay inside a fusable shader chain instead of
/// falling back to a effect-item Skia color-filter segment.
/// </summary>
/// <remarks>
/// Skia unpremultiplies, multiplies the straight components by the matrix, clamps the product to [0, 1], and
/// re-premultiplies by the transformed alpha. Only the product is clamped: an RGBA16F buffer may carry straight
/// components outside [0, 1], and Skia feeds those through the matrix unclamped. Clamping the input instead would
/// diverge on exactly those out-of-range samples.
/// <para>
/// The unpremultiply divides by <c>max(a, 1e-4)</c>, matching Skia's own SkSL <c>unpremul()</c> helper. That
/// clamp is part of the contract, not an optimization: it is what keeps a near-zero alpha from producing an
/// infinite straight value, and it applies unconditionally. Branching on <c>a &gt; 0</c> instead would diverge
/// on a non-canonical premultiplied sample (alpha 0 with non-zero RGB), which Skia carries through the matrix
/// rather than forcing to black.
/// </para>
/// </remarks>
internal static class ColorMatrixShader
{
    /// <summary>The component count of a Skia color-matrix array: four rows of five columns.</summary>
    internal const int SkiaColorMatrixLength = 20;

    private const string MatrixUniformName = "colorMatrix";

    private const string OffsetUniformName = "colorOffset";

    private const string ShaderSource =
        """
        uniform float4x4 colorMatrix;
        uniform float4 colorOffset;

        half4 apply(half4 color) {
            float alpha = color.a;
            // Every sample goes through the matrix, including a transparent one: a non-zero offset column -
            // the alpha offset, matrix[19], in particular - can turn a transparent pixel into a visible one,
            // and the RGB offsets survive the re-premultiply. Short-circuiting transparent pixels to black
            // would drop that. The divisor is clamped exactly the way Skia's unpremul() clamps it, which both
            // bounds the near-zero-alpha band and keeps a non-canonical (a == 0, rgb != 0) sample in parity.
            float4 straight = float4(color.rgb / max(alpha, 0.0001), alpha);

            float4 transformed = clamp(colorMatrix * straight + colorOffset, float4(0.0), float4(1.0));

            return half4(half3(transformed.rgb * transformed.a), half(transformed.a));
        }
        """;

    private static readonly SkslSource s_shaderSource =
        new(ShaderSource, ShaderDescriptionKind.CurrentPixel);

    /// <summary>Builds the shared color-matrix stage from a Skia-layout 4x5 color matrix.</summary>
    /// <param name="matrix">
    /// A row-major 4x5 color matrix in the layout <c>SKColorFilter.CreateColorMatrix</c> consumes. Its values are
    /// copied while the description is created; the storage is never retained.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="matrix"/> does not contain exactly <see cref="SkiaColorMatrixLength"/> values.
    /// </exception>
    internal static ShaderDescription CurrentPixel(ReadOnlySpan<float> matrix)
    {
        if (matrix.Length != SkiaColorMatrixLength)
        {
            throw new ArgumentException(
                $"A Skia color matrix requires exactly {SkiaColorMatrixLength} values.",
                nameof(matrix));
        }

        // The row-major 4x5 array splits into a 4x4 multiplier and its fifth translation column. SkSL reads a
        // matrix uniform column-major and indexes it as [column][row], so source element (row, column) moves to
        // the flat slot (column * 4) + row.
        float[] multiplier = new float[16];
        float[] offset = new float[4];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
                multiplier[(column * 4) + row] = matrix[(row * 5) + column];

            offset[row] = matrix[(row * 5) + 4];
        }

        return ShaderDescription.CurrentPixel(
            s_shaderSource,
            bindings =>
            {
                bindings.Uniform(MatrixUniformName, multiplier);
                bindings.Uniform(OffsetUniformName, offset);
            });
    }
}
