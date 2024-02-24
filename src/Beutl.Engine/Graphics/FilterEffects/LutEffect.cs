using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Language;
using Beutl.Media;
using Beutl.Rendering;

using ILGPU;
using ILGPU.Runtime;

using Microsoft.Extensions.Logging;

using OpenCvSharp;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public sealed class LutEffect : FilterEffect
{
    public static readonly CoreProperty<FileInfo?> SourceProperty;
    public static readonly CoreProperty<float> StrengthProperty;
    private static readonly ILogger<LutEffect> s_logger=BeutlApplication.Current.LoggerFactory.CreateLogger<LutEffect>();
    private FileInfo? _source;
    private float _strength = 100;
    private CubeFile? _cube;

    static LutEffect()
    {
        SourceProperty = ConfigureProperty<FileInfo?, LutEffect>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .Register();

        StrengthProperty = ConfigureProperty<float, LutEffect>(nameof(Strength))
            .Accessor(o => o.Strength, (o, v) => o.Strength = v)
            .DefaultValue(100)
            .Register();

        AffectsRender<LutEffect>(SourceProperty, StrengthProperty);
    }

    public FileInfo? Source
    {
        get => _source;
        set
        {
            if (SetAndRaise(SourceProperty, ref _source, value))
            {
                OnSourceChanged(value);
            }
        }
    }

    private void OnSourceChanged(FileInfo? value)
    {
        _cube = null;
        if (value != null)
        {
            using FileStream stream = value.OpenRead();
            try
            {
                _cube = CubeFile.FromStream(stream);
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "Cubeファイルの解析に失敗しました。{FileName}", value.FullName);
            }
        }
    }

    [Display(Name = nameof(Strings.Strength), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public float Strength
    {
        get => _strength;
        set => SetAndRaise(StrengthProperty, ref _strength, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        if (_cube != null)
        {
            if (_cube.Dimention == CubeFileDimension.OneDimension)
            {
                context.LookupTable(
                    _cube,
                    _strength / 100,
                    (CubeFile cube, (byte[] A, byte[] R, byte[] G, byte[] B) data) =>
                    {
                        LookupTable.Linear(data.A);
                        cube.ToLUT(1, data.R, data.G, data.B);
                    });
            }
            else
            {
                context.CustomEffect((_cube, _strength / 100), OnApply3DLUT_GPU, (_, r) => r);
            }
        }
    }

    private unsafe void OnApply3DLUT_GPU((CubeFile, float) data, CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            var target= context.Targets[i];
            var surface = target.Surface!.Value;
            Accelerator accelerator = SharedGPUContext.Accelerator;
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<Vec4b>, ArrayView<Vector3>, int, float>(Apply3DLUTKernel);

            var size = PixelSize.FromSize(target.Bounds.Size, 1);
            var imgInfo = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888);

            using var source = accelerator.Allocate1D<Vec4b>(size.Width * size.Height);
            using var lut = accelerator.Allocate1D(data.Item1.Data);

            CopyFromCPU(source, surface, imgInfo);

            kernel((int)source.Length, source.View, lut.View, data.Item1.Size, data.Item2);

            SKCanvas canvas = surface.Canvas;
            canvas.Clear();

            using var skBmp = new SKBitmap(imgInfo);

            CopyToCPU(source, skBmp);

            canvas.DrawBitmap(skBmp, 0, 0);
        }
    }

    private static unsafe void CopyFromCPU(MemoryBuffer1D<Vec4b, Stride1D.Dense> source, SKSurface surface, SKImageInfo imageInfo)
    {
        void* tmp = NativeMemory.Alloc((nuint)source.LengthInBytes);
        try
        {
            bool result = surface.ReadPixels(imageInfo, (nint)tmp, imageInfo.Width * 4, 0, 0);

            source.View.CopyFromCPU(ref Unsafe.AsRef<Vec4b>(tmp), source.Length);
        }
        finally
        {
            NativeMemory.Free(tmp);
        }
    }

    private static unsafe void CopyToCPU(MemoryBuffer1D<Vec4b, Stride1D.Dense> source, SKBitmap bitmap)
    {
        source.View.CopyToCPU(ref Unsafe.AsRef<Vec4b>((void*)bitmap.GetPixels()), source.Length);
    }

    private static Vector3 TrilinearInterplate(Vec4b color, int lut_size, ArrayView<Vector3> lut)
    {
        Vec3b pos = default; // 0~33
        Vec3f delta = default; //
        int lut_size_2 = lut_size * lut_size;

        pos.Item0 = (byte)(color.Item0 * lut_size / 256);
        pos.Item1 = (byte)(color.Item1 * lut_size / 256);
        pos.Item2 = (byte)(color.Item2 * lut_size / 256);

        delta.Item0 = color.Item0 * lut_size / 256.0f - pos.Item0;
        delta.Item1 = color.Item1 * lut_size / 256.0f - pos.Item1;
        delta.Item2 = color.Item2 * lut_size / 256.0f - pos.Item2;

        Vector3 vertex_color_0, vertex_color_1, vertex_color_2, vertex_color_3, vertex_color_4, vertex_color_5, vertex_color_6, vertex_color_7;
        Vector3 surf_color_0, surf_color_1, surf_color_2, surf_color_3;
        Vector3 line_color_0, line_color_1;
        Vector3 out_color;

        int index = pos.Item0 + pos.Item1 * lut_size + pos.Item2 * lut_size_2;

        int next_index_0 = 1, next_index_1 = lut_size, next_index_2 = lut_size_2;

        if (index % lut_size == lut_size - 1)
        {
            next_index_0 = 0;
        }
        if (index / lut_size % lut_size == lut_size - 1)
        {
            next_index_1 = 0;
        }
        if (index / lut_size_2 % lut_size == lut_size - 1)
        {
            next_index_2 = 0;
        }

        // https://en.wikipedia.org/wiki/Trilinear_interpolation
        vertex_color_0 = lut[index];
        vertex_color_1 = lut[index + next_index_0];
        vertex_color_2 = lut[index + next_index_0 + next_index_1];
        vertex_color_3 = lut[index + next_index_1];
        vertex_color_4 = lut[index + next_index_2];
        vertex_color_5 = lut[index + next_index_0 + next_index_2];
        vertex_color_6 = lut[index + next_index_0 + next_index_1 + next_index_2];
        vertex_color_7 = lut[index + next_index_1 + next_index_2];

        surf_color_0 = vertex_color_0 * (1.0f - delta.Item2) + vertex_color_4 * delta.Item2;
        surf_color_1 = vertex_color_1 * (1.0f - delta.Item2) + vertex_color_5 * delta.Item2;
        surf_color_2 = vertex_color_2 * (1.0f - delta.Item2) + vertex_color_6 * delta.Item2;
        surf_color_3 = vertex_color_3 * (1.0f - delta.Item2) + vertex_color_7 * delta.Item2;

        line_color_0 = surf_color_0 * (1.0f - delta.Item0) + surf_color_1 * delta.Item0;
        line_color_1 = surf_color_2 * (1.0f - delta.Item0) + surf_color_3 * delta.Item0;

        out_color = line_color_0 * (1.0f - delta.Item1) + line_color_1 * delta.Item1;

        return out_color;
    }

    private static void Apply3DLUTKernel(Index1D index, ArrayView<Vec4b> src, ArrayView<Vector3> lut, int lutSize, float strength)
    {
        Vec4b pixel = src[index];

        Vector3 newColor = TrilinearInterplate(pixel, lutSize, lut);

        src[index] = SetStrength(strength, newColor, pixel);
    }

    private static Vec4b SetStrength(float strength, Vector3 color, Vec4b original)
    {
        var newColor = new Vec4b((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), original.Item3);

        newColor.Item0 = (byte)((newColor.Item0 * strength) + (original.Item0 * (1 - strength)));
        newColor.Item1 = (byte)((newColor.Item1 * strength) + (original.Item1 * (1 - strength)));
        newColor.Item2 = (byte)((newColor.Item2 * strength) + (original.Item2 * (1 - strength)));

        return newColor;
    }
}
