using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Reactive.Bindings;

namespace BEditor.ViewModels.Settings
{
    public sealed class AppearanceViewModel
    {
        public ReactiveCollection<string> Langs { get; } = new()
        {
            "ja-JP",
            "en-US",
        };
    }
}