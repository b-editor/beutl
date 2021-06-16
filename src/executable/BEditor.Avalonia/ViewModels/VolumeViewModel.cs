using System;

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
                if (sound is null)
                {
                    Left.Value = 0;
                    Right.Value = 0;
                    return;
                }
                var (left, right) = sound.RMS();

                Left.Value = left;
                Right.Value = right;
            });
        }

        public ReactiveProperty<Sound<StereoPCMFloat>?> Sound { get; }

        public ReactiveProperty<double> Left { get; } = new();

        public ReactiveProperty<double> Right { get; } = new();
    }
}