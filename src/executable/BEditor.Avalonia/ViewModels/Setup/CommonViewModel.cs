using System;
using System.Globalization;
using System.Linq;

using BEditor.Properties;
using BEditor.ViewModels.Settings;

using Reactive.Bindings;

namespace BEditor.ViewModels.Setup
{
    public sealed class CommonViewModel
    {
        public record SupportedLangage(string Name, string Culture);

        public record GraphicsProfile(string Name, string Description);

        public CommonViewModel()
        {
            Languages = new SupportedLangage[]
            {
                new("Japanese", "ja-JP"),
                new($"English ({Strings.MachineTranslation})", "en-US"),
            };
            SelectedLanguage.Value = Languages.First(l => l.Culture == CultureInfo.CurrentUICulture.Name);

            SelectedLanguage.Subscribe(lang =>
            {
                if (CultureInfo.CurrentUICulture.Name != lang.Culture)
                {
                    LanguageRemark.Value = Strings.TheChangesWillBeAppliedAfterRestarting;
                }
                else
                {
                    LanguageRemark.Value = "";
                }

                BEditor.Settings.Default.Language = lang.Culture;
            });

            SelectedProfile.Subscribe(p => BEditor.Settings.Default.GraphicsProfile = p);
        }

        public SupportedLangage[] Languages { get; }

        public ReactiveProperty<SupportedLangage> SelectedLanguage { get; } = new();

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