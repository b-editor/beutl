using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Views;

using BEditor.Core.Data;
using Reactive.Bindings;
using BEditor.Views.CreateDialog;

namespace BEditor.ViewModels.ToolControl
{
    public class SceneManagerViewModel : BasePropertyChanged
    {

        public SceneManagerViewModel()
        {
            AddScene.Subscribe(() =>
            {
                new SceneCreateDialog().ShowDialog();
            });
        }

        public ReactiveCommand AddScene { get; } = new();
    }
}
