using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics;

public partial class ImmediateCanvas
{
    /// <summary>
    /// The largest normalized basis dot product <see cref="InflateByOneDevicePixel"/> still reads as
    /// orthogonal.
    /// </summary>
    private const float OrthogonalBasisTolerance = 1e-5f;

    public void Pop(int count = -1)
    {
        VerifyAccess();
        int stateFloor = _executionToken is null ? 0 : _callbackStateFloor;

        if (count < 0)
        {
            while (_states.Count > stateFloor
                   && count < 0
                   && _states.TryPop(out CanvasPushedState? state))
            {
                state.Pop(this);
                count++;
            }
        }
        else
        {
            while (_states.Count > stateFloor
                   && _states.Count >= count
                   && _states.TryPop(out CanvasPushedState? state))
            {
                state.Pop(this);
            }
        }
    }

    public PushedState Push()
    {
        VerifyAccess();
        int count = Canvas.Save();

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushLayer(Rect limit = default)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        int count;
        if (limit == default)
        {
            count = Canvas.SaveLayer();
        }
        else
        {
            using (var paint = new SKPaint())
            {
                count = Canvas.SaveLayer(limit.ToSKRect(), paint);
            }
        }

        _states.Push(new CanvasPushedState.LayerPushedState(count));
        return new PushedState(this, _states.Count);
    }

    internal PushedState PushPaint(SKPaint paint, Rect? rect = null)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        int count;
        if (rect.HasValue)
        {
            count = Canvas.SaveLayer(rect.Value.ToSKRect(), paint);
        }
        else
        {
            count = Canvas.SaveLayer(paint);
        }

        _states.Push(new CanvasPushedState.LayerPushedState(count));
        return new PushedState(this, _states.Count);
    }

    /// <summary>
    /// Opens a filter's save layer over <paramref name="contentBounds"/>, widened so that every edge of
    /// the content sits one device pixel inside the layer.
    /// </summary>
    /// <remarks>
    /// The bound keeps a spatial filter from sampling input pixels nobody wrote, but a layer whose
    /// device bounds hug the content loses the coverage of content thinner than one device pixel — the
    /// Ganesh backend keeps only <c>(1 + w) / 2</c> of a w-device-pixel-wide feature. The apron restores
    /// it by giving the rasterizer somewhere to put the antialiased spill of the content, so the replay
    /// does write into the apron and a filter does sample it. What the layer guarantees is therefore a
    /// bound, not an exclusion: no edge is more than one device pixel out from the content it bounds,
    /// and the apron starts transparent because <c>SaveLayer</c> clears it, so it can never carry pixels
    /// nobody wrote. A sheared basis needs a wider logical apron to buy that one perpendicular pixel, so
    /// its device bounding box grows by more than a pixel along the sheared axis.
    /// </remarks>
    internal PushedState PushFilterLayer(SKPaint paint, Rect contentBounds)
        => PushPaint(paint, InflateByOneDevicePixel(contentBounds, _currentTransform));

    /// <summary>
    /// Widens <paramref name="bounds"/> so that <paramref name="transform"/> carries every edge exactly
    /// one device pixel away from the content, measured perpendicular to that edge.
    /// </summary>
    /// <remarks>
    /// Moving a vertical edge by one logical unit displaces it perpendicularly by
    /// <c>|det| / devicePerY</c> device pixels, not by <c>devicePerX</c>, so the apron an axis needs is
    /// the other axis's basis length over the determinant. For an orthogonal basis that is the
    /// reciprocal of the axis's own basis length, which is what every scale and rotation reduces to;
    /// only a sheared basis needs more. A transform that collapses the plane leaves the bounds alone.
    /// </remarks>
    internal static Rect InflateByOneDevicePixel(Rect bounds, Matrix transform)
    {
        float devicePerX = MathF.Sqrt((transform.M11 * transform.M11) + (transform.M12 * transform.M12));
        float devicePerY = MathF.Sqrt((transform.M21 * transform.M21) + (transform.M22 * transform.M22));
        if (!float.IsFinite(devicePerX) || devicePerX <= 0
            || !float.IsFinite(devicePerY) || devicePerY <= 0)
        {
            return bounds;
        }

        // Composing a rotation with an anisotropic scale leaves the basis orthogonal yet misses a zero
        // dot product by up to ~1e-7 of the basis lengths, while the shallowest shear that can move a
        // device pixel misses it by ~1e-3. Keeping the reciprocal form below that split holds every
        // unsheared transform bit-identical instead of moving it by the rounding of the general form.
        float obliqueness = MathF.Abs((transform.M11 * transform.M21) + (transform.M12 * transform.M22));
        if (obliqueness <= devicePerX * devicePerY * OrthogonalBasisTolerance)
            return bounds.Inflate(new Thickness(1f / devicePerX, 1f / devicePerY));

        float area = MathF.Abs((transform.M11 * transform.M22) - (transform.M12 * transform.M21));
        float horizontal = devicePerY / area;
        float vertical = devicePerX / area;
        // A singular basis has no area to divide by and drives the apron to infinity.
        if (!float.IsFinite(horizontal) || !float.IsFinite(vertical))
            return bounds;

        return bounds.Inflate(new Thickness(horizontal, vertical));
    }

    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        int count = Canvas.Save();
        ClipRect(clip, operation);

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushClip(Geometry.Resource geometry, ClipOperation operation = ClipOperation.Intersect)
    {
        VerifyAccess();
        VerifyCallbackResource(geometry, nameof(geometry));
        int count = Canvas.Save();
        ClipPath(geometry, operation);

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushOpacity(float opacity)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        float oldOpacity = Opacity;
        Opacity *= opacity;

        if (oldOpacity == 1f && opacity == 1f)
        {
            // Skia sizes an isolation layer from the active clip, and rasterizing into that smaller
            // surface changes antialiased coverage. A fully opaque group is SrcOver-associative, so
            // the layer would only be an identity pass that perturbs coverage.
            _states.Push(new CanvasPushedState.SKCanvasPushedState(Canvas.Save()));
            return new PushedState(this, _states.Count);
        }

        // A float color filter preserves 16-bit opacity; SaveLayer alpha and DstIn masks quantize to 8 bits.
        // SaveLayer copies the paint, so the filter need not outlive this call.
        int count;
        using (var paint = new SKPaint())
        using (SKColorFilter filter = CreateOpacityColorFilter(opacity))
        {
            paint.ColorFilter = filter;
            count = Canvas.SaveLayer(paint);
        }

        _states.Push(new CanvasPushedState.OpacityPushedState(oldOpacity, count));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushOpacityMask(Brush.Resource mask, Rect bounds, bool invert = false)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        var paint = new SKPaint();

        int count = Canvas.SaveLayer(paint);
        new BrushConstructor(
            bounds,
            mask,
            (BlendMode)paint.BlendMode,
            _currentDensity,
            MaxWorkingScale,
            Intent,
            DrawableBrushMaterializer,
            RenderTargetLeaseSession).ConfigurePaint(paint);
        _states.Push(new CanvasPushedState.MaskPushedState(count, invert, paint));
        return new PushedState(this, _states.Count);
    }

    public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend)
    {
        VerifyAccess();
        int count = Canvas.Save();

        if (transformOperator == TransformOperator.Prepend)
        {
            Transform = Transform.Prepend(matrix);
        }
        else if (transformOperator == TransformOperator.Append)
        {
            // Append composes after the scene's own transform, not after the base CTM the target's density
            // lives in: appending a translation has to move content the same logical distance whatever
            // density the frame is rendered at, and Set already draws the same line.
            Transform = _currentBaseTransform.TryInvert(out Matrix inverseBase)
                ? Transform.Append(inverseBase).Append(matrix).Append(_currentBaseTransform)
                : Transform.Append(matrix);
        }
        else
        {
            // Set re-applies the current base CTM so the canvas stays in the right coordinate space.
            Transform = _currentBaseTransform.Prepend(matrix);
        }

        _states.Push(new CanvasPushedState.SKCanvasPushedState(count));
        return new PushedState(this, _states.Count);
    }

    /// <summary>
    /// Enter absolute device space (CTM = identity, <see cref="Density"/> = 1) for the lifetime
    /// of the returned state. Restored on dispose.
    /// </summary>
    public PushedState PushDeviceSpace()
    {
        VerifyAccess();

        // No-op when already in absolute device space.
        if (_currentDensity == 1f && _currentTransform.IsIdentity && _currentBaseTransform.IsIdentity)
        {
            _states.Push(CanvasPushedState.NoOpPushedState.Instance);
            return new PushedState(this, _states.Count);
        }

        int count = Canvas.Save();
        var state = new CanvasPushedState.DeviceSpacePushedState(count, _currentDensity, _currentBaseTransform);

        Canvas.SetMatrix((SKMatrix44)Matrix.Identity.ToSKMatrix());
        _currentTransform = Matrix.Identity;
        _currentDensity = 1f;
        _currentBaseTransform = Matrix.Identity;

        _states.Push(state);
        return new PushedState(this, _states.Count);
    }

    public PushedState PushBlendMode(BlendMode blendMode)
    {
        VerifyAccess();
        VerifyHiddenLayerOperation();
        BlendMode tmp = BlendMode;
        bool previousProductRectangleCoverage = _productRectangleCoverage;
        BlendMode = blendMode;
        _productRectangleCoverage = blendMode == BlendMode.DstIn;
        var paint = new SKPaint();
        paint.BlendMode = (SKBlendMode)blendMode;

        int count = Canvas.SaveLayer(paint);
        _states.Push(new CanvasPushedState.BlendModePushedState(
            tmp,
            previousProductRectangleCoverage,
            count,
            paint));
        return new PushedState(this, _states.Count);
    }

    internal PushedState PushDirectBlendMode(BlendMode blendMode)
    {
        VerifyAccess();
        int count = Canvas.Save();
        BlendMode previousBlendMode = BlendMode;
        BlendMode? previousDirectBlendMode = _directBlendMode;
        BlendMode = blendMode;
        _directBlendMode = blendMode;
        _states.Push(new CanvasPushedState.DirectBlendModePushedState(
            previousBlendMode,
            previousDirectBlendMode,
            count));
        return new PushedState(this, _states.Count);
    }

    /// <summary>
    /// Scales a premultiplied layer by a float opacity, without the 8-bit step Skia's own layer-alpha and
    /// DstIn-mask paths both introduce.
    /// </summary>
    /// <remarks>
    /// Multiplying all four premultiplied components by the same factor is exactly what group opacity means,
    /// and it leaves the straight color untouched. A color-matrix filter would express the same scale but
    /// clamps its result to [0, 1], which would crush the out-of-range values an RGBA16F layer is allowed to
    /// carry; a runtime color filter has no such clamp.
    /// </remarks>
    private static SKRuntimeEffect CreateOpacityScaleEffect()
    {
        const string source =
            """
            uniform half opacity;

            half4 main(half4 color)
            {
                return color * opacity;
            }
            """;
        return SKRuntimeEffect.CreateColorFilter(source, out string? errorText)
               ?? throw new InvalidOperationException(
                   $"Failed to compile the opacity scale color filter: {errorText}");
    }

    /// <summary>Builds the float-precision group-opacity color filter for one normalized opacity.</summary>
    private static SKColorFilter CreateOpacityColorFilter(float opacity)
    {
        using var uniforms = new SKRuntimeEffectUniforms(s_opacityScaleEffect.Value)
        {
            { "opacity", opacity },
        };
        return s_opacityScaleEffect.Value.ToColorFilter(uniforms);
    }
}
