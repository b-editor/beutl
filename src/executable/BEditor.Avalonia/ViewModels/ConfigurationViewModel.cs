using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Media;

using BEditor.Data;
using BEditor.Drawing.Pixel;
using BEditor.Media;
using BEditor.Models;
using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public sealed class ConfigurationViewModel
    {
        private readonly Project _project;

        public ConfigurationViewModel(Project project)
        {
            _project = project;
            SelectedBackground = project.GetObservable(ProjectConfig.BackgroundTypeProperty)
                .Select(i => ToStr(i))
                .ToReactiveProperty()!;

            SelectedBackground.Value = ToStr(ProjectConfig.GetBackgroundType(project));
            SelectedBackground.Subscribe(i => ProjectConfig.SetBackgroundType(_project, ToEnum(i)));

            Speed = project.GetObservable(ProjectConfig.SpeedProperty)
                .Select(i => i * 100)
                .ToReactiveProperty();

            Speed.Value = ProjectConfig.GetSpeed(project) * 100;
            Speed.Subscribe(i => ProjectConfig.SetSpeed(_project, i / 100));

            _project.GetObservable(Project.CurrentSceneProperty).Subscribe(scene =>
            {
                if (scene.Cache != null)
                {
                    Start.Value = scene.Cache.Start.ToTimeSpan(_project.Framerate);
                    End.Value = scene.Cache.Length.ToTimeSpan(_project.Framerate);
                }
                else
                {
                    if (Start.Value >= scene.TotalFrame.ToTimeSpan(_project.Framerate))
                    {
                        Start.Value = default;
                    }

                    var total = scene.TotalFrame.ToTimeSpan(_project.Framerate);
                    if ((End.Value + Start.Value) >= total)
                    {
                        End.Value = total - Start.Value;
                    }
                }

                UseCache.Value = scene.UseCache;
            });

            UseCache.Subscribe(i => _project.CurrentScene.UseCache = i);

            UseCurrentFrame.Subscribe(i =>
            {
                var scene = _project.CurrentScene;
                i.Value = scene.PreviewFrame.ToTimeSpan(_project.Framerate);
            });

            CreateCache.Subscribe(async () =>
            {
                var scene = _project.CurrentScene;
                var st = Frame.FromTimeSpan(Start.Value, _project.Framerate);
                var ed = Frame.FromTimeSpan(End.Value, _project.Framerate);

                await Task.Run(() => scene.Cache.Create(st, ed - st, LowColor.Value, LowResolution.Value));
            });

            ClearCache.Subscribe(() => _project.CurrentScene.Cache.Clear());

            Start.Subscribe(_ => PresumedUsageCapacity.Value = DataSizeToString(CalculateCacheSize()).GB);
            End.Subscribe(_ => PresumedUsageCapacity.Value = DataSizeToString(CalculateCacheSize()).GB);
            LowColor.Subscribe(_ => PresumedUsageCapacity.Value = DataSizeToString(CalculateCacheSize()).GB);
            LowResolution.Subscribe(_ => PresumedUsageCapacity.Value = DataSizeToString(CalculateCacheSize()).GB);
        }

        public enum BackgroundType
        {
            CheckDark,
            CheckLight,
            Transparent,
            Black,
            White,
        }

        public ReactiveProperty<string> SelectedBackground { get; }

        public ReactiveProperty<double> Speed { get; }

        public ReactiveProperty<TimeSpan> Start { get; } = new();

        public ReactiveProperty<TimeSpan> End { get; } = new();

        public ReactiveProperty<bool> UseCache { get; } = new();

        public ReactiveProperty<bool> LowColor { get; } = new();

        public ReactiveProperty<bool> LowResolution { get; } = new();

        public ReactivePropertySlim<string> PresumedUsageCapacity { get; } = new();

        public ReactiveCommand<ReactiveProperty<TimeSpan>> UseCurrentFrame { get; } = new();

        public AsyncReactiveCommand CreateCache { get; } = new();

        public ReactiveCommand ClearCache { get; } = new();

        public string[] Backgrounds { get; } =
        {
            Strings.Transparent,
            Strings.CheckDark,
            Strings.CheckLight,
            Strings.Black,
            Strings.White,
        };

        private static (string GB, string GiB) DataSizeToString(long size)
        {
            // 桁数
            // 1 - 3 : b
            // 4 - 7 : kb
            // 8 - 11 : mb
            // 12 - 13 : gb
            const decimal kb = 1000;
            const decimal mb = 1000 * 1000;
            const decimal gb = 1000 * 1000 * 1000;
            const decimal kib = 1024;
            const decimal mib = 1024 * 1024;
            const decimal gib = 1024 * 1024 * 1024;
            var digit = (size == 0) ? 1 : ((int)Math.Log10(size) + 1);

            var GB = digit switch
            {
                >= 1 and < 4 => $"{size}B",
                >= 4 and < 7 => $"{size / kb:F2}KB",
                >= 7 and < 10 => $"{size / mb:F2}MB",
                >= 10 and < 13 => $"{size / gb:F2}GB",
                _ => $"{size / mb:F2}MB",
            };
            var GiB = digit switch
            {
                >= 1 and < 4 => $"{size}B",
                >= 4 and < 7 => $"{size / kib:F2}KiB",
                >= 7 and < 10 => $"{size / mib:F2}MiB",
                >= 10 and < 13 => $"{size / gib:F2}GiB",
                _ => $"{size / mib:F2}MiB",
            };

            return (GB, GiB);
        }

        private unsafe long CalculateCacheSize()
        {
            var length = Frame.FromTimeSpan(End.Value, _project.Framerate) - Frame.FromTimeSpan(Start.Value, _project.Framerate);
            var scn = _project.CurrentScene;
            var size = (long)(sizeof(int) * 2) + (sizeof(bool) * 2) + (IntPtr.Size * 2);
            long imgSize;
            if (LowResolution.Value)
            {
                imgSize = (long)(scn.Width / 2) * (scn.Height / 2);
            }
            else
            {
                imgSize = (long)scn.Width * scn.Height;
            }

            if (LowColor.Value)
            {
                imgSize *= sizeof(Bgra4444);
            }
            else
            {
                imgSize *= sizeof(BGRA32);
            }

            size += imgSize;
            size *= length.Value;

            return size;
        }

        private static BackgroundType ToEnum(string str)
        {
            if (str == Strings.Transparent) return BackgroundType.Transparent;
            else if (str == Strings.CheckDark) return BackgroundType.CheckDark;
            else if (str == Strings.CheckLight) return BackgroundType.CheckLight;
            else if (str == Strings.Black) return BackgroundType.Black;
            else if (str == Strings.White) return BackgroundType.White;
            else return BackgroundType.Transparent;
        }

        private static string ToStr(BackgroundType type)
        {
            if (type == BackgroundType.Transparent) return Strings.Transparent;
            else if (type == BackgroundType.CheckDark) return Strings.CheckDark;
            else if (type == BackgroundType.CheckLight) return Strings.CheckLight;
            else if (type == BackgroundType.Black) return Strings.Black;
            else if (type == BackgroundType.White) return Strings.White;
            else return Strings.Transparent;
        }
    }
}