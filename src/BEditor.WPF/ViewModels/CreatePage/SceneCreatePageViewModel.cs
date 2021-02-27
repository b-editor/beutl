using BEditor.Data;
using BEditor.Models;
using System.ComponentModel;
using Reactive.Bindings;
using System;
using System.Reactive.Disposables;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.CreatePage
{
    public sealed class SceneCreatePageViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposable = new();

        public SceneCreatePageViewModel()
        {
            Width.Value = AppData.Current.Project!.PreviewScene.Width;
            Height.Value = AppData.Current.Project!.PreviewScene.Height;
            Name.Value = $"Scene{AppData.Current.Project.SceneList.Count}";

            ResetCommand.Subscribe(() =>
            {
                Width.Value = AppData.Current.Project!.PreviewScene.Width;
                Height.Value = AppData.Current.Project!.PreviewScene.Height;
                Name.Value = $"Scene{AppData.Current.Project.SceneList.Count}";
            }).AddTo(_disposable);

            CreateCommand.Subscribe(() =>
            {
                var scene = new Scene(Width.Value, Height.Value) { SceneName = Name.Value, Parent = AppData.Current.Project };
                scene.Load();
                AppData.Current.Project.SceneList.Add(scene);
                AppData.Current.Project.PreviewScene = scene;
            }).AddTo(_disposable);
        }
        ~SceneCreatePageViewModel()
        {
            _disposable.Dispose();
        }

        public ReactiveProperty<int> Width { get; } = new();
        public ReactiveProperty<int> Height { get; } = new();
        public ReactiveProperty<string> Name { get; } = new();

        public ReactiveCommand CreateCommand { get; } = new();
        public ReactiveCommand ResetCommand { get; } = new();

        public void Dispose()
        {
            _disposable.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
