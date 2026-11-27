using Avalonia.Input;
using Avalonia.Threading;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public partial class PlayerViewModel : IEffectComparisonSelection
{
    internal sealed class EffectComparisonTarget(FilterEffect effect)
    {
        public FilterEffect Effect { get; } = effect;
        public Guid EffectId { get; } = effect.Id.Value;
        internal volatile EffectComparisonCapture Capture = null!;
        internal int CapturePending;
    }

    internal sealed record EffectComparisonCapture(EffectComparisonTarget Target, Lazy<Scene> Scene);

    internal sealed record EffectComparisonRenderScene(
        SceneRenderer Renderer, FilterEffect Effect, EffectComparisonCapture Capture, SceneRenderer Preview);

    internal sealed record EffectComparisonFrame(EffectComparisonTarget Target, Ref<Bitmap> Before) : IDisposable
    {
        public void Dispose() => Before.Dispose();
    }

    private readonly object _effectComparisonLock = new();
    private volatile EffectComparisonTarget? _effectComparisonTarget;
    // The renderer and the scene it was built from are owned by the render thread.
    private EffectComparisonRenderScene? _comparisonRenderScene;
    private Ref<Bitmap>? _effectComparisonBefore;
    private Ref<Bitmap>? _effectComparisonAfter;
    private double _effectComparisonDivider = 0.5;

    internal EffectComparisonTarget? ComparisonTarget => _effectComparisonTarget;
    internal event EventHandler? EffectComparisonTargetChanged;

    internal ReactivePropertySlim<bool> IsEffectComparisonActive { get; } = new();
    internal ReactivePropertySlim<bool> IsSelectingEffectForComparison { get; } = new();
    internal ReactivePropertySlim<Ref<Bitmap>?> EffectComparisonImage { get; } = new();

    IReadOnlyReactiveProperty<bool> IEffectComparisonSelection.IsSelectingEffectForComparison => IsSelectingEffectForComparison;

    private void InitializeEffectComparison()
    {
        IsEffectComparisonActive.DisposeWith(_disposables);
        IsSelectingEffectForComparison.DisposeWith(_disposables);
        EffectComparisonImage.DisposeWith(_disposables);
    }

    internal void ToggleEffectComparison()
    {
        if (_isDisposing) return;
        if (IsEffectComparisonActive.Value)
        {
            EndEffectComparison();
        }
        else
        {
            StopPuppetPinPlacement();
            SetEffectComparisonDivider(0.5);
            IsEffectComparisonActive.Value = true;
            BeginEffectComparisonSelection();
        }
    }

    internal void BeginEffectComparisonSelection()
    {
        if (!IsEffectComparisonActive.Value) return;
        IsSelectingEffectForComparison.Value = true;
        SetEffectComparisonTarget(null);
    }

    internal void HandleEffectComparisonEscape(KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || !IsEffectComparisonActive.Value) return;
        EndEffectComparison();
        e.Handled = true;
    }

    private void EndEffectComparison()
    {
        IsSelectingEffectForComparison.Value = false;
        IsEffectComparisonActive.Value = false;
        SetEffectComparisonTarget(null);
    }

    bool IEffectComparisonSelection.CanCompareEffect(FilterEffect effect) => IsEffectInCurrentScene(effect);

    bool IEffectComparisonSelection.TryChooseComparisonEffect(FilterEffect effect)
    {
        if (!IsSelectingEffectForComparison.Value || !IsEffectInCurrentScene(effect)) return false;
        IsSelectingEffectForComparison.Value = false;
        SetEffectComparisonTarget(new(effect));
        return true;
    }

    private bool IsEffectInCurrentScene(FilterEffect effect)
        => Scene is { } scene && ReferenceEquals(effect.FindHierarchicalParent<Scene>(), scene);

    private void SetEffectComparisonTarget(EffectComparisonTarget? target)
    {
        if (target is not null)
            RefreshComparisonCapture(target);

        EffectComparisonTarget? previous;
        lock (_effectComparisonLock)
        {
            previous = _effectComparisonTarget;
            if (ReferenceEquals(previous, target)) return;
            if (previous is not null) previous.Effect.DetachedFromHierarchy -= OnComparisonEffectDetached;
            if (target is not null) target.Effect.DetachedFromHierarchy += OnComparisonEffectDetached;
            _effectComparisonTarget = target;
            ReplaceEffectComparisonFrames(null, null);
            ReplaceImage(EffectComparisonImage, null);
        }

        if (previous is not null)
        {
            RenderThread.Dispatcher.Dispatch(() =>
            {
                if (ReferenceEquals(_comparisonRenderScene?.Capture.Target, previous))
                    ReleaseComparisonRenderer();
            });
        }

        EffectComparisonTargetChanged?.Invoke(this, EventArgs.Empty);
        if (!IsPlaybackActive)
            QueueRender();
    }

    private void OnComparisonEffectDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) EndIfTarget();
        else Dispatcher.UIThread.Post(EndIfTarget);

        void EndIfTarget()
        {
            if (ReferenceEquals(ComparisonTarget?.Effect, sender))
                EndEffectComparison();
        }
    }

    internal double EffectComparisonDivider
    {
        get
        {
            lock (_effectComparisonLock) return _effectComparisonDivider;
        }
    }

    internal void SetEffectComparisonDivider(double fraction)
    {
        lock (_effectComparisonLock)
        {
            fraction = Math.Clamp(fraction, 0, 1);
            if (_effectComparisonDivider == fraction) return;
            _effectComparisonDivider = fraction;
            ComposeEffectComparisonImage();
        }
    }

    private void UpdateEffectComparisonImage(Ref<Bitmap> after, EffectComparisonFrame comparison)
    {
        lock (_effectComparisonLock)
        {
            // A render or a frame posted by the playback ticker may finish after the user changes targets.
            if (!ReferenceEquals(comparison.Target, _effectComparisonTarget)) return;
            ReplaceEffectComparisonFrames(comparison.Before, after);
            ComposeEffectComparisonImage();
        }
    }

    private void ReplaceEffectComparisonFrames(Ref<Bitmap>? before, Ref<Bitmap>? after)
    {
        _effectComparisonBefore?.Dispose();
        _effectComparisonAfter?.Dispose();
        _effectComparisonBefore = before?.Clone();
        _effectComparisonAfter = after?.Clone();
    }

    private void ComposeEffectComparisonImage()
    {
        if (_effectComparisonBefore is not { Value: var before } || _effectComparisonAfter is not { Value: var after })
            return;

        // Both frames come from the same renderer. Copy pixels at its native precision, including alpha,
        // so the same split can go through the regular SDR or HDR preview surface. Dragging reuses this pair.
        using var split = Ref<Bitmap>.Create(after.Clone());
        int leftBytes = (int)Math.Round(after.Width * _effectComparisonDivider) * after.BytesPerPixel;
        for (int y = 0; y < after.Height; y++)
            before.GetRow(y)[..leftBytes].CopyTo(split.Value.GetRow(y));
        ReplaceImage(EffectComparisonImage, split);
    }

    // Capture on the editing thread. This keeps even effects that sample the scene during drawing
    // on the same revision, without making the editing thread wait for the two draws.
    internal EffectComparisonCapture RefreshComparisonCapture(EffectComparisonTarget target)
    {
        try
        {
            Scene scene = Scene!;
            var capture = SceneSnapshot.Capture(scene);
            var gizmos = scene.Children.SelectMany(element => element.Objects).OfType<Scene3D>()
                .Where(obj => obj.GizmoTarget.Value.HasValue)
                .Select(obj => (Id: obj.Id.Value, Target: obj.GizmoTarget.Value, Mode: obj.GizmoMode.Value))
                .ToArray();
            return target.Capture = new(target, new Lazy<Scene>(() =>
            {
                Scene detached = SceneSnapshot.Materialize(capture);
                foreach (var (id, gizmoTarget, mode) in gizmos)
                {
                    if (ReferenceScope.Find(detached, id) is Scene3D obj)
                    {
                        obj.GizmoTarget.Value = gizmoTarget;
                        obj.GizmoMode.Value = mode;
                    }
                }

                return detached;
            }));
        }
        catch (Exception ex)
        {
            // Let the usual preview error handling report a capture failure on its next render.
            return target.Capture = new(target, new Lazy<Scene>(
                () => throw new InvalidOperationException("The comparison scene could not be captured.", ex)));
        }
    }

    internal void RequestPlaybackComparisonCapture(EffectComparisonTarget target)
    {
        if (Interlocked.Exchange(ref target.CapturePending, 1) != 0)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref target.CapturePending, 0);
            if (ReferenceEquals(target, ComparisonTarget))
                RefreshComparisonCapture(target);
        }, DispatcherPriority.Background);
    }

    internal EffectComparisonRenderScene GetComparisonRenderScene(SceneRenderer preview, EffectComparisonCapture capture)
    {
        if (_comparisonRenderScene is { } current && ReferenceEquals(current.Capture, capture)
            && ReferenceEquals(current.Preview, preview))
            return current;

        Scene scene = capture.Scene.Value;
        var effect = ReferenceScope.Find(scene, capture.Target.EffectId) as FilterEffect
            ?? throw new InvalidOperationException("The compared effect could not be found in the captured scene.");
        ReleaseComparisonRenderer();
        var renderer = EditViewModel.CreatePreviewRenderer(scene, preview.OutputScale);
        return _comparisonRenderScene = new(renderer, effect, capture, preview);
    }

    private void ReleaseComparisonRenderer()
    {
        var previous = _comparisonRenderScene;
        _comparisonRenderScene = null;
        previous?.Renderer.Dispose();
    }

    internal static EffectComparisonFrame RenderComparisonBefore(EffectComparisonRenderScene scene, MediaTime time)
    {
        SceneRenderer renderer = scene.Renderer;
        renderer.Render(renderer.Compositor.EvaluateGraphicsWithBypassedFilterEffect(time, scene.Effect));
        return new(scene.Capture.Target, Ref<Bitmap>.Create(renderer.Snapshot()));
    }
}
