using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Beutl.Graphics.Transformation;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class TransformListItemEditor : UserControl, IListItemEditor
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(167));
    private CancellationTokenSource? _lastTransitionCts;

    public TransformListItemEditor()
    {
        Resources["TransformTypeToIconConverter"] = TransformTypeToIconConverter.Instance;
        InitializeComponent();
        reorderHandle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    await s_transition.Start(null, content, localToken);
                }
                else
                {
                    await s_transition.Start(content, null, localToken);
                }
            });
    }

    public Control? ReorderHandle =>
        (DataContext as TransformEditorViewModel)?.IsPresenter.Value == true
            ? presenterEditor.ReorderHandle
            : reorderHandle;

    public event EventHandler? DeleteRequested;

    private void DeleteClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void SelectTarget_Requested(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TransformEditorViewModel { IsDisposed: false } vm) return;

        var targets = vm.GetAvailableTargets();
        var pickerVm = new TargetPickerFlyoutViewModel();
        pickerVm.Initialize(targets);

        var flyout = new TargetPickerFlyout(pickerVm);
        flyout.ShowAt(this);

        var tcs = new TaskCompletionSource<Transform?>();
        flyout.Dismissed += (_, _) => tcs.TrySetResult(null);
        flyout.Confirmed += (_, _) => tcs.TrySetResult(
            (pickerVm.SelectedItem.Value?.UserData as TargetObjectInfo)?.Object as Transform);

        var result = await tcs.Task;
        if (result != null)
        {
            vm.SetTarget(result);
        }
    }

    private sealed class TransformTypeToIconConverter : IValueConverter
    {
        public static readonly TransformTypeToIconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value switch
            {
                KnownTransformType.Translate => Application.Current!.FindResource("TranslateTransformIconData"),
                KnownTransformType.Rotation => Application.Current!.FindResource("RotationTransformIconData"),
                KnownTransformType.Scale => Application.Current!.FindResource("ScaleTransformIconData"),
                KnownTransformType.Skew => Application.Current!.FindResource("SkewTransformIconData"),
                KnownTransformType.Rotation3D => Application.Current!.FindResource("Rotation3DTransformIconData"),
                _ => Application.Current!.FindResource("TransformListItemEditor_ReOrderIcon")
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
