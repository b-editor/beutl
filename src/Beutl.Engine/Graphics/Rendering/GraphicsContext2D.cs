using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.TextFormatting;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

public sealed class GraphicsContext2D(
    ContainerRenderNode container,
    Size canvasSize = default,
    float outputScale = 1f)
    : IDisposable, IPopable
{
    private readonly Stack<(ContainerRenderNode Container, int OperationIndex, bool HasChanges)> _nodes = [];
    private int _drawOperationindex;

    private readonly ContainerRenderNode _rootContainer = container;
    private ContainerRenderNode _container = container;

    // Belongs to the innermost open scope, not to the pass: it accumulates until that scope closes.
    private bool _hasChanges;
    private bool _faulted;

    /// <summary>The logical viewport size (float, not rounded to device pixels).</summary>
    public Size Size => canvasSize;

    /// <summary>
    /// The output scale <c>s_out</c> this context was built for, normalized by
    /// <see cref="RenderScaleUtilities.SanitizeOutputScale"/> so it is always a usable density.
    /// </summary>
    public float OutputScale { get; } = RenderScaleUtilities.SanitizeOutputScale(outputScale);

    // The number of open scopes. A PushedState at level n closes the scope at that depth and those inside it.
    internal int Depth => _nodes.Count;

    private void Add(RenderNode node)
    {
        bool replacementAttempted = false;
        try
        {
            if (_drawOperationindex < _container.Children.Count)
            {
                replacementAttempted = true;
                _container.SetChild(_drawOperationindex, node);
            }
            else
            {
                _container.AddChild(node);
            }
        }
        catch
        {
            // SetChild installs before it disposes, so an installed replacement means the previous child is
            // already torn down — and RenderNode.Dispose sets IsDisposed only after OnDispose and
            // Cache.Dispose return, so a failed teardown leaves a node the pipeline's guards wave through.
            bool ownershipTransferred = replacementAttempted
                && ReferenceEquals(_container.Children[_drawOperationindex], node);

            if (!ownershipTransferred)
            {
                try
                {
                    node.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    // Preserve the recording failure that prevented ownership transfer.
                    ReportCleanupFailure(cleanupFailure, "disposing a rejected render node");
                }
            }

            throw;
        }

        _hasChanges = true;
    }

    private void AddAndPush(ContainerRenderNode node)
    {
        Add(node);
        Push(node);
    }

    private void Push(ContainerRenderNode node)
    {
        _nodes.Push((_container, _drawOperationindex + 1, _hasChanges));

        _drawOperationindex = 0;
        _container = node;
        _hasChanges = false;
    }

    private void CloseScope(in (ContainerRenderNode Container, int OperationIndex, bool HasChanges) state)
    {
        if (!_faulted)
        {
            TrimTrailingNodes(_container, _drawOperationindex);
            if (_hasChanges)
            {
                _container.MarkChanged();
            }
        }

        bool scopeChanges = _hasChanges;
        _container = state.Container;
        _drawOperationindex = state.OperationIndex;
        _hasChanges = state.HasChanges | scopeChanges;
    }

    private T? Next<T>() where T : RenderNode
    {
        if (_drawOperationindex < _container.Children.Count)
        {
            var node = _container.Children[_drawOperationindex];
            // is-asだと、Next<FilterEffectRenderNode>()のような呼び出しで継承されていないノードが欲しいのにNodeGraphFilterEffectRenderNodeが返ってきてしまうので、
            // GetType() == typeof(T)で厳密に型を比較する。
            if (node.GetType() == typeof(T))
            {
                return (T)node;
            }
        }

        return null;
    }

    private RenderNode? Next()
    {
        return _drawOperationindex < _container.Children.Count ? _container.Children[_drawOperationindex] : null;
    }

    public void Dispose()
    {
        if (_faulted)
            return;

        TrimTrailingNodes(_container, _drawOperationindex);
        if (_hasChanges)
        {
            _container.MarkChanged();
        }
    }

    private bool BeginRecordingOperation()
    {
        bool wasFaulted = _faulted;
        _faulted = true;
        return wasFaulted;
    }

    private void CompleteRecordingOperation(bool wasFaulted)
    {
        _faulted = wasFaulted;
    }

    private void TrimTrailingNodes(ContainerRenderNode container, int start)
    {
        RenderNode[] removed;
        try
        {
            int count = container.Children.Count - start;
            if (count == 0)
                return;

            removed = [.. container.Children.Skip(start)];
            container.RemoveRange(start, count);
            container.MarkChanged();
            _hasChanges = true;
        }
        catch (Exception cleanupFailure)
        {
            // Recording cleanup must not replace an exception already leaving the caller.
            ReportCleanupFailure(cleanupFailure, "detaching trailing render nodes");
            return;
        }

        foreach (RenderNode node in removed)
        {
            try
            {
                node.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                // Continue discharging every detached node.
                ReportCleanupFailure(cleanupFailure, "disposing a detached render node");
            }
        }
    }

    private static void ThrowIfDisposed(Brush.Resource? fill, Pen.Resource? pen)
    {
        if (fill != null) ObjectDisposedException.ThrowIf(fill.IsDisposed, fill);
        if (pen != null) ObjectDisposedException.ThrowIf(pen.IsDisposed, pen);
    }

    // The node callbacks validate their arguments, so an invalid argument still faults the recording pass.
    private static void ThrowIfUnusable(
        EngineObject.Resource resource,
        Brush.Resource? fill,
        Pen.Resource? pen,
        string paramName)
    {
        ThrowIfDisposed(fill, pen);
        ThrowIfUnusable(resource, paramName);
    }

    private static void ThrowIfUnusable(EngineObject.Resource? resource, string paramName)
    {
        ArgumentNullException.ThrowIfNull(resource, paramName);
        ObjectDisposedException.ThrowIf(resource.IsDisposed, resource);
    }

    private static void ReportCleanupFailure(Exception exception, string operation)
    {
        try
        {
            Log.CreateLogger<GraphicsContext2D>().LogWarning(
                exception,
                "GraphicsContext2D cleanup failed while {Operation}; continuing cleanup.",
                operation);
        }
        catch
        {
            // Logging must not replace either the recording failure or the cleanup failure being suppressed.
        }
    }

    public void Reset()
    {
        _drawOperationindex = 0;
        _nodes.Clear();
        _container = _rootContainer;
        _faulted = false;
        _hasChanges = false;
    }

    public MemoryNode<T> UseMemory<T>(T defaultValue)
    {
        bool wasFaulted = BeginRecordingOperation();
        MemoryNode<T>? next = Next<MemoryNode<T>>();

        if (next == null)
        {
            next = new MemoryNode<T>(defaultValue);
            Add(next);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
        return next;
    }

    public MemoryNode<T?> UseMemory<T>()
    {
        return UseMemory<T?>(default);
    }

    public void Clear() => Clear(default);

    public void Clear(Color color)
        => DrawNode(
            color,
            static c => new ClearRenderNode(c),
            static (node, c) => node.Update(c));

    public void DrawImageSource(ImageSource.Resource source, Brush.Resource? fill, Pen.Resource? pen)
        => DrawNode(
            (Source: source, Fill: fill, Pen: pen),
            static p =>
            {
                ThrowIfUnusable(p.Source, p.Fill, p.Pen, nameof(source));
                return new ImageSourceRenderNode(p.Source, p.Fill, p.Pen);
            },
            static (node, p) =>
            {
                ThrowIfUnusable(p.Source, p.Fill, p.Pen, nameof(source));
                return node.Update(p.Source, p.Fill, p.Pen);
            });

    public void DrawVideoSource(VideoSource.Resource source, int frame, Brush.Resource? fill, Pen.Resource? pen)
        => DrawNode(
            (Source: source, Frame: frame, Fill: fill, Pen: pen),
            static p =>
            {
                ThrowIfUnusable(p.Source, p.Fill, p.Pen, nameof(source));
                return new VideoSourceRenderNode(p.Source, p.Frame, p.Fill, p.Pen);
            },
            static (node, p) =>
            {
                ThrowIfUnusable(p.Source, p.Fill, p.Pen, nameof(source));
                return node.Update(p.Source, p.Frame, p.Fill, p.Pen);
            });

    public void DrawEllipse(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
        => DrawNode(
            (Rect: rect, Fill: fill, Pen: pen),
            static p =>
            {
                ThrowIfDisposed(p.Fill, p.Pen);
                return new EllipseRenderNode(p.Rect, p.Fill, p.Pen);
            },
            static (node, p) =>
            {
                ThrowIfDisposed(p.Fill, p.Pen);
                return node.Update(p.Rect, p.Fill, p.Pen);
            });

    public void DrawGeometry(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
        => DrawNode(
            (Geometry: geometry, Fill: fill, Pen: pen),
            static p =>
            {
                ThrowIfUnusable(p.Geometry, p.Fill, p.Pen, nameof(geometry));
                return new GeometryRenderNode(p.Geometry, p.Fill, p.Pen);
            },
            static (node, p) =>
            {
                ThrowIfUnusable(p.Geometry, p.Fill, p.Pen, nameof(geometry));
                return node.Update(p.Geometry, p.Fill, p.Pen);
            });

    public void DrawRectangle(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
        => DrawNode(
            (Rect: rect, Fill: fill, Pen: pen),
            static p =>
            {
                ThrowIfDisposed(p.Fill, p.Pen);
                return new RectangleRenderNode(p.Rect, p.Fill, p.Pen);
            },
            static (node, p) =>
            {
                ThrowIfDisposed(p.Fill, p.Pen);
                return node.Update(p.Rect, p.Fill, p.Pen);
            });

    public void DrawText(FormattedText text, Brush.Resource? fill, Pen.Resource? pen)
        => DrawNode(
            (Text: text, Fill: fill, Pen: pen),
            static p =>
            {
                ThrowIfDisposed(p.Fill, p.Pen);
                ArgumentNullException.ThrowIfNull(p.Text, nameof(text));
                return new TextRenderNode(p.Text, p.Fill, p.Pen);
            },
            static (node, p) =>
            {
                ThrowIfDisposed(p.Fill, p.Pen);
                ArgumentNullException.ThrowIfNull(p.Text, nameof(text));
                return node.Update(p.Text, p.Fill, p.Pen);
            });

    public void DrawDrawable(Drawable.Resource drawable)
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(drawable);
        ObjectDisposedException.ThrowIf(drawable.IsDisposed, drawable);

        ContainerRenderNode parent = _container;
        int operationIndex = _drawOperationindex;
        DrawableRenderNode? next = Next<DrawableRenderNode>();

        if (next == null)
        {
            AddAndPush(new DrawableRenderNode(drawable));
        }
        else
        {
            _hasChanges |= next.Update(drawable);
            Push(next);
        }

        int count = _nodes.Count;
        CompleteRecordingOperation(wasFaulted);
        try
        {
            drawable.Render(this);
        }
        catch
        {
            _faulted = true;
            TrimTrailingNodes(parent, operationIndex);
            throw;
        }
        finally
        {
            Pop(count);
        }
    }

    public void DrawNode(RenderNode node)
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(node);
        ObjectDisposedException.ThrowIf(node.IsDisposed, node);

        RenderNode? next = Next();

        if (next == null || !node.Equals(next))
        {
            Add(node);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawNode<TNode, TParams>(in TParams parameters, Func<TParams, TNode> createNode,
        Func<TNode, TParams, bool> updateNode)
        where TNode : RenderNode
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(createNode);
        ArgumentNullException.ThrowIfNull(updateNode);

        TNode? next = Next<TNode>();

        if (next == null)
        {
            TNode node = createNode(parameters);
            Add(node);
        }
        else
        {
            _hasChanges |= updateNode(next, parameters);
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
    }

    public void DrawBackdrop(IBackdrop backdrop)
        => DrawNode(
            (Backdrop: backdrop, Bounds: new Rect(canvasSize)),
            static p =>
            {
                ArgumentNullException.ThrowIfNull(p.Backdrop, nameof(backdrop));
                return new DrawBackdropRenderNode(p.Backdrop, p.Bounds);
            },
            static (node, p) =>
            {
                ArgumentNullException.ThrowIfNull(p.Backdrop, nameof(backdrop));
                return node.Update(p.Backdrop, p.Bounds);
            });

    public IBackdrop Snapshot()
    {
        bool wasFaulted = BeginRecordingOperation();
        SnapshotBackdropRenderNode? next = Next<SnapshotBackdropRenderNode>();

        if (next == null)
        {
            Add(next = new SnapshotBackdropRenderNode());
        }

        ++_drawOperationindex;
        CompleteRecordingOperation(wasFaulted);
        return next;
    }

    public void Pop(int count = -1)
    {
        if (count < 0)
        {
            while (count < 0
                   && _nodes.TryPop(out (ContainerRenderNode, int, bool) state))
            {
                CloseScope(state);
                count++;
            }
        }
        else
        {
            while (_nodes.Count >= count
                   && _nodes.TryPop(out (ContainerRenderNode, int, bool) state))
            {
                CloseScope(state);
            }
        }
    }

    public PushedState Push()
        => PushNode(0, static _ => new PushRenderNode(), static (_, _) => false);

    public PushedState PushLayer(Rect limit = default)
        => PushNode(limit, static limit => new LayerRenderNode(limit), static (node, limit) => node.Update(limit));

    public PushedState PushBlendMode(BlendMode blendMode)
        => PushNode(
            blendMode,
            static blendMode => new BlendModeRenderNode(blendMode),
            static (node, blendMode) => node.Update(blendMode));

    public PushedState PushClip(Rect clip, ClipOperation operation = ClipOperation.Intersect)
        => PushNode(
            (Clip: clip, Operation: operation),
            static p => new RectClipRenderNode(p.Clip, p.Operation),
            static (node, p) => node.Update(p.Clip, p.Operation));

    public PushedState PushClip(Geometry.Resource geometry, ClipOperation operation = ClipOperation.Intersect)
        => PushNode(
            (Geometry: geometry, Operation: operation),
            static p =>
            {
                ThrowIfUnusable(p.Geometry, nameof(geometry));
                return new GeometryClipRenderNode(p.Geometry, p.Operation);
            },
            static (node, p) =>
            {
                ThrowIfUnusable(p.Geometry, nameof(geometry));
                return node.Update(p.Geometry, p.Operation);
            });

    public PushedState PushOpacity(float opacity)
        => PushNode(
            opacity,
            static opacity => new OpacityRenderNode(opacity),
            static (node, opacity) => node.Update(opacity));

    public PushedState PushFilterEffect(FilterEffect.Resource effect)
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(effect);
        ObjectDisposedException.ThrowIf(effect.IsDisposed, effect);

        PushedState result = effect.PushInto(this);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }

    public PushedState PushOpacityMask(Brush.Resource mask, Rect bounds, bool invert = false)
        => PushNode(
            (Mask: mask, Bounds: bounds, Invert: invert),
            static p =>
            {
                ThrowIfUnusable(p.Mask, nameof(mask));
                return new OpacityMaskRenderNode(p.Mask, p.Bounds, p.Invert);
            },
            static (node, p) =>
            {
                ThrowIfUnusable(p.Mask, nameof(mask));
                return node.Update(p.Mask, p.Bounds, p.Invert);
            });

    public PushedState PushTransform(Matrix matrix, TransformOperator transformOperator = TransformOperator.Prepend)
        => PushNode(
            (Matrix: matrix, Operator: transformOperator),
            static p => new TransformRenderNode(p.Matrix, p.Operator),
            static (node, p) => node.Update(p.Matrix, p.Operator));

    public PushedState PushTransform(Transform.Resource transform,
        TransformOperator transformOperator = TransformOperator.Prepend)
        => PushNode(
            (Transform: transform, Operator: transformOperator),
            static p =>
            {
                ThrowIfUnusable(p.Transform, nameof(transform));
                return new TransformRenderNode(p.Transform.Matrix, p.Operator);
            },
            static (node, p) =>
            {
                ThrowIfUnusable(p.Transform, nameof(transform));
                return node.Update(p.Transform.Matrix, p.Operator);
            });

    public PushedState PushNode<TNode, TParams>(in TParams parameters, Func<TParams, TNode> createNode,
        Func<TNode, TParams, bool> updateNode)
        where TNode : ContainerRenderNode
    {
        bool wasFaulted = BeginRecordingOperation();
        ArgumentNullException.ThrowIfNull(createNode);
        ArgumentNullException.ThrowIfNull(updateNode);

        TNode? next = Next<TNode>();

        if (next == null)
        {
            TNode node = createNode(parameters);
            AddAndPush(node);
        }
        else
        {
            _hasChanges |= updateNode(next, parameters);
            Push(next);
        }

        var result = new PushedState(this, _nodes.Count);
        CompleteRecordingOperation(wasFaulted);
        return result;
    }
}
