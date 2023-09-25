using Beutl.Extensibility;
using Beutl.Operation;
using Beutl.Services;

namespace PackageSample;

[Export]
public sealed class SampleExtension : LayerExtension
{
    public override string Name => "SampleExtension";

    public override string DisplayName => "SampleExtension";

    public override void Load()
    {
        LibraryService.Current.Register<SampleOp>(KnownLibraryItemFormats.SourceOperator, "Sample Operator");
        LibraryService.Current.Register<ChoicesProviderTest>(KnownLibraryItemFormats.FilterEffect, "ChoicesProviderTest");
    }
}
