using System;
using System.Linq;
using System.Numerics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Graphics;
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
        private int _zIndex;
        private KeyFramePair<float> _xPair;
        private KeyFramePair<float> _yPair;
        private KeyFramePair<float> _zPair;
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
            if (_isMouseDown && _clip != null && _scene?.GraphicsContext != null)
            {
                var point = e.GetPosition(_image);

                // à⁄ìÆó 
                var move = point - _startPoint;
                var items = _clip.Effect[0].GetAllChildren<Coordinate>();
                var xRatio = _scene.Width / _image.Bounds.Width;
                var yRatio = _scene.Height / _image.Bounds.Height;
                move = move.WithX(move.X * xRatio).WithY(move.Y * yRatio);

                if (items.Any())
                {
                    var item = items.First();
                    var xP = item.X.Pairs[_xIndex];
                    var yP = item.Y.Pairs[_yIndex];
                    var zP = item.Z.Pairs[_zIndex];

                    // à⁄ìÆó ÅiÉJÉÅÉâÇ…çáÇÌÇπÇΩÅj
                    var vector = GetTransform(move, _scene.GraphicsContext);

                    item.X.Pairs[_xIndex] = xP.WithValue(xP.Value + vector.X);
                    item.Y.Pairs[_yIndex] = yP.WithValue(yP.Value - vector.Y);
                    item.Z.Pairs[_zIndex] = zP.WithValue(zP.Value - vector.Z);
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
                    var zTmp = item.Z.Pairs[_zIndex];
                    item.X.Pairs[_xIndex] = _xPair;
                    item.Y.Pairs[_yIndex] = _yPair;
                    item.Z.Pairs[_zIndex] = _zPair;

                    item.X.ChangeValue(_xIndex, xTmp.Value)
                        .Combine(item.Y.ChangeValue(_yIndex, yTmp.Value))
                        .Combine(item.Z.ChangeValue(_zIndex, zTmp.Value))
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
                    _zIndex = FindIndex(item.Z, _scene.PreviewFrame);
                    _xPair = item.X.Pairs[_xIndex];
                    _yPair = item.Y.Pairs[_yIndex];
                    _zPair = item.Z.Pairs[_zIndex];
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

        private static Vector3 GetTransform(Point point, GraphicsContext context)
        {
            var view = context.Camera.GetViewMatrix();
            var model = Matrix4x4.CreateTranslation(new((float)point.X, (float)point.Y, 0));

            var mat = (model * view) - view;
            return mat.Translation;
        }
    }
}