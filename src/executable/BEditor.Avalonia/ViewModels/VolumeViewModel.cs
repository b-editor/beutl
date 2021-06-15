using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Media;

using BEditor.Media;
using BEditor.Media.PCM;

using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public class VolumeViewModel
    {
        public VolumeViewModel(ReactiveProperty<Sound<StereoPCMFloat>?> property)
        {
            Sound = property;
            property.Subscribe(sound =>
            {
                Left.Clear();
                Right.Clear();
                if (sound is null) return;
                var (left, right) = sound.RMS();

                if (double.IsFinite(left))
                {
                    for (var i = 0; i < -(left / 5); i++)
                    {
                        Left.Add(new()
                        {
                            Brush = Brushes.White
                        });
                    }
                }
                if (double.IsFinite(right))
                {
                    for (var i = 0; i < -(right / 5); i++)
                    {
                        Right.Add(new()
                        {
                            Brush = Brushes.White
                        });
                    }
                }
            });
        }

        public ReactiveProperty<Sound<StereoPCMFloat>?> Sound { get; }

        public ObservableCollection<Item> Left { get; } = new();

        public ObservableCollection<Item> Right { get; } = new();

        public struct Item
        {
            public IBrush Brush { get; set; }
        }
    }
}
