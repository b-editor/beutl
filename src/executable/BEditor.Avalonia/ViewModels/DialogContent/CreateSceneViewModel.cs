using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.DialogContent
{
    public sealed class CreateSceneViewModel
    {
        private readonly Project _project;

        public CreateSceneViewModel()
        {
            _project = AppModel.Current.Project;
            Width = new((uint)_project.CurrentScene.Width);
            Height = new((uint)_project.CurrentScene.Height);
            Name = new ReactiveProperty<string>($"{Strings.Scene}{_project.SceneList.Count}")
                .SetValidateNotifyError(name =>
                {
                    if (_project.SceneList.Any(s => s.SceneName == name))
                    {
                        return Strings.ThisNameAlreadyExists;
                    }
                    else
                    {
                        return null;
                    }
                });

            Create.Subscribe(() =>
            {
                var scene = new Scene((int)Width.Value, (int)Height.Value)
                {
                    SceneName = Name.Value,
                    Parent = _project
                };

                scene.Load();
                _project.SceneList.Add(scene);
                _project.CurrentScene = scene;
            });
        }

        public ReactivePropertySlim<uint> Width { get; }
        public ReactivePropertySlim<uint> Height { get; }
        public ReactiveProperty<string> Name { get; }
        public ReactiveCommand Create { get; } = new();
    }
}