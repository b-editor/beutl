using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Command;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.PropertyControl
{
    public class FilePropertyViewModel
    {
        public FilePropertyViewModel(FileProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();

            Command.Subscribe(x =>
            {
                var file = x?.Invoke(Property.PropertyMetadata?.FilterName, Property.PropertyMetadata?.Filter);

                if (file != null)
                {
                    CommandManager.Do(new FileProperty.ChangeFileCommand(Property, file));
                }
            });
            Reset.Subscribe(() => CommandManager.Do(new FileProperty.ChangeFileCommand(Property, Property.PropertyMetadata.DefaultFile)));
        }

        public ReadOnlyReactiveProperty<FilePropertyMetadata> Metadata { get; }
        public FileProperty Property { get; }
        public ReactiveCommand<Func<string, string, string>> Command { get; } = new();
        public ReactiveCommand Reset { get; } = new();
    }
}
