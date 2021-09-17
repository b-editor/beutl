using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Media;
using BEditor.Models;
using BEditor.ViewModels;

namespace BEditor.Views
{
    public sealed class Previewer : UserControl
    {
        private readonly Image _image;
        private bool _isMouseDown;
        private Point _startPoint;
        private int _xIndex;
        private int _yIndex;
        private KeyFramePair<float> _xPair;
        private KeyFramePair<float> _yPair;
        private Scene? _scene;
        private ClipElement? _clip;

        public Previewer()
        {
            InitializeComponent();
            _image = this.FindControl<Image>("image");

            _image.PointerPressed += Image_PointerPressed;
            _image.PointerReleased += Image_PointerReleased;
            _image.PointerLeave += Image_PointerLeave;
            _image.PointerMoved += Image_PointerMoved;
        }

        private void Image_PointerLeave(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            _isMouseDown = false;
        }

        private void Image_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (_isMouseDown && _clip != null && _scene != null)
            {
                var point = e.GetPosition(_image);

                // ˆÚ“®—Ê
                var move = (point - _startPoint) * 2;
                var items = _clip.Effect[0].GetAllChildren<Coordinate>();

                if (items.Any())
                {
                    var item = items.First();
                    var xP = item.X.Pairs[_xIndex];
                    var yP = item.Y.Pairs[_yIndex];

                    item.X.Pairs[_xIndex] = xP.WithValue(xP.Value + (float)move.X);
                    item.Y.Pairs[_yIndex] = yP.WithValue(yP.Value - (float)move.Y);
                }

                _startPoint = point;
            }
        }

        private void Image_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            _isMouseDown = false;

            if (_scene != null && _clip != null)
            {
                var items = _clip.Effect[0].GetAllChildren<Coordinate>();

                if (items.Any())
                {
                    var item = items.First();
                    var xTmp = item.X.Pairs[_xIndex];
                    var yTmp = item.Y.Pairs[_yIndex];
                    item.X.Pairs[_xIndex] = _xPair;
                    item.Y.Pairs[_yIndex] = _yPair;

                    item.X.ChangeValue(_xIndex, xTmp.Value)
                        .Combine(item.Y.ChangeValue(_yIndex, yTmp.Value))
                        .Execute();
                }
            }
        }

        private void Image_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            _scene = AppModel.Current.Project?.CurrentScene;
            _clip = _scene?.SelectItem;

            _isMouseDown = true;
            _startPoint = e.GetPosition(_image);

            if (_scene != null && _clip != null)
            {
                var items = _clip.Effect[0].GetAllChildren<Coordinate>();

                if (items.Any())
                {
                    var item = items.First();
                    _xIndex = FindIndex(item.X, _scene.PreviewFrame);
                    _yIndex = FindIndex(item.Y, _scene.PreviewFrame);
                    _xPair = item.X.Pairs[_xIndex];
                    _yPair = item.Y.Pairs[_yIndex];
                }
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is PreviewerViewModel viewModel)
            {
                viewModel.ImageChanged += ViewModel_ImageChanged;
            }
        }

        private void ViewModel_ImageChanged(object? sender, EventArgs e)
        {
            _image.InvalidateVisual();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private static int FindIndex(EaseProperty property, Frame frame)
        {
            frame -= property.Parent.Parent.Start;
            var length = property.Parent.Parent.Length.Value;
            var time = frame / (float)length;
            if (time >= 0 && time <= property.Pairs[1].Position.GetPercentagePosition(length))
            {
                return 0;
            }
            else if (property.Pairs[^2].Position.GetPercentagePosition(length) <= time && time <= 1)
            {
                return property.Pairs.Count - 2;
            }
            else
            {
                var index = 0;
                for (var f = 0; f < property.Pairs.Count - 1; f++)
                {
                    if (property.Pairs[f].Position.GetPercentagePosition(length) <= time && time <= property.Pairs[f + 1].Position.GetPercentagePosition(length))
                    {
                        index = f;
                    }
                }

                return index;
            }

            throw new Exception();
        }
    }
}