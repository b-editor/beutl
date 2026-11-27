using Avalonia.Input;
using Avalonia.Threading;
using Beutl.Editor.Components.PropertyEditors;
using Beutl.Graphics.Effects;
using Beutl.Media;
using Beutl.Media.Source;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public partial class PlayerViewModel : IEffectComparisonSelection
{
    internal sealed record EffectComparisonTarget(FilterEffect Effect);

    internal sealed record EffectComparisonFrame(EffectComparisonTarget Target, Ref<Bitmap> Before) : IDisposable
    {
        public void Dispose() => Before.Dispose();
    }

    private readonly object _effectComparisonLock = new();
    private volatile EffectComparisonTarget? _effectComparisonTarget;
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
        => Scene is { } scene && ReferenceEquals(effect.FindHierarchicalParent<ProjectSystem.Scene>(), scene);

    private void SetEffectComparisonTarget(EffectComparisonTarget? target)
    {
        lock (_effectComparisonLock)
        {
            EffectComparisonTarget? previous = _effectComparisonTarget;
            if (ReferenceEquals(previous, target)) return;
            if (previous is not null) previous.Effect.DetachedFromHierarchy -= OnComparisonEffectDetached;
            if (target is not null) target.Effect.DetachedFromHierarchy += OnComparisonEffectDetached;
            _effectComparisonTarget = target;
            ReplaceEffectComparisonFrames(null, null);
            ReplaceImage(EffectComparisonImage, null);
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

    internal static EffectComparisonFrame RenderComparisonBefore(SceneRenderer renderer, MediaTime time, EffectComparisonTarget target)
    {
        renderer.Render(renderer.Compositor.EvaluateGraphicsWithBypassedFilterEffect(time, target.Effect));
        return new(target, Ref<Bitmap>.Create(renderer.Snapshot()));
    }
}
