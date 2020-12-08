using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.PropertyControl
{
    public class FolderPropertyViewModel
    {
        public FolderPropertyViewModel(FolderProperty property)
        {
            Property = property;
            Command.Subscribe(x =>
            {
                var file = x?.Invoke();

                if (file != null)
                {
                    CommandManager.Do(new FolderProperty.ChangeFolderCommand(Property, file));
                }
            });
            Reset.Subscribe(() => CommandManager.Do(new FolderProperty.ChangeFolderCommand(Property, Property.PropertyMetadata.Default)));
        }

        public FolderProperty Property { get; }
        public ReactiveCommand<Func<string>> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
    }
}
