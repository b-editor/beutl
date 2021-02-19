using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Properties;
using BEditor.Media;
using BEditor.Models;
using BEditor.Models.Extension;

using Reactive.Bindings;
using Microsoft.Extensions.DependencyInjection;
namespace BEditor.ViewModels.CreatePage
{
    public class ClipCreatePageViewModel
    {
        public ClipCreatePageViewModel()
        {
            Start = new ReactiveProperty<int>(1)
                .SetValidateNotifyError(value => (value <= 0) ? string.Format(Resources.RangeAbove, "0") : null);
            Length = new ReactiveProperty<int>(180)
                .SetValidateNotifyError(value => (value <= 0) ? string.Format(Resources.RangeAbove, "0") : null);
            Layer = new ReactiveProperty<int>(1)
                .SetValidateNotifyError(value => (value <= 0) ? string.Format(Resources.RangeAbove, "0") : null);
            TypeItems = new(ObjectMetadata.LoadedObjects.Select(i =>
            {
                var typeItem = new TypeItem(i);

                typeItem.Command.Subscribe(i =>
                {
                    foreach(var item in TypeItems!)
                    {
                        item.IsSelected.Value = false;
                    }

                    i.IsSelected.Value = !i.IsSelected.Value;
                });

                return typeItem;
            }));
            TypeItems[0].IsSelected.Value = true;

            AddCommand.Subscribe(() =>
            {
                if (!Scene.Value.InRange(Start.Value, Start.Value + Length.Value, Layer.Value))
                {
                    Scene.Value.ServiceProvider?.GetService<IMessage>()?
                        .Snackbar("指定した場所にクリップが存在しているため、新しいクリップを配置できません");

                    return;
                }

                Scene.Value.AddClip(Start.Value, Layer.Value, Type, out var data).Execute();

                if (Name.Value != string.Empty) data.LabelText = Name.Value;

                data.End = Start.Value + Length.Value;
            });
        }

        public ReactiveProperty<Scene> Scene { get; } = new(AppData.Current.Project!.PreviewScene);
        public ObjectMetadata Type => TypeItems.Where(i => i.IsSelected.Value).First().Metadata;
        public ReactiveProperty<int> Start { get; }
        public ReactiveProperty<int> Length { get; }
        public ReactiveProperty<int> Layer { get; }
        public ReactiveProperty<string> Name { get; } = new("");
        public ReactiveCommand AddCommand { get; } = new();
        public ObservableCollection<TypeItem> TypeItems { get; }

        public record TypeItem(ObjectMetadata Metadata)
        {
            public ReactiveProperty<bool> IsSelected { get; } = new();
            public ReactiveCommand<TypeItem> Command { get; } = new();
        }
    }
}
