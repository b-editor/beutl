using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.ViewModels.CreateDialog
{
    public class ClipCreateDialogViewModel
    {
        public ClipCreateDialogViewModel()
        {
            Start = new ReactiveProperty<int>(1)
                .SetValidateNotifyError(value => (value <= 0) ? "1以上の値を指定してください" : null);
            Length = new ReactiveProperty<int>(180)
                .SetValidateNotifyError(value => (value <= 0) ? "1以上の値を指定してください" : null);
            Layer = new ReactiveProperty<int>(1)
                .SetValidateNotifyError(value => (value <= 0) ? "1以上の値を指定してください" : null);

            AddCommand.Subscribe(() =>
            {
                var command = new ClipData.AddCommand(Scene.Value, Start.Value, Layer.Value, Type.Value.Type);

                CommandManager.Do(command);

                if (Name.Value != string.Empty) command.data.LabelText = Name.Value;
                command.data.End = Start.Value + Length.Value;
            });
        }

        public ReactiveProperty<Scene> Scene { get; } = new(AppData.Current.Project.PreviewScene);
        public ReactiveProperty<ObjectMetadata> Type { get; } = new(ObjectMetadata.LoadedObjects[0]);
        public ReactiveProperty<int> Start { get; }
        public ReactiveProperty<int> Length { get; }
        public ReactiveProperty<int> Layer { get; }
        public ReactiveProperty<string> Name { get; } = new("");
        public ReactiveCommand AddCommand { get; } = new();
    }
}
