using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Models;
using BEditor.Packaging;
using BEditor.Properties;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

using Setting = BEditor.Settings;


namespace BEditor.ViewModels.Settings
{
    public sealed class PackageSourceViewModel
    {
        public PackageSourceViewModel()
        {
            SelectedItem.Value = Setting.Default.PackageSources[0];
            IsSelected = SelectedItem.Select(dir => dir is not null).ToReadOnlyReactivePropertySlim();

            Remove.SelectMany(_ => CanApplyOrRemoveAsync())
                .Where(i => i)
                .Subscribe(_ =>
                {
                    Setting.Default.PackageSources.Remove(SelectedItem.Value);
                    Setting.Default.Save();
                });

            Name = SelectedItem.Select(i => i?.Name)
                .ToReactiveProperty()
                .SetValidateNotifyError(i => i is null ? string.Format(Strings.PleaseEnter, Strings.Name) : null);
            Url = SelectedItem.Select(i => i?.Url?.OriginalString)
                .ToReactiveProperty()
                .SetValidateNotifyError(i => i is null ? string.Format(Strings.PleaseEnter, "Url") : null)!;

            Apply.Subscribe(
                async _ =>
                {
                    if (await CanApplyOrRemoveAsync())
                    {
                        SelectedItem.Value.Url = new(Url.Value!);
                        SelectedItem.Value.Name = Name.Value!;
                        Setting.Default.Save();
                    }
                },
                async e =>
                {
                    var mes = string.Format(Strings.FailedTo, Strings.Operate);
                    await AppModel.Current.Message.DialogAsync(mes);
                    App.Logger.LogError(e, mes);
                });

            Add.SelectMany(_ => CanAddAsync())
                .Where(i => i)
                .Subscribe(
                    _ =>
                    {
                        Setting.Default.PackageSources.Add(new() { Name = Name.Value!, Url = new(Url.Value!) });
                        Setting.Default.Save();
                    },
                    async e =>
                    {
                        var mes = string.Format(Strings.FailedTo, Strings.Operate);
                        await AppModel.Current.Message.DialogAsync(mes);

                        App.Logger.LogError(e, mes);
                    });
        }

        public ReactiveProperty<string?> Name { get; }

        public ReactiveProperty<string?> Url { get; }

        public ReactiveProperty<PackageSourceInfo> SelectedItem { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

        public ReactiveCommand Add { get; } = new();

        public ReactiveCommand Apply { get; } = new();

        public ReactiveCommand Remove { get; } = new();

        private async Task<bool> CanAddAsync()
        {
            var mes = AppModel.Current.Message;

            if (Setting.Default.PackageSources.Any(i => i.Name == Name.Value))
            {
                await mes.DialogAsync(Strings.ThisNameAlreadyExists);

                return false;
            }
            else if (Name.Value is null)
            {
                await mes.DialogAsync(string.Format(Strings.PleaseEnter, Strings.Name));
                return false;
            }
            else if (Url.Value is null)
            {
                await mes.DialogAsync(string.Format(Strings.PleaseEnter, "Url"));
                return false;
            }
            else
            {
                return true;
            }
        }

        private async Task<bool> CanApplyOrRemoveAsync()
        {
            var mes = AppModel.Current.Message;

            if (SelectedItem.Value.Name is "BEditor" && SelectedItem.Value.Url!.OriginalString is "https://beditor.net/api/index.json")
            {
                await mes.DialogAsync(Strings.ThisItemCannotBeChanged);
                return false;
            }
            else if (Name.Value is null)
            {
                await mes.DialogAsync(string.Format(Strings.PleaseEnter, Strings.Name));
                return false;
            }
            else if (Url.Value is null)
            {
                await mes.DialogAsync(string.Format(Strings.PleaseEnter, "Url"));
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}