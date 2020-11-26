using System;
using System.Collections.Generic;
using System.Text;
using BEditor.Core.Data;
using MaterialDesignThemes.Wpf;

using Reactive.Bindings;

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
                            PaletteHelper paletteHelper = new PaletteHelper();
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
