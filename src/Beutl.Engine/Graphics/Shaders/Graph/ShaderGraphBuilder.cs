using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Shaders;

/// <summary>
/// Records a graph synchronously. Callbacks run only while recording; no author callback is retained for rendering.
/// Passes are ordered, and each read must follow initialization of its resource. Repeated writes describe updates.
/// </summary>
public sealed partial class ShaderGraphBuilder
{
    private readonly List<ShaderResource> _resources = [];
    private readonly List<ShaderPassDescription> _passes = [];
    private readonly HashSet<string> _resourceNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _passNames = new(StringComparer.Ordinal);
    private bool _sealed;
    private bool _recordingPass;

    private ShaderGraphBuilder()
    {
        Source = new ShaderImage(this, 0, "source", TextureFormat.RGBA16Float, null, null, 0, 1);
        _resources.Add(Source);
        _resourceNames.Add(Source.Name);
    }

    /// <summary>The complete incoming image. Sampling it does not implicitly bind it to every pass.</summary>
    public ShaderImage Source { get; }

    /// <summary>Records and validates an immutable graph. The state and callback are not retained.</summary>
    public static ShaderGraphDescription Build<T>(T state, Func<ShaderGraphBuilder, T, ShaderGraphOutput> record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var builder = new ShaderGraphBuilder();
        try
        {
            ShaderGraphOutput output = record(builder, state)
                ?? throw new InvalidOperationException("A shader graph must select an output.");
            builder.ValidateResource(output.Image);
            if (output.Color != ColorEncoding.LinearPremultiplied)
            {
                ShaderImage converted = builder.ConvertColor(builder.InternalName("output-color"),
                    output.Image, output.Color, ColorEncoding.LinearPremultiplied);
                output = new(output.Image, output.Color, converted);
            }
            return new ShaderGraphDescription([.. builder._resources], [.. builder._passes], output);
        }
        finally
        {
            builder._sealed = true;
        }
    }

    /// <summary>Declares an image with the logical bounds of another image, optionally expanded on all sides.</summary>
    /// <param name="resolutionScale">Raster density relative to the graph's working scale; independent of bounds.</param>
    public ShaderImage Image2D(string name, ShaderImage like,
        TextureFormat format = TextureFormat.RGBA16Float, float padding = 0, float resolutionScale = 1)
    {
        ValidateResource(like);
        return AddImage(name, like, null, format, padding, resolutionScale);
    }

    /// <summary>Declares an image in an explicit composition-coordinate rectangle.</summary>
    public ShaderImage Image2D(string name, Rect bounds,
        TextureFormat format = TextureFormat.RGBA16Float, float resolutionScale = 1)
    {
        RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(bounds));
        return AddImage(name, null, bounds, format, 0, resolutionScale);
    }

    private ShaderImage AddImage(string name, ShaderImage? like, Rect? bounds,
        TextureFormat format, float padding, float resolutionScale)
    {
        EnsureRecording();
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));
        if (!float.IsFinite(padding) || padding < 0)
            throw new ArgumentOutOfRangeException(nameof(padding));
        if (!float.IsFinite(resolutionScale) || resolutionScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(resolutionScale));
        AddName(_resourceNames, name);
        var image = new ShaderImage(this, _resources.Count, name, format, like, bounds, padding, resolutionScale);
        _resources.Add(image);
        return image;
    }

    /// <summary>Declares storage whose contents must be written by a pass before being read.</summary>
    public ShaderBuffer Buffer<T>(string name, int count) where T : unmanaged
        => AddBuffer(name, count, Unsafe.SizeOf<T>(), []);

    /// <summary>Copies initial buffer bytes into the recorded request.</summary>
    /// <remarks>The element's unmanaged layout must match the shader's declared buffer layout.</remarks>
    public ShaderBuffer Buffer<T>(string name, ReadOnlySpan<T> values) where T : unmanaged
        => AddBuffer(name, values.Length, Unsafe.SizeOf<T>(), [.. MemoryMarshal.AsBytes(values)]);

    /// <summary>Declares uninitialized storage with an explicit element layout, without a CLR element type.</summary>
    /// <remarks>The stride must match the shader's array stride, including any layout padding.</remarks>
    public ShaderBuffer Buffer(string name, int count, int stride)
        => AddBuffer(name, count, stride, []);

    /// <summary>Copies packed initial bytes with an explicit element stride.</summary>
    public ShaderBuffer Buffer(string name, ReadOnlySpan<byte> data, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);
        if (data.Length % stride != 0)
            throw new ArgumentException("Buffer data must contain complete elements.", nameof(data));
        return AddBuffer(name, data.Length / stride, stride, [.. data]);
    }

    private ShaderBuffer AddBuffer(string name, int count, int stride, ImmutableArray<byte> data)
    {
        EnsureRecording();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);
        AddName(_resourceNames, name);
        var buffer = new ShaderBuffer(this, _resources.Count, name, count, stride, data);
        _resources.Add(buffer);
        return buffer;
    }

    public void Compute(string name, ShaderProgram program, Action<ShaderPassBuilder> record)
        => AddPass(name, program, record, compute: true, fullscreen: false);

    public void Render(string name, ShaderProgram program, Action<ShaderPassBuilder> record)
        => AddPass(name, program, record, compute: false, fullscreen: false);

    /// <summary>Records a graphics pass with a three-vertex, one-instance draw.</summary>
    public void Fullscreen(string name, ShaderProgram program, Action<ShaderPassBuilder> record)
        => AddPass(name, program, record, compute: false, fullscreen: true);

    private void AddPass(string name, ShaderProgram program, Action<ShaderPassBuilder> record,
        bool compute, bool fullscreen)
    {
        EnsureRecording();
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(record);
        if (program.IsCompute != compute)
            throw new ArgumentException($"Pass '{name}' has the wrong program stages.", nameof(program));
        AddName(_passNames, name);
        var pass = new ShaderPassBuilder(this, name, program, fullscreen);
        _recordingPass = true;
        try
        {
            record(pass);
            _passes.Add(pass.Build());
        }
        catch
        {
            _passNames.Remove(name);
            throw;
        }
        finally
        {
            pass.Seal();
            _recordingPass = false;
        }
    }

    public ShaderGraphOutput Output(ShaderImage image, ColorEncoding color = ColorEncoding.LinearPremultiplied)
    {
        EnsureRecording();
        ValidateResource(image);
        if (!Enum.IsDefined(color))
            throw new ArgumentOutOfRangeException(nameof(color));
        if (!image.IsAttachment || image.Format.IsDepthFormat() || image.Format.GetNumericType() != NumericType.Float)
            throw new ArgumentException("The graph output must be a single 2D color subresource with floating-point sample values. Convert numeric or depth data in an explicit pass.", nameof(image));
        return new(image, color);
    }

    private string InternalName(string purpose)
    {
        string name = "$" + purpose;
        for (int i = 0; _resourceNames.Contains(name) || _passNames.Contains(name); i++)
            name = "$" + purpose + i;
        return name;
    }

    internal void ValidateResource(ShaderResource resource)
    {
        ObjectDisposedException.ThrowIf(_sealed, this);
        ArgumentNullException.ThrowIfNull(resource);
        if (!ReferenceEquals(resource.Owner, this))
            throw new ArgumentException("The resource belongs to another shader graph.", nameof(resource));
    }

    private void EnsureRecording()
    {
        ObjectDisposedException.ThrowIf(_sealed, this);
        if (_recordingPass)
            throw new InvalidOperationException("Resources and passes cannot be declared inside a pass callback.");
    }

    private static void AddName(HashSet<string> names, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!names.Add(name))
            throw new ArgumentException($"The shader graph already contains '{name}'.", nameof(name));
    }
}
