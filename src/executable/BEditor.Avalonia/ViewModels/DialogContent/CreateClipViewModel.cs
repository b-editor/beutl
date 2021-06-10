using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;

using BEditor.Data;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.Properties;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.ViewModels.DialogContent
{
    public sealed class CreateClipViewModel
    {
        public CreateClipViewModel()
        {
            Start = new ReactiveProperty<int>(1)
                .SetValidateNotifyError(value => (value <= 0) ? string.Format(Strings.RangeAbove, "0") : null);

            Length = new ReactiveProperty<int>(180)
                .SetValidateNotifyError(value => (value <= 0) ? string.Format(Strings.RangeAbove, "0") : null);

            Layer = new ReactiveProperty<int>(1)
                .SetValidateNotifyError(value => (value <= 0) ? string.Format(Strings.RangeAbove, "0") : null);

            TypeItems = ObjectMetadata.LoadedObjects;
            Selected.Value = TypeItems[0];

            Create.Subscribe(() =>
            {
                if (!Scene.Value.InRange(Start.Value, Start.Value + Length.Value, Layer.Value))
                {
                    Scene.Value.ServiceProvider?.GetService<IMessage>()?
                        .Snackbar(Strings.ClipExistsInTheSpecifiedLocation);

                    return;
                }

                Scene.Value.AddClip(Start.Value, Layer.Value, Selected.Value, out var data).Execute();

                if (Name.Value != string.Empty) data.LabelText = Name.Value;

                data.End = Start.Value + Length.Value;
            });
        }

        public ReactivePropertySlim<Scene> Scene { get; } = new(AppModel.Current.Project!.PreviewScene);

        public ReactivePropertySlim<ObjectMetadata> Selected { get; } = new();

        public ReactiveProperty<int> Start { get; }

        public ReactiveProperty<int> Length { get; }

        public ReactiveProperty<int> Layer { get; }

        public ReactiveProperty<string> Name { get; } = new(string.Empty);

        public ReactiveCommand Create { get; } = new();

        public ObservableCollection<ObjectMetadata> TypeItems { get; }
    }
}