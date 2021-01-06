using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

using BEditor.Core.Data;

using ControlzEx.Theming;

using MaterialDesignThemes.Wpf;

using Reactive.Bindings;

using Theme = MaterialDesignThemes.Wpf.Theme;

namespace BEditor.ViewModels.SettingsControl.General
{
    public class AppearanceViewModel : BasePropertyChanged
    {

        private ReactiveCommand<object> darkmodecommand;
        public ReactiveCommand<object> UseDarkModeClick
        {
            get
            {
                if (darkmodecommand == null)
                {
                    darkmodecommand = new();
                    darkmodecommand.Subscribe(_ =>
                    {
                        if (Settings.Default.UseDarkMode)
                        {
                            var paletteHelper = new PaletteHelper();
                            ITheme theme = paletteHelper.GetTheme();
                            theme.SetBaseTheme(Theme.Dark);
                            paletteHelper.SetTheme(theme);
                        }
                        else
                        {
                            PaletteHelper paletteHelper = new PaletteHelper();
                            ITheme theme = paletteHelper.GetTheme();
                            theme.SetBaseTheme(Theme.Light);
                            paletteHelper.SetTheme(theme);
                        }
                    });
                }

                return darkmodecommand;
            }
        }
    }
}
