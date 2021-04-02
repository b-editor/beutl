using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Reactive.Bindings;

namespace BEditor.ViewModels.DialogContent
{
    public class CreateProjectViewModel
    {
        public ReactiveProperty<uint> Width { get; } = new(1920);
        public ReactiveProperty<uint> Height { get; } = new(1080);
        public ReactiveProperty<uint> Framerate { get; } = new(30);
        public ReactiveProperty<uint> Samplingrate { get; } = new(44100);
        public ReactiveProperty<string> Name { get; } = new();
        public ReactiveProperty<string> Folder { get; } = new();
        public ReactiveCommand OpenFolerDialog { get; } = new();
        public ReactiveCommand Create { get; } = new();
    }
}
