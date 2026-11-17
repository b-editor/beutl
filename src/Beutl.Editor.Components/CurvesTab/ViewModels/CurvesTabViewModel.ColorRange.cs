using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Controls;
using Beutl.Editor.Components.CurvesTab.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Bitmap = Beutl.Media.Bitmap;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.Editor.Components.CurvesTab.ViewModels;

// The color range on the hue curves: a color picked from the preview, where it stands on the curve shown, and the hues
// around it that the range shapes. The color is read from the frame drawn without this effect, so what the curves do
// cannot move it; picking it again reads the same color.
public sealed partial class CurvesTabViewModel
{
    // Degrees across the range: by default, one of the six hues from halfway to each neighbor.
    private const float DefaultRangeWidth = 60;
    private const float MinRangeWidth = 1;
    private const float MaxRangeWidth = 300;

    private static readonly ILogger s_logger = Log.CreateLogger<CurvesTabViewModel>();
    private CancellationTokenSource? _pick;
    private Task<(Bitmap Frame, float Scale)?>? _pickFrame;
    private MediaTime _pickFrameTime;
    private BtlPoint? _hoveredPoint;
    private CurveColorSample? _pickedColor;
    private RangeEdit? _rangeEdit;
    private bool _isShaping;

    // On while the preview waits for a click on a color; turning it off cancels the pick.
    public ReactivePropertySlim<bool> IsPickingColor { get; } = new();

    // Whether the shown curve has a color range: a hue curve of an effect.
    public ReadOnlyReactivePropertySlim<bool> CanPickColor { get; }

    public ReactivePropertySlim<bool> HasPickedColor { get; } = new();

    // Whether the range's editors show: a color is picked for a hue curve, and none is being picked.
    public ReadOnlyReactivePropertySlim<bool> ShowColorRange { get; }

    public ReactivePropertySlim<IBrush?> PickedColorBrush { get; } = new();

    public ReactivePropertySlim<string?> PickedColorTip { get; } = new();

    // Where the picked color stands on the curve shown, from 0 to 1, and the same in degrees.
    public ReactivePropertySlim<double?> MarkerPosition { get; } = new();

    public ReactivePropertySlim<string> MarkerPositionText { get; } = new(string.Empty);

    // Where the color under the eyedropper stands, while picking.
    public ReactivePropertySlim<double?> HoverPosition { get; } = new();

    // Degrees across the range, and the same as a part of the curve's width.
    public ReactivePropertySlim<float> RangeWidth { get; } = new(DefaultRangeWidth);

    public ReactivePropertySlim<double> MarkerRange { get; } = new(DefaultRangeWidth / 360);

    // The curve's change at the picked color in the units of the curve shown: degrees of hue, or percent of the
    // saturation or luminance.
    public ReactivePropertySlim<float> RangeValue { get; } = new();

    public ReactivePropertySlim<string> RangeValueHeader { get; } = new(string.Empty);

    public ReactivePropertySlim<string> RangeValueUnit { get; } = new(string.Empty);

    private void InitializeColorRange()
    {
        IsPickingColor
            .Subscribe(picking =>
            {
                if (picking) PickColor();
                else CancelPick();
            })
            .DisposeWith(Disposables);

        CanPickColor
            .Where(canPick => !canPick)
            .Subscribe(_ => IsPickingColor.Value = false)
            .DisposeWith(Disposables);

        // Playback moves the frame away from the one being read.
        _player.IsPlaying
            .Where(playing => playing)
            .Subscribe(_ => IsPickingColor.Value = false)
            .DisposeWith(Disposables);

        SelectedGroup
            .Subscribe(_ => UpdateColorRange())
            .DisposeWith(Disposables);

        RangeWidth
            .Subscribe(width => MarkerRange.Value = width / 360)
            .DisposeWith(Disposables);
    }

    private static bool IsHueGroup(CurveGroup group)
        => group is CurveGroup.HueVsHue or CurveGroup.HueVsSaturation or CurveGroup.HueVsLuminance;

    private IProperty<CurveMap>? SelectedHueCurve() => Effect.Value is not { } effect
        ? null
        : SelectedGroup.Value switch
        {
            CurveGroup.HueVsHue => effect.HueVsHue,
            CurveGroup.HueVsSaturation => effect.HueVsSaturation,
            CurveGroup.HueVsLuminance => effect.HueVsLuminance,
            _ => null,
        };

    // The shown curve's value at the picked color follows its edits, undo included, and the marker follows hue vs hue.
    private void ObserveHueCurves(Curves effect)
    {
        foreach (IProperty<CurveMap> curve in (IProperty<CurveMap>[])[effect.HueVsHue, effect.HueVsSaturation, effect.HueVsLuminance])
        {
            EventHandler<PropertyValueChangedEventArgs<CurveMap>> handler = (_, _) => UpdateColorRange();
            curve.ValueChanged += handler;
            EffectDisposables.Add(Disposable.Create(() => curve.ValueChanged -= handler));
        }
    }

    // Forgets the picked color, for the button beside it.
    public void ClearPickedColor()
    {
        IsPickingColor.Value = false;
        _pickedColor = null;
        _rangeEdit = null;
        UpdateColorRange();
    }

    // Another effect can take the tab with the same curve shown, which keeps CanPickColor on, so the pick ends here.
    private void ClearColorRange()
    {
        IsPickingColor.Value = false;
        CancelPick();
        _pickedColor = null;
        _rangeEdit = null;
        UpdateColorRange();
    }

    // Waits for clicks on the preview until one lands on a color; a click on nothing waits for another.
    private async void PickColor()
    {
        CancelPick();
        if (EditorContext.GetService<IPreviewEyedropper>() is not { } eyedropper || Effect.Value is not { } effect)
        {
            IsPickingColor.Value = false;
            return;
        }

        var pick = new CancellationTokenSource();
        _pick = pick;
        try
        {
            while (true)
            {
                BtlPoint? point = await eyedropper.PickPointAsync(
                    hovered => OnPickHovered(eyedropper, effect, hovered, pick.Token), pick.Token);
                if (point is not { } clicked || pick.IsCancellationRequested)
                    break;

                (Bitmap Frame, float Scale)? frame = await DrawPickFrame(eyedropper, effect, pick.Token);
                if (pick.IsCancellationRequested)
                    break;

                if (frame is { } drawn && Sample(drawn, clicked) is { } color)
                {
                    _pickedColor = color;
                    _rangeEdit = null;
                    UpdateColorRange();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Picking a color for the curves failed.");
        }
        finally
        {
            // The source stays undisposed: a frame drawn for a hover may still hold its token.
            if (_pick == pick)
            {
                EndPick();
                IsPickingColor.Value = false;
            }
        }
    }

    private void CancelPick()
    {
        if (_pick is { } pick)
        {
            EndPick();
            pick.Cancel();
        }
    }

    private void EndPick()
    {
        _pick = null;
        _hoveredPoint = null;
        HoverPosition.Value = null;
        ReleasePickFrame();
    }

    // The frame goes once it is drawn, as it may still be.
    private void ReleasePickFrame()
    {
        if (_pickFrame is not { } frame)
            return;

        _pickFrame = null;
        frame.ContinueWith(
            static task =>
            {
                if (task.IsCompletedSuccessfully)
                    task.Result?.Frame.Dispose();
            },
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void OnPickHovered(IPreviewEyedropper eyedropper, Curves effect, BtlPoint? point, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        _hoveredPoint = point;
        if (point is null)
        {
            HoverPosition.Value = null;
            return;
        }

        Task<(Bitmap Frame, float Scale)?> frame = DrawPickFrame(eyedropper, effect, token);
        UpdateHover(frame);
    }

    private void UpdateHover(Task<(Bitmap Frame, float Scale)?> frame)
    {
        HoverPosition.Value = frame.IsCompletedSuccessfully && frame.Result is { } drawn
            && _hoveredPoint is { } point && Sample(drawn, point) is { } color
            ? PositionOn(color)
            : null;
    }

    // The frame the eyedropper reads, drawn once for each frame of the playhead it waits on.
    private Task<(Bitmap Frame, float Scale)?> DrawPickFrame(IPreviewEyedropper eyedropper, Curves effect, CancellationToken token)
    {
        MediaTime time = EditorContext.GetService<IEditorClock>()?.CurrentTime.Value ?? default;
        if (_pickFrame is { } current && _pickFrameTime == time)
            return current;

        ReleasePickFrame();
        Task<(Bitmap Frame, float Scale)?> frame = eyedropper.DrawFrameWithout(effect, token);
        _pickFrame = frame;
        _pickFrameTime = time;
        ShowHoverWhenDrawn(frame);
        return frame;
    }

    // The pointer may rest while the frame is drawn, so the hover shows as soon as it is.
    private async void ShowHoverWhenDrawn(Task<(Bitmap Frame, float Scale)?> frame)
    {
        try
        {
            await frame;
        }
        catch
        {
            return;
        }

        if (ReferenceEquals(frame, _pickFrame))
            UpdateHover(frame);
    }

    private static CurveColorSample? Sample((Bitmap Frame, float Scale) frame, BtlPoint point)
        => CurveColorSample.Read(
            frame.Frame,
            (int)MathF.Floor(point.X * frame.Scale),
            (int)MathF.Floor(point.Y * frame.Scale));

    // Hue vs hue reads the color's own hue; hue vs saturation and hue vs luminance read it after hue vs hue turned it.
    private double? PositionOn(CurveColorSample color) => SelectedGroup.Value switch
    {
        CurveGroup.HueVsHue => color.HueOn(null),
        CurveGroup.HueVsSaturation or CurveGroup.HueVsLuminance => color.HueOn(Effect.Value?.HueVsHue.Value),
        _ => null,
    };

    private void UpdateColorRange()
    {
        HasPickedColor.Value = _pickedColor is not null;
        if (_pickedColor is not { } color)
        {
            PickedColorBrush.Value = null;
            PickedColorTip.Value = null;
            MarkerPosition.Value = null;
            MarkerPositionText.Value = string.Empty;
            return;
        }

        PickedColorBrush.Value = new ImmutableSolidColorBrush(color.ToColor().ToAvaColor());
        PickedColorTip.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Curves_PickedColorTip,
            MathF.Round(color.Hue * 360) % 360,
            MathF.Round(color.Saturation * 100),
            MathF.Round(color.Value * 100));

        double? position = PositionOn(color);
        MarkerPosition.Value = position;
        MarkerPositionText.Value = position is { } x
            ? string.Format(CultureInfo.CurrentCulture, "{0:0}°", Math.Round(x * 360) % 360)
            : string.Empty;

        CurveGroup group = SelectedGroup.Value;
        RangeValueHeader.Value = group switch
        {
            CurveGroup.HueVsHue => Strings.Hue,
            CurveGroup.HueVsSaturation => Strings.Saturation,
            _ => Strings.Curves_Luminance,
        };
        RangeValueUnit.Value = group == CurveGroup.HueVsHue ? "°" : "%";

        // A value being dragged stays as the box has it, rather than as the curve rounds it.
        if (!_isShaping && SelectedHueCurve() is { } curve && position is { } center)
            RangeValue.Value = MathF.Round(ToRangeValue(group, curve.Value.Evaluate((float)center)), 1);
    }

    // While the range's value is dragged, and when it is set: shapes the shown curve around the picked color.
    public void PreviewRangeValue(float value) => SetRangeValue(value, commit: false);

    public void ConfirmRangeValue(float value) => SetRangeValue(value, commit: true);

    public void PreviewRangeWidth(float width) => SetRangeWidth(width, commit: false);

    public void ConfirmRangeWidth(float width) => SetRangeWidth(width, commit: true);

    private void SetRangeValue(float value, bool commit)
    {
        CurveGroup group = SelectedGroup.Value;
        value = group == CurveGroup.HueVsHue ? Math.Clamp(value, -180, 180) : Math.Clamp(value, 0, 200);
        if (SelectedHueCurve() is { } curve && MarkerPosition.Value is { } position)
        {
            float center = (float)position;
            if (_rangeEdit is not { } edit || !edit.Continues(curve, center))
                _rangeEdit = edit = new RangeEdit(curve, center, curve.Value);

            edit.Value = FromRangeValue(group, value);
            Shape(edit, commit);
        }

        RangeValue.Value = value;
        RangeValue.ForceNotify();
    }

    // A narrower range gives back what a wider one covered, so the range is shaped again from the curve as it was
    // before the first edit. Without a change to the curve yet, the width only marks the hues.
    private void SetRangeWidth(float width, bool commit)
    {
        RangeWidth.Value = Math.Clamp(width, MinRangeWidth, MaxRangeWidth);
        if (_rangeEdit is { } edit && SelectedHueCurve() is { } curve && MarkerPosition.Value is { } position
            && edit.Continues(curve, (float)position))
        {
            Shape(edit, commit);
        }

        RangeWidth.ForceNotify();
    }

    private void Shape(RangeEdit edit, bool commit)
    {
        CurveMap shaped = HueRangeCurve.Shape(edit.Original, edit.Center, RangeWidth.Value / 360, edit.Value);
        _isShaping = true;
        try
        {
            edit.Written = shaped;
            edit.Curve.Value = shaped;
        }
        finally
        {
            _isShaping = false;
        }

        if (commit)
            EditorContext.GetService<HistoryManager>()?.Commit(CommandNames.EditProperty);
    }

    private static float ToRangeValue(CurveGroup group, float curveValue)
        => group == CurveGroup.HueVsHue ? (curveValue - 0.5f) * 360 : curveValue * 200;

    // Curve value 0.5 is no change: for hue vs hue a turn of 0.5 either way, for the others 0x to 2x.
    private static float FromRangeValue(CurveGroup group, float value)
        => Math.Clamp(group == CurveGroup.HueVsHue ? 0.5f + value / 360 : value / 200, 0, 1);

    // The edits to the range on one curve since it was last changed some other way, and the curve before the first of
    // them, which each edit shapes anew.
    private sealed class RangeEdit(IProperty<CurveMap> curve, float center, CurveMap original)
    {
        public IProperty<CurveMap> Curve { get; } = curve;

        public float Center { get; } = center;

        public CurveMap Original { get; } = original;

        public CurveMap? Written { get; set; }

        public float Value { get; set; }

        public bool Continues(IProperty<CurveMap> curve, float center)
            => ReferenceEquals(Curve, curve) && Center == center && Equals(curve.Value, Written);
    }
}
