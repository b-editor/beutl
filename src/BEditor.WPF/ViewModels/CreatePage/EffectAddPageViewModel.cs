using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Models;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.CreatePage
{
    public sealed class EffectAddPageViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposable = new();

        public EffectAddPageViewModel()
        {
            ClipItems = Scene.Select(i => i.Datas)
                .Select(i => i.Select(c =>
            {
                var typeItem = new ClipItem(c);


                typeItem.Command.Subscribe(ci =>
                {
                    foreach (var item in ClipItems!.Value!)
                    {
                        item.IsSelected.Value = false;
                    }

                    ci.IsSelected.Value = !ci.IsSelected.Value;
                });

                return typeItem;
            }).ToArray())
                .ToReadOnlyReactivePropertySlim()
                .AddTo(_disposable)!;

            ClipItems.Value[0].IsSelected.Value = true;
            Effect.Value = AllEffects.First();

            AddCommand.Subscribe(() =>
            {
                var effectinstance = Effect.Value.Metadata.CreateFunc();

                TargetClip.AddEffect(effectinstance).Execute();
            }).AddTo(_disposable);
        }
        ~EffectAddPageViewModel()
        {
            _disposable.Dispose();
        }

        public ReactivePropertySlim<Scene> Scene { get; } = new(AppData.Current.Project!.SceneList[0]);
        public ReactiveProperty<EffectItem> Effect { get; } = new();
        public static IEnumerable<EffectItem> AllEffects
            => EffectMetadata.LoadedEffects
                .SelectMany(i => i.Children?
                    .Select(i2 => new EffectItem(i2, i.Name)) ?? new EffectItem[] { new(i, null) });
        public ReadOnlyReactivePropertySlim<ClipItem[]> ClipItems { get; }
        public ClipElement TargetClip => ClipItems.Value.Where(i => i.IsSelected.Value).First().Clip;
        public ReactiveCommand AddCommand { get; } = new();

        public record EffectItem(EffectMetadata Metadata, string? ParentName)
        {
            public string Name => ParentName is null ? Metadata.Name : $"{ParentName}.{Metadata.Name}";
        }
        public record ClipItem(ClipElement Clip)
        {
            public ReactivePropertySlim<bool> IsSelected { get; } = new();
            public ReactiveCommand<ClipItem> Command { get; } = new();
        }

        public void Dispose()
        {
            Scene.Dispose();
            Effect.Dispose();
            foreach (var item in ClipItems.Value)
            {
                item.IsSelected.Dispose();
                item.Command.Dispose();
            }
            ClipItems.Dispose();
            AddCommand.Dispose();
            _disposable.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
