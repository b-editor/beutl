using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using Beutl.Controls;
using Beutl.Editor.Components.Converters;
using Beutl.Editor.Components.Helpers;
using Beutl.Models;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;

using Microsoft.Extensions.Logging;

using AvaPoint = Avalonia.Point;
using Matrix = Beutl.Graphics.Matrix;

namespace Beutl.Views;

public partial class PlayerView
{
    // Device pixels per frame pixel: the levels the zoom box offers, and the steps of the zoom commands.
    private static readonly double[] s_zoomLevels = [0.1, 0.25, 0.5, 1, 2, 4, 8];

    private static readonly PreviewZoomOption[] s_zoomOptions =
    [
        new(Strings.PreviewZoomFit, null),
        .. s_zoomLevels.Select(level => new PreviewZoomOption(FormatZoom(level), level)),
    ];

    // A level counts as reached within this ratio, so a step never lands on what rounds to the same percentage.
    private const double ZoomLevelTolerance = 0.005;

    // At a zoom the user chose, a frame pixel this many device pixels wide or more is drawn as a sharp square. Below it,
    // nearest sampling would draw the pixels at uneven sizes, one device pixel wide or two (#4082).
    private const double SharpPixelsMinScreenZoom = 2;

    // The range of FrameMatrix's scale, which keeps the frame findable and the numbers sane.
    private const float MinFrameMatrixScale = 0.05f;
    private const float MaxFrameMatrixScale = 64f;

    // A translucent gray, which shows against the panel in the dark and the light theme alike.
    private static readonly Color s_frameOutlineColor = Color.FromArgb(0x60, 0x80, 0x80, 0x80);

    public static readonly DirectProperty<PlayerView, string> ZoomLabelProperty =
        AvaloniaProperty.RegisterDirect<PlayerView, string>(nameof(ZoomLabel), view => view.ZoomLabel);

    // The name that fits the footer. The box's list and the settings tab spell the automatic scale out.
    public static readonly FuncValueConverter<RenderScale, string> ResolutionLabelConverter = new(GetResolutionLabel);

    private PlayerViewportTransform? _viewportTransform;
    private string _zoomLabel = "-";
    private bool _isSyncingZoomOption;
    private RenderScale? _requestedPreviewScale;

    public string ZoomLabel
    {
        get => _zoomLabel;
        private set => SetAndRaise(ZoomLabelProperty, ref _zoomLabel, value);
    }

    internal PlayerViewportTransform? ViewportTransform
    {
        get
        {
            if (DataContext is not PlayerViewModel { Scene: { } scene } || image is null)
                return _viewportTransform = null;

            if (_viewportTransform == null || _viewportTransform.PreviewSize != image.Bounds.Size || _viewportTransform.FrameSize != scene.FrameSize.Value)
                _viewportTransform = PlayerViewportTransform.Create(image.Bounds.Size, scene.FrameSize.Value);
            return _viewportTransform;
        }
    }

    // Device pixels per frame pixel on screen now, so 100% shows the frame pixel for pixel.
    internal double? ScreenZoom
    {
        get
        {
            if (DataContext is not PlayerViewModel viewModel || ViewportTransform is not { } viewport)
                return null;

            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            return viewport.FrameScale * viewModel.FrameMatrix.Value.M11 * scaling;
        }
    }

    private void UpdateViewportScale()
    {
        pathEditorView.ViewportScale = ViewportTransform?.FrameScale ?? 1;
        UpdateTransformHandles();
        UpdateZoomDisplay();
    }

    private void UpdateZoomDisplay()
    {
        double? zoom = ScreenZoom;
        ZoomLabel = zoom is { } value ? FormatZoom(value) : "-";

        bool isFit = DataContext is PlayerViewModel viewModel && viewModel.FrameMatrix.Value == Matrix.Identity;
        _isSyncingZoomOption = true;
        // No option matches a zoom between the levels, and the box then shows its placeholder: the same label.
        zoomComboBox.SelectedItem = isFit
            ? s_zoomOptions[0]
            : Array.Find(s_zoomOptions, option => option.Level is { } level && zoom is { } current
                && Math.Abs((current / level) - 1) < ZoomLevelTolerance);
        _isSyncingZoomOption = false;

        UpdatePreviewInterpolation(zoom);
    }

    // The frame stays smooth at fit, whatever its size, and at a zoom the user chose its pixels turn sharp once each is
    // SharpPixelsMinScreenZoom device pixels wide. Only the scale tells the two apart, so panning at fit keeps it smooth.
    private void UpdatePreviewInterpolation(double? zoom)
    {
        bool zoomed = DataContext is PlayerViewModel viewModel
                      && Math.Abs(viewModel.FrameMatrix.Value.M11 - 1) >= ZoomLevelTolerance;
        // A zoom the box counts as the level, which rounding can leave just under it, reaches it here too.
        BitmapInterpolationMode mode = zoomed && zoom >= SharpPixelsMinScreenZoom * (1 - ZoomLevelTolerance)
            ? BitmapInterpolationMode.None
            : BitmapInterpolationMode.HighQuality;
        if (image is BitmapView sdr)
            sdr.InterpolationMode = mode;
        if (hdrView is { } hdr)
            hdr.InterpolationMode = mode;
    }

    private static string FormatZoom(double zoom)
        => (zoom * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    // One screen pixel wide at any zoom, outside the image so it covers none of the frame. A shadow, because layout
    // rounding would move a margin or a border this thin to one side.
    private void UpdateFrameOutline(float zoom)
    {
        imageBackground.BoxShadow = new BoxShadows(new BoxShadow { Spread = 1.0 / zoom, Color = s_frameOutlineColor });
    }

    // Scales about a point of framePanel, which stays where it is on screen.
    private static void ZoomFrameAt(PlayerViewModel viewModel, float ratio, AvaPoint point)
    {
        Matrix current = viewModel.FrameMatrix.Value;
        ratio = Math.Clamp(current.M11 * ratio, MinFrameMatrixScale, MaxFrameMatrixScale) / current.M11;
        float x = (float)point.X, y = (float)point.Y;
        viewModel.FrameMatrix.Value = new Matrix(ratio, 0, 0, ratio, x - ratio * x, y - ratio * y) * current;
    }

    // Scales about what is in the middle of the panel now.
    private void ZoomFrameAtCenter(PlayerViewModel viewModel, float ratio)
    {
        Matrix current = viewModel.FrameMatrix.Value;
        var center = new AvaPoint(
            ((framePanel.Bounds.Width / 2) - current.M31) / current.M11,
            ((framePanel.Bounds.Height / 2) - current.M32) / current.M22);
        ZoomFrameAt(viewModel, ratio, center);
    }

    private void SetScreenZoom(PlayerViewModel viewModel, double zoom)
    {
        if (ScreenZoom is { } current)
            ZoomFrameAtCenter(viewModel, (float)(zoom / current));
    }

    private void OnPreviewZoomRequested(PlayerViewModel viewModel, PreviewZoomAction action)
    {
        if (action == PreviewZoomAction.Fit)
        {
            viewModel.FrameMatrix.Value = Matrix.Identity;
        }
        else if (ScreenZoom is { } zoom)
        {
            double target = action == PreviewZoomAction.ZoomIn
                ? s_zoomLevels.FirstOrDefault(level => level > zoom * (1 + ZoomLevelTolerance))
                : s_zoomLevels.LastOrDefault(level => level < zoom * (1 - ZoomLevelTolerance));
            if (target > 0)
                SetScreenZoom(viewModel, target);
        }
    }

    private void OnZoomSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingZoomOption
            || DataContext is not PlayerViewModel viewModel
            || zoomComboBox.SelectedItem is not PreviewZoomOption option)
        {
            return;
        }

        if (option.Level is { } level)
            SetScreenZoom(viewModel, level);
        else
            OnPreviewZoomRequested(viewModel, PreviewZoomAction.Fit);
    }

    // The list keeps its items between openings, and a shortcut can change in the key map meanwhile.
    private void OnZoomDropDownOpened(object? sender, EventArgs e)
        => s_zoomOptions[0].RefreshShortcut();

    private async void OnResolutionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is PlayerViewModel viewModel && resolutionComboBox.SelectedItem is RenderScale scale)
            await ChangePreviewScaleAsync(viewModel, scale);
    }

    internal async Task ChangePreviewScaleAsync(PlayerViewModel viewModel, RenderScale scale)
    {
        EditViewModel editor = viewModel.EditViewModel;
        // A choice still waiting for the pause gives way to this one.
        _requestedPreviewScale = scale;
        // Choosing the quality in use changes nothing, so it must not stop playback either.
        if (scale == editor.PreviewScale.Value)
            return;

        try
        {
            // Rebuilding the renderer must not happen mid-playback.
            await viewModel.Pause();
            if (ReferenceEquals(DataContext, viewModel) && _requestedPreviewScale == scale)
                editor.PreviewScale.Value = scale;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change the preview render scale to {Scale}.", scale);
            // The box goes back to the quality in use, unless a newer choice took over. Going back starts no pause.
            if (ReferenceEquals(DataContext, viewModel) && _requestedPreviewScale == scale)
                resolutionComboBox.SelectedItem = editor.PreviewScale.Value;
            NotificationService.ShowError(MessageStrings.UnexpectedError, MessageStrings.OperationFailed);
        }
    }

    private static string GetResolutionLabel(RenderScale scale)
        => scale == RenderScale.FitToPreviewer ? Strings.RenderScale_FitToPreviewer_Short : RenderScaleNameConverter.GetName(scale);

    internal static KeyGesture? GetPreviewCommandGesture(string commandId)
        => GetCommandGestureExtension.GetKeyGesture(typeof(SceneEditorExtension), commandId);

    private void OnPlayerTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        ShowShortcutInToolTip(e.NameScope.Find<Control>("PART_StartButton"), "editor.seekStart");
        ShowShortcutInToolTip(e.NameScope.Find<Control>("PART_PreviousButton"), "editor.previous");
        ShowShortcutInToolTip(e.NameScope.Find<Control>("PART_PlayButton"), "editor.playPause");
        ShowShortcutInToolTip(e.NameScope.Find<Control>("PART_NextButton"), "editor.next");
        ShowShortcutInToolTip(e.NameScope.Find<Control>("PART_EndButton"), "editor.seekEnd");
        ShowShortcutInToolTip(e.NameScope.Find<Control>("PART_LoopButton"), "editor.toggleLoop");
        ShowShortcutInToolTip(e.NameScope.Find<Control>("PART_CurrentTimeTextBlock"), "editor.gotoTimecode");
    }

    // Written as the tip opens, because a shortcut can change in the key map while the view lives.
    private static void ShowShortcutInToolTip(Control? control, string commandId)
    {
        if (control is null || ToolTip.GetTip(control) is not string name)
            return;

        ToolTip.AddToolTipOpeningHandler(control, (_, _) => ToolTip.SetTip(control, FormatToolTip(name, commandId)));
    }

    private static string FormatToolTip(string name, string commandId)
        => GetPreviewCommandGesture(commandId) is { } gesture ? $"{name} ({gesture.ToString("p", null)})" : name;
}
