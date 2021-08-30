using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Media;

using BEditor.Data;
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
                    Length.Value = scene.Cache.Length.ToTimeSpan(_project.Framerate);
                }
                else
                {
                    if (Start.Value >= scene.TotalFrame.ToTimeSpan(_project.Framerate))
                    {
                        Start.Value = default;
                    }

                    var total = scene.TotalFrame.ToTimeSpan(_project.Framerate);
                    if ((Length.Value + Start.Value) >= total)
                    {
                        Length.Value = total - Start.Value;
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
                var len = Frame.FromTimeSpan(Length.Value, _project.Framerate);

                await Task.Run(() => scene.Cache.Create(st, len));
            });

            ClearCache.Subscribe(() => _project.CurrentScene.Cache.Clear());
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

        public ReactiveProperty<TimeSpan> Length { get; } = new();

        public ReactiveProperty<bool> UseCache { get; } = new();

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