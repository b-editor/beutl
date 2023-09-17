using Avalonia.Controls;
using Avalonia.Input;

using Beutl.Animation;
using Beutl.Commands;
using Beutl.Controls;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.ViewModels;

using AvaImage = Avalonia.Controls.Image;
using AvaPoint = Avalonia.Point;

namespace Beutl.Views;

static file class CommandHelper
{
    public static IRecordableCommand? Compose(IRecordableCommand? first, IRecordableCommand? second)
    {
        if (second != null)
        {
            if (first != null)
            {
                return first.Append(second);
            }
            else
            {
                return second;
            }
        }
        else
        {
            return first;
        }
    }
}

public partial class EditView
{
    private static double Length(AvaPoint point)
    {
        return Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
    }

    private sealed class KeyFrameState
    {
        public KeyFrameState(KeyFrame<float>? previous, KeyFrame<float>? next)
        {
            Previous = previous;
            Next = next;
            OldPreviousValue = previous?.Value ?? 0;
            OldNextValue = next?.Value ?? 0;
        }

        public KeyFrame<float>? Previous { get; }

        public KeyFrame<float>? Next { get; }

        public float OldPreviousValue { get; }

        public float OldNextValue { get; }

        public IRecordableCommand? CreateCommand()
        {
            return CommandHelper.Compose(
                Previous != null && Previous.Value != OldPreviousValue
                    ? new ChangePropertyCommand<float>(Previous, KeyFrame<float>.ValueProperty, Previous.Value, OldPreviousValue)
                    : null,
                Next != null && Next.Value != OldNextValue
                    ? new ChangePropertyCommand<float>(Next, KeyFrame<float>.ValueProperty, Next.Value, OldNextValue)
                    : null);
        }
    }

    private interface IMouseControlHandler
    {
        void OnMoved(PointerEventArgs e);

        void OnPressed(PointerPressedEventArgs e);

        void OnReleased(PointerReleasedEventArgs e);

        void OnWheelChanged(PointerWheelEventArgs e)
        {
        }
    }

    private class MouseControlHand : IMouseControlHandler
    {
        private bool _pressed;
        private AvaPoint _position;

        public required Player Player { get; init; }

        public required AvaImage Image { get; init; }

        public required EditViewModel viewModel { get; init; }

        public void OnWheelChanged(PointerWheelEventArgs e)
        {
            const float ZoomSpeed = 1.2f;

            AvaPoint pos = e.GetPosition(Image);
            float x = (float)pos.X;
            float y = (float)pos.Y;
            float delta = (float)e.Delta.Y;
            float realDelta = MathF.Sign(delta) * MathF.Abs(delta);

            float ratio = MathF.Pow(ZoomSpeed, realDelta);

            var a = new Matrix(ratio, 0, 0, ratio, x - (ratio * x), y - (ratio * y));
            viewModel.Player.FrameMatrix.Value = a * viewModel.Player.FrameMatrix.Value;

            e.Handled = true;
        }

        public void OnMoved(PointerEventArgs e)
        {
            if (_pressed)
            {
                AvaPoint position = e.GetPosition(Player);
                AvaPoint delta = position - _position;
                viewModel.Player.FrameMatrix.Value *= Matrix.CreateTranslation((float)delta.X, (float)delta.Y);

                _position = position;

                e.Handled = true;
            }
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_pressed)
            {
                Player.GetFramePanel().Cursor = Cursors.Hand;
                _pressed = false;
            }
        }

        public void OnPressed(PointerPressedEventArgs e)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(Player);
            _pressed = pointerPoint.Properties.IsLeftButtonPressed;
            _position = pointerPoint.Position;
            if (_pressed)
            {
                Player.GetFramePanel().Cursor = Cursors.HandGrab;

                e.Handled = true;
            }
        }
    }

    private sealed class MouseControlMove : IMouseControlHandler
    {
        private bool _imagePressed;
        private AvaPoint _scaledStartPosition;
        private Element? _mouseSelectedElement;
        private Drawable? _mouseSelected;
        private TranslateTransform? _translateTransform;
        private Matrix _preMatrix = Matrix.Identity;
        private Point _oldTranslation;
        private KeyFrameState? _xKeyFrame;
        private KeyFrameState? _yKeyFrame;

        public required AvaImage Image { get; init; }

        public required EditViewModel viewModel { get; init; }

        public Drawable? Drawable => _mouseSelected;

        public Element? Element => _mouseSelectedElement;

        private static (TranslateTransform?, Matrix) FindOrCreateTranslation(Drawable drawable)
        {
            switch (drawable.Transform)
            {
                case TranslateTransform translateTransform:
                    return (translateTransform, Matrix.Identity);

                case TransformGroup transformGroup:
                    Transforms list = transformGroup.Children;
                    TranslateTransform? obj = null;
                    int i;
                    for (i = 0; i < list.Count; i++)
                    {
                        ITransform item = list[i];
                        if (item is TranslateTransform translate)
                        {
                            obj = translate;
                            break;
                        }
                    }

                    if (obj == null)
                    {
                        obj = new TranslateTransform();
                        transformGroup.Children.BeginRecord<ITransform>()
                            .Insert(0, obj)
                            .ToCommand()
                            .DoAndRecord(CommandRecorder.Default);

                        return (obj, Matrix.Identity);
                    }
                    else
                    {
                        Matrix matrix = Matrix.Identity;
                        for (int j = 0; j < i; j++)
                        {
                            ITransform item = list[j];
                            if (item.IsEnabled)
                                matrix = list[j].Value * matrix;
                        }

                        return (obj, matrix);
                    }
            }

            return (null, Matrix.Identity);
        }

        private KeyFrameState? FindKeyFramePairOrNull(CoreProperty<float> property)
        {
            int rate = viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
            TimeSpan globalkeyTime = viewModel.Scene.CurrentFrame;
            TimeSpan localKeyTime = _mouseSelectedElement != null ? globalkeyTime - _mouseSelectedElement.Start : globalkeyTime;

            if (_translateTransform!.Animations.FirstOrDefault(v => v.Property == property) is KeyFrameAnimation<float> animation)
            {
                TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;
                keyTime = keyTime.RoundToRate(rate);

                (IKeyFrame? prev, IKeyFrame? next) = animation.KeyFrames.GetPreviousAndNextKeyFrame(keyTime);

                if (next?.KeyTime == keyTime)
                    return new(next as KeyFrame<float>, null);

                return new(prev as KeyFrame<float>, next as KeyFrame<float>);
            }

            return default;
        }

        public void OnMoved(PointerEventArgs e)
        {
            if (_imagePressed && _mouseSelected != null)
            {
                if (!viewModel.Player.IsMoveMode.Value)
                    return;

                PointerPoint pointerPoint = e.GetCurrentPoint(Image);
                AvaPoint imagePosition = pointerPoint.Position;
                double scaleX = Image.Bounds.Size.Width / viewModel.Scene.Width;
                AvaPoint scaledPosition = imagePosition / scaleX;
                AvaPoint delta = scaledPosition - _scaledStartPosition;
                if (_translateTransform == null && Length(delta) >= 1)
                {
                    (_translateTransform, _preMatrix) = FindOrCreateTranslation(_mouseSelected);

                    // 最初の一回だけ、キーフレームを探す
                    if (_translateTransform != null)
                    {
                        _oldTranslation = new(_translateTransform.X, _translateTransform.Y);
                        _xKeyFrame = FindKeyFramePairOrNull(TranslateTransform.XProperty);
                        _yKeyFrame = FindKeyFramePairOrNull(TranslateTransform.YProperty);
                    }
                }
                if (_preMatrix.TryInvert(out Matrix inverted))
                {
                    Avalonia.Matrix avaInverted = inverted.ToAvaMatrix();
                    AvaPoint scaledPosition1 = scaledPosition * avaInverted;
                    AvaPoint scaledStartPosition1 = _scaledStartPosition * avaInverted;
                    delta = scaledPosition1 - scaledStartPosition1;
                }

                if (_translateTransform != null)
                {
                    if (!SetKeyFrameValue(_xKeyFrame, (float)delta.X))
                    {
                        _translateTransform.X += (float)delta.X;
                    }

                    if (!SetKeyFrameValue(_yKeyFrame, (float)delta.Y))
                    {
                        _translateTransform.Y += (float)delta.Y;
                    }
                }

                _scaledStartPosition = scaledPosition;
                e.Handled = true;
            }
        }

        // keyframesが両方nullの場合、falseを返す
        private static bool SetKeyFrameValue(KeyFrameState? keyframes, float delta)
        {
            switch ((keyframes?.Previous, keyframes?.Next))
            {
                case (null, null):
                    return false;

                case ({ } prev, { } next):
                    prev.Value += delta;
                    next.Value += delta;
                    break;

                case ({ } prev, null):
                    prev.Value += delta;
                    break;

                case (null, { } next):
                    next.Value += delta;
                    break;
            }

            return true;
        }

        private IRecordableCommand? CreateTranslationCommand()
        {
            if (_translateTransform != null)
            {
                return CommandHelper.Compose(
                    _translateTransform.X != _oldTranslation.X
                        ? new ChangePropertyCommand<float>(_translateTransform, TranslateTransform.XProperty, _translateTransform.X, _oldTranslation.X)
                        : null,
                    _translateTransform.Y != _oldTranslation.Y
                        ? new ChangePropertyCommand<float>(_translateTransform, TranslateTransform.YProperty, _translateTransform.Y, _oldTranslation.Y)
                        : null);
            }

            return null;
        }

        public void OnReleased(PointerReleasedEventArgs e)
        {
            if (_imagePressed)
            {
                _imagePressed = false;

                IRecordableCommand? command = CommandHelper.Compose(
                    CreateTranslationCommand(),
                    CommandHelper.Compose(_xKeyFrame?.CreateCommand(), _yKeyFrame?.CreateCommand()));
                command?.DoAndRecord(CommandRecorder.Default);

                _mouseSelectedElement = null;
                _translateTransform = null;
                _mouseSelected = null;
                _xKeyFrame = default;
                _yKeyFrame = default;
                e.Handled = true;
            }
        }

        public void OnPressed(PointerPressedEventArgs e)
        {
            PointerPoint pointerPoint = e.GetCurrentPoint(Image);
            _imagePressed = pointerPoint.Properties.IsLeftButtonPressed;
            AvaPoint imagePosition = pointerPoint.Position;
            double scaleX = Image.Bounds.Size.Width / viewModel.Scene.Width;
            _scaledStartPosition = imagePosition / scaleX;

            _mouseSelected = viewModel.Scene.Renderer.HitTest(new((float)_scaledStartPosition.X, (float)_scaledStartPosition.Y));

            if (_mouseSelected != null)
            {
                int zindex = (_mouseSelected as DrawableDecorator)?.OriginalZIndex ?? _mouseSelected.ZIndex;
                Scene scene = viewModel.Scene;

                _mouseSelectedElement = scene.Children.FirstOrDefault(v =>
                    v.ZIndex == zindex
                    && v.Start <= scene.CurrentFrame
                    && scene.CurrentFrame < v.Range.End);

                if (_mouseSelectedElement != null)
                {
                    viewModel.SelectedObject.Value = _mouseSelectedElement;
                }
            }

            e.Handled = _imagePressed;
        }
    }

    private readonly WeakReference<Drawable?> _lastSelected = new(null);
    private IMouseControlHandler? _mouseState;

    private void OnFramePointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is EditViewModel viewModel)
        {
            CreateMouseHandler(viewModel).OnWheelChanged(e);
        }
    }

    private void OnFramePointerMoved(object? sender, PointerEventArgs e)
    {
        _mouseState?.OnMoved(e);
    }

    private void OnFramePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mouseState?.OnReleased(e);
        _mouseState = null;
    }

    private IMouseControlHandler CreateMouseHandler(EditViewModel viewModel)
    {
        if (viewModel.Player.IsMoveMode.Value)
        {
            return new MouseControlMove
            {
                Image = Image,
                viewModel = viewModel
            };
        }
        else
        {
            return new MouseControlHand
            {
                Player = Player,
                Image = Image,
                viewModel = viewModel
            };
        }
    }

    private void OnFramePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is EditViewModel viewModel)
        {
            _mouseState = CreateMouseHandler(viewModel);

            _mouseState.OnPressed(e);
            // Todo: 抽象化する
            if (_mouseState is MouseControlMove move)
            {
                _lastSelected.SetTarget(move.Drawable);
            }
        }
    }
}
