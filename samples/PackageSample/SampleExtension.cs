using Beutl.Extensibility;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

namespace PackageSample;

[Export]
public sealed class SampleExtension : Extension
{
    public override string Name => "SampleExtension";

    public override string DisplayName => "SampleExtension";

    public override void Load(IExtensionContext context)
    {
        var library = context.Services.GetRequiredService<LibraryService>();
        library.Register<SampleObject>(KnownLibraryItemFormats.EngineObject, "Sample Operator");
        library.AddMultiple("Sample Drawables", item =>
            item.BindDrawable<SampleDrawable>()
        );
        library.Register<ChoicesProviderTest>(KnownLibraryItemFormats.FilterEffect, "ChoicesProviderTest");
    }
}
