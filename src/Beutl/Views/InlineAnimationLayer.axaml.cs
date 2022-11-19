using System.Numerics;
using System.Reflection;

using Avalonia;
using Avalonia.Layout;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.ViewModels;
using Beutl.Views.AnimationVisualizer;

namespace Beutl.Views;

public partial class InlineAnimationLayer : UserControl
{
    private readonly CrossFade _transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _lastTransitionCts;
    private IDisposable? _disposable1;
    private IDisposable? _disposable2;
    private Control? _simpleView;

    public InlineAnimationLayer()
    {
        InitializeComponent();
        this.SubscribeDataContextChange<InlineAnimationLayerViewModel>(
            OnDataContextAttached,
            OnDataContextDetached);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _lastTransitionCts?.Cancel();
    }

    private void OnDataContextDetached(InlineAnimationLayerViewModel obj)
    {
        obj.AnimationRequested = null;
        _disposable1?.Dispose();
        _disposable1 = null;
        _disposable2?.Dispose();
        _disposable2 = null;
        _simpleView = null;
    }

    private void OnDataContextAttached(InlineAnimationLayerViewModel obj)
    {
        _disposable1 = obj.IsExpanded.Subscribe(OnIsExpandedChanged);

        obj.AnimationRequested = async (margin, token) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var animation = new Avalonia.Animation.Animation
                {
                    Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
                    Duration = TimeSpan.FromSeconds(0.67),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame()
                        {
                            Cue = new Cue(0),
                            Setters =
                            {
                                new Setter(MarginProperty, Margin)
                            }
                        },
                        new KeyFrame()
                        {
                            Cue = new Cue(1),
                            Setters =
                            {
                                new Setter(MarginProperty, margin)
                            }
                        }
                    }
                };

                await animation.RunAsync(this, null, token);
            });
        };
    }

    private void OnIsExpandedChanged(bool obj)
    {
        if (DataContext is InlineAnimationLayerViewModel viewModel)
        {
            if (obj)
            {
                _simpleView ??= CreateSimpleView(viewModel);

                UpdateContentWithTransition(viewModel, _simpleView, Helper.LayerHeight * 2);
            }
            else
            {
                _simpleView ??= CreateSimpleView(viewModel);

                UpdateContentWithTransition(viewModel, _simpleView, Helper.LayerHeight);
            }
        }
    }

    private async void UpdateContentWithTransition(InlineAnimationLayerViewModel viewModel, object? content, double height)
    {
        _lastTransitionCts?.Cancel();
        _lastTransitionCts = new CancellationTokenSource();
        CancellationToken localToken = _lastTransitionCts.Token;

        presenter.Content = content;

        viewModel.Height = height;
        await _transition.Start(null, this, localToken);
    }

    private Control CreateSimpleView(InlineAnimationLayerViewModel viewModel)
    {
        Control control = CreateSimpleViewCore(viewModel);
        _disposable2?.Dispose();
        _disposable2 = control.Bind(WidthProperty, viewModel.Width);

        control.Margin = new Thickness(0, 1);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        return control;
    }

    private static Control CreateSimpleViewCore(InlineAnimationLayerViewModel viewModel)
    {
        Animation.IAnimation animation = viewModel.Property.Animation;

        if (animation is Animation.Animation<Media.Color> colorAnm)
        {
            return new ColorAnimationVisualizer(colorAnm);
        }

        Type type = animation.GetType().GetGenericArguments()[0];
        Type numberType = typeof(INumber<>);
        Type minMaxValueType = typeof(IMinMaxValue<>);
        Type binaryIntegerType = typeof(IBinaryInteger<>);
        Type floatingPointType = typeof(IFloatingPoint<>);

        if (IsAssignableToGenericType(type, numberType)
            && IsAssignableToGenericType(type, minMaxValueType))
        {
            if (IsAssignableToGenericType(type, binaryIntegerType))
            {
                return (Control)Activator.CreateInstance(typeof(IntegerAnimationVisualizer<>).MakeGenericType(type), animation)!;
            }
            else if (IsAssignableToGenericType(type, floatingPointType))
            {
                return (Control)Activator.CreateInstance(typeof(FloatingPointAnimationVisualizer<>).MakeGenericType(type), animation)!;
            }
        }

        return (Control)Activator.CreateInstance(typeof(EasingFunctionVisualizer<>).MakeGenericType(type), animation)!;
    }

    private static bool IsAssignableToGenericType(Type givenType, Type genericType)
    {
        foreach (Type it in givenType.GetInterfaces())
        {
            if (it.IsGenericType && it.GetGenericTypeDefinition() == genericType)
                return true;
        }

        if (givenType.IsGenericType && givenType.GetGenericTypeDefinition() == genericType)
            return true;

        Type? baseType = givenType.BaseType;
        if (baseType == null)
            return false;

        return IsAssignableToGenericType(baseType, genericType);
    }
}
