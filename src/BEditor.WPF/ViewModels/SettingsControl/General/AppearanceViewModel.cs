using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reactive.Linq;
using System.Text;
using System.Windows;

using BEditor.Data;

using MaterialDesignThemes.Wpf;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.SettingsControl.General
{
    public class AppearanceViewModel
    {
        private ReactiveCommand<object>? darkmodecommand;

        public ReactiveCommand<object> UseDarkModeClick
        {
            get
            {
                if (darkmodecommand == null)
                {
                    darkmodecommand = new();
                    darkmodecommand.Subscribe(_ =>
                    {
                        var paletteHelper = new PaletteHelper();
                        ITheme theme = paletteHelper.GetTheme();
                        var baseTheme = Settings.Default.UseDarkMode ? Theme.Dark : Theme.Light;

                        paletteHelper.SetTheme(theme);
                    });
                }

                return darkmodecommand;
            }
        }
        public ReactiveCollection<string> Langs { get; } = new()
        {
            "ja-JP",
            "en-US",
        };
    }
}