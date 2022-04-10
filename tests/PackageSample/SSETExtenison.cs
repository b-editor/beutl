using Avalonia.Controls;
using Avalonia.Media;

using BeUtl;
using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.ProjectSystem;

namespace PackageSample;

// SampleSceneEditorTabExtenison
public sealed class SSETExtenison : SceneEditorTabExtension
{
    public override Geometry? Icon { get; } = FluentIconsRegular.Document.GetGeometry();

    public override TabPlacement Placement { get; }

    public override bool IsClosable { get; }

    public override ResourceReference<string> Header => "S.SamplePackage.SSETExtension";

    public override string Name => "Sample tab";

    public override string DisplayName => "Sample tab";

    public override object CreateContent(Scene scene)
    {
        return new TextBlock
        {
            Text = "Tab content"
        };
    }
}
