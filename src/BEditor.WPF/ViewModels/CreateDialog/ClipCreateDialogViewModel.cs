using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Media;
using BEditor.Models;
using BEditor.Models.Extension;

using Reactive.Bindings;

namespace BEditor.ViewModels.CreateDialog
{
    public class ClipCreateDialogViewModel
    {
        public ClipCreateDialogViewModel()
        {
            Start = new ReactiveProperty<int>(1)
                .SetValidateNotifyError(value => (value <= 0) ? string.Format(Resources.RangeAbove, "0") : null);
            Length = new ReactiveProperty<int>(180)
                .SetValidateNotifyError(value => (value <= 0) ? string.Format(Resources.RangeAbove, "0") : null);
            Layer = new ReactiveProperty<int>(1)
                .SetValidateNotifyError(value => (value <= 0) ? string.Format(Resources.RangeAbove, "0") : null);

            AddCommand.Subscribe(() =>
            {
                if (!Scene.Value.InRange(Start.Value, Start.Value + Length.Value, Layer.Value))
                {
                    Message.Snackbar("指定した場所にクリップが存在しているため、新しいクリップを配置できません");

                    return;
                }

                Scene.Value.CreateAddCommand(Start.Value, Layer.Value, Type.Value, out var data).Execute();

                if (Name.Value != string.Empty) data.LabelText = Name.Value;

                data.End = Start.Value + Length.Value;
            });
        }

        public ReactiveProperty<Scene> Scene { get; } = new(AppData.Current.Project!.PreviewScene);
        public ReactiveProperty<ObjectMetadata> Type { get; } = new(ObjectMetadata.LoadedObjects[0]);
        public ReactiveProperty<int> Start { get; }
        public ReactiveProperty<int> Length { get; }
        public ReactiveProperty<int> Layer { get; }
        public ReactiveProperty<string> Name { get; } = new("");
        public ReactiveCommand AddCommand { get; } = new();
    }
}
