using System;
using System.Globalization;
using System.Linq;

using BEditor.LangResources;
using BEditor.ViewModels.Settings;

using Reactive.Bindings;

namespace BEditor.ViewModels.Setup
{
    public sealed class CommonViewModel
    {
        public sealed record SupportedLangage(string Name, string Culture);

        public sealed record GraphicsProfile(string Name, string Description);

        public CommonViewModel()
        {
            Languages = new SupportedLangage[]
            {
                new("Japanese", "ja-JP"),
                new($"English ({Strings.MachineTranslation})", "en-US"),
            };
            SelectedLanguage.Value = Array.Find(Languages, l => l.Culture == CultureInfo.CurrentCulture.Name) ?? Languages[0];

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