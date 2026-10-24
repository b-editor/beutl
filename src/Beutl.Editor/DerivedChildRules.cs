using Beutl.Audio.Effects;
using Beutl.Editor.Infrastructure;
using Beutl.Editor.Operations;
using Beutl.Graphics3D.Models;

namespace Beutl.Editor;

internal static class DerivedChildRules
{
    public static void Apply(ChangeOperation operation)
    {
        if (operation is not IUpdatePropertyValueOperation update) return;
        var (name, slot) = PropertyPathHelper.Parse(update.PropertyPath);
        if (slot != PropertyPathSlot.Value) return;

        // Record calls this after the source edit; child observers append to the same transaction or edit session.
        switch (update.Object)
        {
            case Model3D model when name == nameof(Model3D.Source):
                model.Children.Clear();
                if (model.Source.Value is { HasUri: true })
                    model.SynchronizeSourceGeometry();
                break;
            case EqualizerEffect equalizer when name == nameof(EqualizerEffect.BandCountOption):
                equalizer.InitializeBands(equalizer.BandCountOption.Value);
                break;
        }
    }
}
