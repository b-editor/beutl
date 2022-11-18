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
        if (obj)
        {
            transitioning.Content = null;
        }
        else if (DataContext is InlineAnimationLayerViewModel viewModel)
        {
            _simpleView ??= CreateSimpleView(viewModel);

            transitioning.Content = _simpleView;
        }
    }

    private Control CreateSimpleView(InlineAnimationLayerViewModel viewModel)
    {
        var control = CreateSimpleViewCore(viewModel);
        _disposable2?.Dispose();
        _disposable2 = control.Bind(WidthProperty, viewModel.Width);

        control.Margin = new Thickness(0, 2);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        return control;
    }

    private Control CreateSimpleViewCore(InlineAnimationLayerViewModel viewModel)
    {
        var animation = viewModel.Property.Animation;

        if (animation is Animation.Animation<Media.Color> colorAnm)
        {
            return new ColorAnimationVisualizer(colorAnm);
        }

        var type = animation.GetType().GetGenericArguments()[0];
        var numberType = typeof(INumber<>);
        var minMaxValueType = typeof(IMinMaxValue<>);
        var binaryIntegerType = typeof(IBinaryInteger<>);
        var floatingPointType = typeof(IFloatingPoint<>);

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
        var interfaceTypes = givenType.GetInterfaces();

        foreach (var it in interfaceTypes)
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
