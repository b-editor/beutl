using System;
using System.Collections.Generic;
using System.Text;
using BEditor.ViewModels.Helper;
using MaterialDesignThemes.Wpf;


namespace BEditor.ViewModels.SettingsControl.General {
    public class AppearanceViewModel : BasePropertyChanged {

        private DelegateCommand<object> darkmodecommand;
        public DelegateCommand<object> UseDarkModeClick {
            get {
                if (darkmodecommand == null) {
                    darkmodecommand = new DelegateCommand<object>();
                    darkmodecommand.Subscribe(_ => {
                        if (Properties.Settings.Default.DarkMode) {
                            PaletteHelper paletteHelper = new PaletteHelper();
                            ITheme theme = paletteHelper.GetTheme();
                            theme.SetBaseTheme(Theme.Dark);
                            paletteHelper.SetTheme(theme);
                        }
                        else {
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
