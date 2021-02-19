using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;

using Reactive.Bindings;

namespace BEditor.ViewModels.SettingsControl
{
    public class SceneSettingsViewModel
    {
        private readonly Scene scene;

        public SceneSettingsViewModel(Scene scene)
        {
            this.scene = scene;
            Width.Value = (uint)scene.Width;
            Height.Value = (uint)scene.Height;
            Color.Value = scene.BackgroundColor.ToString("#argb");
            Name.Value = scene.Name;

            AdaptationCommand.Subscribe(_ =>
            {
                this.scene.Settings = new((int)Width.Value, (int)Height.Value, Name.Value, Drawing.Color.FromHTML(Color.Value));
            });
        }

        public ReactiveProperty<uint> Width { get; } = new();
        public ReactiveProperty<uint> Height { get; } = new();
        public ReactiveProperty<string> Color { get; } = new();
        public ReactiveProperty<string> Name { get; } = new();
        public ReactiveCommand AdaptationCommand { get; } = new();
    }
}
