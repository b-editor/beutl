using System;
using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Extensions.MotionTrack.Resources;
using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;

using OpenCvSharp;

namespace BEditor.Extensions.MotionTrack
{
    public sealed class Tracker : ObjectElement
    {
        public static readonly EditingProperty<ButtonComponent> CropProperty
            = EditingProperty.Register<ButtonComponent, Tracker>(
                nameof(Crop),
                EditingPropertyOptions<ButtonComponent>.Create(new ButtonComponentMetadata(Strings.Crop)).Serialize());

        public static readonly EditingProperty<ValueProperty> TargetIdProperty
            = EditingProperty.Register<ValueProperty, Tracker>(
                nameof(TargetId),
                EditingPropertyOptions<ValueProperty>.Create(new ValuePropertyMetadata(Strings.ID, 0, Min: 0)).Serialize());

        public static readonly EditingProperty<string?> FileProperty
            = EditingProperty.Register<string?, Tracker>(
                nameof(File),
                EditingPropertyOptions<string>.Create()!.Serialize());

        public static readonly EditingProperty<string?> RoiProperty
            = EditingProperty.Register<string?, Tracker>(
                nameof(Roi),
                EditingPropertyOptions<string>.Create()!.Serialize());

        private IDisposable? _disposable;
        private OpenCvSharp.Tracker? _tracker;
        private Rect _roi;
        private bool _isInitialized = false;
        private TrackingService _trackingService;

        public Tracker()
        {
            _trackingService = ServicesLocator.Current.Provider.GetRequiredService<TrackingService>();
        }

        public override string Name => Strings.Tracker;

        public ButtonComponent Crop => GetValue(CropProperty);

        public ValueProperty TargetId => GetValue(TargetIdProperty);

        public string? File
        {
            get => GetValue(FileProperty);
            set => SetValue(FileProperty, value);
        }

        public string? Roi
        {
            get => GetValue(RoiProperty);
            set => SetValue(RoiProperty, value);
        }

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

                            if (args.Type == ApplyType.Edit)
                            {
                                using var texImage = Image.Rect(_roi.Width, _roi.Height, 2, Colors.White);
                                using var texture = Graphics.Texture.FromImage(texImage);
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

        protected override void OnLoad()
        {
            base.OnLoad();
            _trackingService = ServicesLocator.Current.Provider.GetRequiredService<TrackingService>();
            _disposable = Crop.Subscribe(async _ =>
            {
                var scene = this.GetParent<Scene>();
                var dialog = ServicesLocator.Current.Provider.GetRequiredService<IFileDialogService>();
                if (scene != null)
                {
                    _isInitialized = false;
                    using var image = scene.Render(ApplyType.Image);
                    using var ch3 = image.Convert<BGR24>();

                    unsafe
                    {
                        fixed (void* ptr = ch3.Data)
                        {
                            using var mat = new Mat(image.Height, image.Width, MatType.CV_8UC3, (IntPtr)ptr, 0);

                            _roi = Cv2.SelectROI(mat);

                            _tracker?.Dispose();
                            _tracker = Create();
                            _tracker.Init(mat, _roi);

                            Cv2.DestroyAllWindows();
                        }
                    }

                    var record = new SaveFileRecord
                    {
                        Filters =
                        {
                            new("画像ファイル", new string[]
                            {
                                "png",
                                "jpg",
                                "jpeg",
                            })
                        },
                    };

                    if (await dialog.ShowSaveFileDialogAsync(record))
                    {
                        File = record.FileName;
                        Roi = FormattableString.Invariant($"{_roi.X},{_roi.Y},{_roi.Width},{_roi.Height}");
                        image.Save(File);
                    }

                    _isInitialized = true;
                }
            });

            if (System.IO.File.Exists(File) && File != null && Roi != null)
            {
                var arr = Roi.Split(',');
                var x = int.Parse(arr[0]);
                var y = int.Parse(arr[1]);
                var width = int.Parse(arr[2]);
                var height = int.Parse(arr[3]);
                _roi = new Rect(x, y, width, height);
                using var image = Image<BGR24>.FromFile(File);

                unsafe
                {
                    fixed (void* ptr = image.Data)
                    {
                        using var mat = new Mat(image.Height, image.Width, MatType.CV_8UC3, (IntPtr)ptr, 0);

                        _tracker = Create();
                        _tracker.Init(mat, _roi);
                        _isInitialized = true;
                    }
                }
            }
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

        private static OpenCvSharp.Tracker Create()
        {
            var plugin = PluginManager.Default.Get<Plugin>();

            if (plugin.Settings is CustomSettings settings)
            {
                return settings.Algorithm switch
                {
                    Algorithm.MIL => TrackerMIL.Create(),
                    Algorithm.KCF => OpenCvSharp.Tracking.TrackerKCF.Create(),
                    Algorithm.CSRT => OpenCvSharp.Tracking.TrackerCSRT.Create(),
                    _ => OpenCvSharp.Tracking.TrackerKCF.Create(),
                };
            }

            return OpenCvSharp.Tracking.TrackerKCF.Create();
        }
    }
}
