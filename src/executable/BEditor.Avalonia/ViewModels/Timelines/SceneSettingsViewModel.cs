using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.Timelines
{
    public sealed class SceneSettingsViewModel
    {
        private readonly Scene _scene;

        public SceneSettingsViewModel(Scene scene)
        {
            _scene = scene;
            Width = new((uint)scene.Width);
            Height = new((uint)scene.Height);
            Name = new ReactiveProperty<string>(scene.SceneName)
                .SetValidateNotifyError(name =>
                {
                    if (_scene.Parent.SceneList.Any(s => s != _scene && s.SceneName == name))
                    {
                        return Strings.ThisNameAlreadyExists;
                    }
                    else
                    {
                        return null;
                    }
                });

            Apply.Subscribe(() => _scene.Settings = new((int)Width.Value, (int)Height.Value, Name.Value));
        }

        public ReactivePropertySlim<uint> Width { get; }

        public ReactivePropertySlim<uint> Height { get; }

        public ReactiveProperty<string> Name { get; }

        public ReactiveCommand Apply { get; } = new();
    }
}