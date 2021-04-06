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
            Name.Value = scene.Name;

            AdaptationCommand.Subscribe(_ =>
            {
                this.scene.Settings = new((int)Width.Value, (int)Height.Value, Name.Value);
            });
        }

        public ReactivePropertySlim<uint> Width { get; } = new();
        public ReactivePropertySlim<uint> Height { get; } = new();
        public ReactivePropertySlim<string> Name { get; } = new();
        public ReactiveCommand AdaptationCommand { get; } = new();
    }
}
