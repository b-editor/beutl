using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;

using BEditor.Drawing;
using BEditor.Models;
using BEditor.LangResources;

using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public sealed class AddToColorPaletteViewModel
    {
        private readonly Color _color;

        public AddToColorPaletteViewModel(Color color)
        {
            _color = color;
            Palettes.Value = PaletteRegistry.GetRegistered();
            Name = new ReactiveProperty<string>().SetValidateNotifyError(i =>
            {
                if (NameValidate(i))
                {
                    return Strings.ThisNameAlreadyExists;
                }
                else
                {
                    return null;
                }
            });
            Name.Value = color.ToString("#argb-U");

            NewPaletteName = new ReactiveProperty<string>().SetValidateNotifyError(i =>
            {
                if (PaletteNameValidate(i))
                {
                    return Strings.ThisNameAlreadyExists;
                }
                else
                {
                    return null;
                }
            });

            SelectedPalette.Subscribe(_ =>
            {
                Name.ForceValidate();
                Name.ForceNotify();
            });

            CanAdd = Name.Select(i => !NameValidate(i)).ToReactiveProperty();

            Add = new(CanAdd);
            Add.Subscribe(async () =>
            {
                if (SelectedPalette.Value is null)
                {
                    await AppModel.Current.Message.DialogAsync(Strings.PaletteNotSelected);
                }
                else
                {
                    SelectedPalette.Value.Colors.Add(Name.Value, _color);
                    Close.Execute();
                    PaletteRegistry.Save();
                }
            });

            CanAddPalette = NewPaletteName.Select(i => !PaletteNameValidate(i)).ToReactiveProperty();

            AddPalette = new(CanAddPalette);
            AddPalette.Subscribe(() =>
            {
                PaletteRegistry.Register(new ColorPalette() { Name = NewPaletteName.Value });

                Palettes.Value = null!;
                Palettes.Value = PaletteRegistry.GetRegistered();

                ClosePopup.Execute();
                PaletteRegistry.Save();

                NewPaletteName.ForceNotify();
            });

            DeletePalette = new(SelectedPalette.Select(i => i is not null));
            DeletePalette.Subscribe(() =>
            {
                PaletteRegistry.RemoveRegistered(SelectedPalette.Value!.Id);

                Palettes.Value = null!;
                Palettes.Value = PaletteRegistry.GetRegistered();

                PaletteRegistry.Save();

                SelectedPalette.Value = null;
            });
        }

        public ReactivePropertySlim<IEnumerable<ColorPalette>> Palettes { get; } = new();

        public ReactivePropertySlim<ColorPalette?> SelectedPalette { get; } = new();

        public ReactiveProperty<string> Name { get; }

        public ReactiveProperty<bool> CanAdd { get; }

        public ReactiveCommand Add { get; }

        public ReactiveCommand Close { get; } = new();

        public ReactiveCommand DeletePalette { get; }

        public ReactiveProperty<string> NewPaletteName { get; }

        public ReactiveProperty<bool> CanAddPalette { get; }

        public ReactiveCommand AddPalette { get; }

        public ReactiveCommand ClosePopup { get; } = new();

        private bool NameValidate(string name)
        {
            return SelectedPalette.Value?.Colors.Keys.Contains(name) ?? true;
        }

        private bool PaletteNameValidate(string name)
        {
            return Palettes.Value.Select(i => i.Name).Contains(name);
        }
    }
}