using System.ComponentModel;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

public sealed class EffectTarget : IDisposable
{
    [Obsolete("Use a constructor with no parameters.")]
    public static readonly EffectTarget Empty = new();

    private object? _target;

    public EffectTarget(RenderNodeOperation node)
    {
        _target = node;
        OriginalBounds = node.Bounds;
        Bounds = node.Bounds;
        CorrectionScale = node.CorrectionScale;
    }

    public EffectTarget(RenderTarget renderTarget, Rect originalBounds)
        : this(renderTarget, originalBounds, RenderScale.Identity)
    {
    }

    public EffectTarget(RenderTarget renderTarget, Rect originalBounds, RenderScale correctionScale)
    {
        _target = renderTarget.ShallowCopy();
        OriginalBounds = originalBounds;
        Bounds = originalBounds;
        CorrectionScale = correctionScale;
    }

    public EffectTarget()
    {
    }

    public Rect OriginalBounds { get; set; }

    public Rect Bounds { get; set; }

    /// <summary>
    /// The raster-vs-authoring ratio at which this target's underlying <see cref="RenderTarget"/>
    /// (or wrapped <see cref="RenderNodeOperation"/>) was produced. <see cref="RenderScale.Identity"/>
    /// means the raster matches the bounds 1:1; <c>(4, 4)</c> means the raster is 1/4 linear size and
    /// the compositor will upscale 4× when blitting downstream.
    /// </summary>
    public RenderScale CorrectionScale { get; internal set; } = RenderScale.Identity;

    [Obsolete]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Size Size => Bounds.Size;

    public RenderNodeOperation? NodeOperation => _target as RenderNodeOperation;

    public RenderTarget? RenderTarget => _target as RenderTarget;

    public bool IsEmpty => _target == null;

    public EffectTarget Clone()
    {
        if (RenderTarget != null)
        {
            return new EffectTarget(RenderTarget, OriginalBounds, CorrectionScale) { Bounds = Bounds };
        }
        else
        {
            return this;
        }
    }

    public void Dispose()
    {
        RenderTarget?.Dispose();
        NodeOperation?.Dispose();
        _target = null;
        OriginalBounds = default;
    }

    public void Draw(ImmediateCanvas canvas)
    {
        if (RenderTarget != null)
        {
            canvas.DrawRenderTarget(RenderTarget, default);
        }
        else
        {
            NodeOperation?.Render(canvas);
        }
    }
}
