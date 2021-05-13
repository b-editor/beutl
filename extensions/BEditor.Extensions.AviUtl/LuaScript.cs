using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;
using BEditor.Primitive.Objects;

using Neo.IronLua;

using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using BEditor.Graphics;

namespace BEditor.Extensions.AviUtl
{
    public sealed class LuaScript : ImageEffect
    {
        public static readonly DirectEditingProperty<LuaScript, DocumentProperty> CodeProperty = EditingProperty.RegisterSerializeDirect<DocumentProperty, LuaScript>(
            nameof(Code),
            owner => owner.Code,
            (owner, obj) => owner.Code = obj,
            new DocumentPropertyMetadata(string.Empty));

        internal static readonly Lua LuaEngine = new();

        internal static readonly LuaGlobal LuaGlobal = LuaEngine.CreateEnvironment();

        static LuaScript()
        {
            //LuaGlobal.SetValue("obj", ObjectTable);
        }

        public override string Name => "スクリプト制御";

        [AllowNull]
        public DocumentProperty Code { get; private set; }

        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            if (Parent.Effect[0] is ImageObject obj)
            {
                var table = new ObjectTable(args, obj);
                LuaGlobal.SetValue("obj", table);
            }
            Parent.Parent.GraphicsContext!.MakeCurrent();
            Parent.Parent.GraphicsContext.Framebuffer.Bind();
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Code;
        }
    }

    public class ObjectTable
    {
        internal static GraphicsContext? _sharedGraphics;
        private readonly ImageObject _imageobj;
        private readonly Frame _frame;
        private readonly Image<BGRA32> _img;
        private readonly Project _proj;
        private readonly Scene _scene;
        // クリップからの現在のフレーム
        private readonly Frame rframe;

        public ObjectTable(EffectApplyArgs<Image<BGRA32>> args, ImageObject image)
        {
            _frame = args.Frame;
            _img = args.Value;
            _imageobj = image;
            _scene = image.Parent.Parent;
            _proj = image.Parent.Parent.Parent;
            rframe = args.Frame - image.Parent.Start;

            if(_sharedGraphics is null)
            {
                _sharedGraphics = new(args.Value.Width, args.Value.Height);
            }
            else
            {
                _sharedGraphics.SetSize(args.Value.Size);
            }
        }

#pragma warning disable IDE1006, CA1822

        #region Properties
        public float ox
        {
            get => _imageobj.Coordinate.CenterX.Optional;
            set => _imageobj.Coordinate.CenterX.Optional = value;
        }

        public float oy
        {
            get => _imageobj.Coordinate.CenterY.Optional;
            set => _imageobj.Coordinate.CenterY.Optional = value;
        }

        public float oz
        {
            get => _imageobj.Coordinate.CenterZ.Optional;
            set => _imageobj.Coordinate.CenterZ.Optional = value;
        }

        public float rx
        {
            get => _imageobj.Rotate.RotateX[_frame];
            set => _imageobj.Rotate.RotateX.Optional = value - rx;
        }

        public float ry
        {
            get => _imageobj.Rotate.RotateY[_frame];
            set => _imageobj.Rotate.RotateY.Optional = value - ry;
        }

        public float rz
        {
            get => _imageobj.Rotate.RotateZ[_frame];
            set => _imageobj.Rotate.RotateZ.Optional = value - rz;
        }

        public float cx
        {
            get => _imageobj.Coordinate.CenterX[_frame];
            set => _imageobj.Coordinate.CenterX.Optional = value - cx;
        }

        public float cy
        {
            get => _imageobj.Coordinate.CenterY[_frame];
            set => _imageobj.Coordinate.CenterY.Optional = value - cy;
        }

        public float cz
        {
            get => _imageobj.Coordinate.CenterZ[_frame];
            set => _imageobj.Coordinate.CenterZ.Optional = value - cz;
        }

        public float zoom
        {
            get => _imageobj.Scale.Scale1[_frame] / 100;
            set => _imageobj.Scale.Scale1.Optional = (value - zoom) * 100;
        }

        public float alpha
        {
            get => _imageobj.Blend.Opacity[_frame] / 100;
            set => _imageobj.Blend.Opacity.Optional = (value - alpha) * 100;
        }

        public float aspect
        {
            get => ToAspect(zoom_w, zoom_h);
            set
            {
                var size = ToSize(MathF.Max(zoom_w, zoom_h), value);
                zoom_w = size.Width;
                zoom_h = size.Height;
            }
        }

        public float zoom_w
        {
            get => _imageobj.Scale.ScaleX[_frame];
            set => _imageobj.Scale.ScaleX.Optional = value - zoom_w;
        }

        public float zoom_h
        {
            get => _imageobj.Scale.ScaleY[_frame];
            set => _imageobj.Scale.ScaleY.Optional = value - zoom_h;
        }

        public float x => _imageobj.Coordinate.X[_frame];

        public float y => _imageobj.Coordinate.Y[_frame];

        public float z => _imageobj.Coordinate.Z[_frame];

        public int w => _img.Width;

        public int h => _img.Height;

        public int screen_w => _scene.Width;

        public int screen_h => _scene.Height;

        public int framerate => _proj.Framerate;

        public int frame => rframe;

        public double time => rframe.ToSeconds(framerate);

        public int totalframe => _imageobj.Parent.Length;

        public double totaltime => _imageobj.Parent.Length.ToSeconds(framerate);

        public int layer => _imageobj.Parent.Layer;

        public int index => 0;

        public int num => 1;
        #endregion

        #region Methods
        public void mes(string text)
        {
            _imageobj.ServiceProvider?.GetService<IMessage>()?.Snackbar(text);
        }

        public void draw(float ox = 0, float oy = 0, float oz = 0, float zoom = 0, float alpha = 0, float rx = 0, float ry = 0, float rz = 0)
        {

        }
        #endregion

#pragma warning restore IDE1006, CA1822

        internal static Size ToSize(float size, float aspect)
        {
            Size result;

            if (aspect > 0)
            {
                result = new((int)(size - (aspect * size / 100f)), (int)size);
            }
            else
            {
                result = new((int)size, (int)(size + (aspect * size / 100)));
            }

            return result;
        }

        internal static float ToAspect(float width, float height)
        {
            var max = MathF.Max(width, height);
            var min = MathF.Min(width, height);

            return (1 - (1 / max) * min) * -100;
        }
    }
}