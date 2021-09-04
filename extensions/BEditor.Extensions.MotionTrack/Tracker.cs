using System;
using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using Microsoft.Extensions.DependencyInjection;

using OpenCvSharp;

namespace BEditor.Extensions.MotionTrack
{
    public sealed class Tracker : ObjectElement
    {
        public static readonly EditingProperty<ButtonComponent> CropProperty
            = EditingProperty.Register<ButtonComponent, Tracker>(
                nameof(Crop),
                EditingPropertyOptions<ButtonComponent>.Create(new ButtonComponentMetadata("範囲を選択")).Serialize());

        public static readonly EditingProperty<ValueProperty> TargetIdProperty
            = EditingProperty.Register<ValueProperty, Tracker>(
                nameof(TargetId),
                EditingPropertyOptions<ValueProperty>.Create(new ValuePropertyMetadata("ID", 0, Min: 0)).Serialize());

        private IDisposable? _disposable;

        private OpenCvSharp.Tracker? _tracker;

        private Rect _roi;

        private bool _isInitialized = false;

        private TrackingService _trackingService;

        public Tracker()
        {
            _trackingService = ServicesLocator.Current.Provider.GetRequiredService<TrackingService>();
        }

        public override string Name => "トラッカー";

        public ButtonComponent Crop => GetValue(CropProperty);

        public ValueProperty TargetId => GetValue(TargetIdProperty);

        public override unsafe void Apply(EffectApplyArgs args)
        {
            if (args.Type == ApplyType.Audio) return;

            var scene = this.GetParent<Scene>();
            if (_tracker != null && _isInitialized && scene != null && scene.GraphicsContext != null)
            {
                lock (this)
                {
                    using var image = new Image<BGRA32>(scene.Width, scene.Height);
                    scene.GraphicsContext.ReadImage(image);
                    using var ch3 = image.Convert<BGR24>();

                    fixed (void* ptr = ch3.Data)
                    {
                        using var mat = new Mat(image.Height, image.Width, MatType.CV_8UC3, (IntPtr)ptr, 0);

                        if (_tracker.Update(mat, ref _roi))
                        {
                            _trackingService[(int)TargetId.Value] = new Rectangle(_roi.X, _roi.Y, _roi.Width, _roi.Height);

                            if(args.Type== ApplyType.Edit)
                            {
                                using var texImage = Image.Rect(_roi.Width, _roi.Height, 2, Colors.White);
                                using var texture = Graphics.Texture.FromImage(image);
                                var pos = texture.Transform.Position;
                                pos.X = _roi.X + _roi.Width / 2 - scene.Width / 2;
                                pos.Y = -(_roi.Y + _roi.Height / 2 - scene.Height / 2);
                                var transform = texture.Transform;
                                transform.Position = pos;
                                texture.Transform = transform;

                                scene.GraphicsContext.DrawTexture(texture);
                            }
                        }
                    }
                }
            }
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Crop;
            yield return TargetId;
        }

        protected override unsafe void OnLoad()
        {
            base.OnLoad();
            _tracker = OpenCvSharp.Tracking.TrackerKCF.Create();
            _trackingService = ServicesLocator.Current.Provider.GetRequiredService<TrackingService>();
            _disposable = Crop.Subscribe(_ =>
            {
                var scene = this.GetParent<Scene>();
                if (scene != null)
                {
                    _isInitialized = false;
                    using var image = scene.Render(ApplyType.Image);
                    fixed (void* ptr = image.Data)
                    {
                        using var mat = new Mat(image.Height, image.Width, MatType.CV_8UC4, (IntPtr)ptr, 0);

                        _roi = Cv2.SelectROI(mat);
                        _tracker.Init(mat, _roi);
                        _isInitialized = true;
                    }
                }
            });
        }

        protected override void OnUnload()
        {
            base.OnUnload();
            _tracker?.Dispose();
            _tracker = null;
            _disposable?.Dispose();
            _disposable = null;
            _isInitialized = false;
        }
    }
}
