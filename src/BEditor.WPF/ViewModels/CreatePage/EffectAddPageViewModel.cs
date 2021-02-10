using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.ViewModels.CreatePage
{
    public class EffectAddPageViewModel
    {
        public EffectAddPageViewModel()
        {
            ClipItems = Scene.Select(i => i.Datas).Select(i => i.Select(c =>
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
            }).ToArray()).ToReactiveProperty()!;
            ClipItems.Value[0].IsSelected.Value = true;
            Effect.Value = AllEffects.First();

            AddCommand.Subscribe(() =>
            {
                var effectinstance = Effect.Value.Metadata.CreateFunc();

                TargetClip.AddEffect(effectinstance).Execute();
            });
        }

        public ReactiveProperty<Scene> Scene { get; } = new(AppData.Current.Project!.SceneList[0]);
        public ReactiveProperty<EffectItem> Effect { get; } = new();
        public IEnumerable<EffectItem> AllEffects
            => EffectMetadata.LoadedEffects
                .SelectMany(i => i.Children?
                    .Select(i2 => new EffectItem(i2, i.Name)) ?? new EffectItem[] { new(i, null) });
        public ReactiveProperty<ClipItem[]> ClipItems { get; }
        public ClipElement TargetClip => ClipItems.Value.Where(i => i.IsSelected.Value).First().Clip;
        public ReactiveCommand AddCommand { get; } = new();

        public record EffectItem(EffectMetadata Metadata, string? ParentName)
        {
            public string Name => ParentName is null ? Metadata.Name : $"{ParentName}.{Metadata.Name}";
        }
        public record ClipItem(ClipElement Clip)
        {
            public ReactiveProperty<bool> IsSelected { get; } = new();
            public ReactiveCommand<ClipItem> Command { get; } = new();
        }
    }
}
