using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

using BEditor.LangResources;
using BEditor.ViewModels.Settings;

using Reactive.Bindings;

namespace BEditor.ViewModels.Setup
{
    public sealed class CommonViewModel
    {
        public sealed record GraphicsProfile(string Name, string Description);

        public CommonViewModel()
        {
            Languages = BEditor.Settings.Default.SupportedLanguages;
            SelectedLanguage.Value = BEditor.Settings.Default.Language;

            SelectedLanguage.Subscribe(lang =>
            {
                if (!CultureInfo.CurrentUICulture.Equals(lang.Culture))
                {
                    LanguageRemark.Value = Strings.TheChangesWillBeAppliedAfterRestarting;
                }
                else
                {
                    LanguageRemark.Value = "";
                }

                BEditor.Settings.Default.Language = lang;
            });

            SelectedProfile.Subscribe(p => BEditor.Settings.Default.GraphicsProfile = p);
        }

        public ObservableCollection<SupportedLanguage> Languages { get; }

        public ReactiveProperty<SupportedLanguage> SelectedLanguage { get; } = new();

        public ReactivePropertySlim<string> LanguageRemark { get; } = new("");

        public string[] Profiles { get; } = new[]
        {
            ProjectViewModel.OPENGL,
            ProjectViewModel.SKIA,
            ProjectViewModel.DIRECT3D11,
            ProjectViewModel.METAL,
            ProjectViewModel.VULKAN,
        };

        public ReactiveProperty<string> SelectedProfile { get; } = new(BEditor.Settings.Default.GraphicsProfile);
    }
}