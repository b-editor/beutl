using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Media;

using BEditor.Data;
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