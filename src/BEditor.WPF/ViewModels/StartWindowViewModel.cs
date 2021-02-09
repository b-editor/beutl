using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Properties;
using BEditor.Views.StartWindowControl;

using MaterialDesignThemes.Wpf;

using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public class StartWindowViewModel
    {
        public StartWindowViewModel()
        {
            MenuItems.Add(new(Resources.Project, PackIconKind.Layers, new ProjectsControl())
            {
                IsChecked = { Value = true }
            });
            MenuItems.Add(new(Resources.LearnHowToUse, PackIconKind.School, null!));

            foreach (var item in MenuItems)
            {
                item.IsChecked.Where(v => v).Subscribe(v =>
                {
                    Selected.Value = item;
                });
            }
        }

        public ReactiveCollection<MenuItem> MenuItems { get; } = new();

        public ReactiveProperty<MenuItem> Selected { get; } = new();


        public record MenuItem(string Text, PackIconKind PackIconKind, object? Control)
        {
            public ReactiveProperty<bool> IsChecked { get; } = new();
        }
    }
}
