using Beutl.Extensibility;
using Beutl.Services;

namespace PackageSample;

[Export]
public sealed class SampleExtension : LayerExtension
{
    public override string Name => "SampleExtension";

    public override string DisplayName => "SampleExtension";

    public override void Load()
    {
        LibraryService.Current.Register<SampleOp>(KnownLibraryItemFormats.EngineObject, "Sample Operator");
        LibraryService.Current.AddMultiple("Sample Drawables", item =>
            item.BindDrawable<SampleDrawable>()
        );
        LibraryService.Current.Register<ChoicesProviderTest>(KnownLibraryItemFormats.FilterEffect, "ChoicesProviderTest");
    }
}
