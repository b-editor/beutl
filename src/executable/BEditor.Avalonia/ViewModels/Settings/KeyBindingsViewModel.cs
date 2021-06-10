using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Input;

using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.ViewModels.Settings
{
    public sealed class KeyBindingsViewModel
    {
        public KeyBindingsViewModel()
        {
            SelectedBinding = SelectedCommand
                .Select(i => KeyBindingModel.Bindings.FirstOrDefault(c => c.CommandName == i.Name))
                .ToReadOnlyReactivePropertySlim();

            Key = SelectedBinding.Select(i => i?.Key).ToReactiveProperty();

            Apply.Where(_ => SelectedBinding.Value is not null)
                .Subscribe(async _ =>
                {
                    if (Key.Value is not null)
                    {
                        SelectedBinding.Value!.Key = Key.Value;
                        await KeyBindingModel.SaveAsync();
                    }
                });
        }

        public KeyBindingCommand[] Commands { get; } = KeyBindingModel.AllCommands;

        public ReactiveProperty<KeyBindingCommand> SelectedCommand { get; } = new(KeyBindingModel.AllCommands[0]);

        public ReadOnlyReactivePropertySlim<KeyBindingModel?> SelectedBinding { get; }

        public ReactiveProperty<string?> Key { get; }

        public ReactiveCommand Apply { get; } = new();
    }
}