using System.Globalization;

using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;

using BeUtl.Framework;

namespace PackageSample;

public sealed class SamplePackage : Package
{
    public override IEnumerable<Extension> GetExtensions()
    {
        yield return new SampleExtension();
        yield return new SampleEditorExtension();
        yield return new SSETExtenison();
        yield return new SamplePageExtension();
    }

    public override IResourceProvider? GetResource(CultureInfo ci)
    {
        return new ResourceInclude
        {
            Source = new Uri("avares://PackageSample/Resources/ja-JP/Strings.axaml")
        };
    }
}
