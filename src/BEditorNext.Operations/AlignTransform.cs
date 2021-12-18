using BEditorNext.Media;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations;

public sealed class AlignOperation : RenderOperation
{
    public static readonly PropertyDefine<AlignmentX> XProperty;
    public static readonly PropertyDefine<AlignmentY> YProperty;

    static AlignOperation()
    {
        XProperty = RegisterProperty<AlignmentX, AlignOperation>(nameof(X), (owner, obj) => owner.X = obj, owner => owner.X)
            .EnableEditor()
            .Header("XString")
            .JsonName("x");

        YProperty = RegisterProperty<AlignmentY, AlignOperation>(nameof(Y), (owner, obj) => owner.Y = obj, owner => owner.Y)
            .EnableEditor()
            .Header("YString")
            .JsonName("y");
    }

    public AlignmentX X { get; set; }

    public AlignmentY Y { get; set; }

    public override void Render(in OperationRenderArgs args)
    {
        for (int i = 0; i < args.List.Count; i++)
        {
            IRenderable item = args.List[i];
            if (item is RenderableBitmap bmp)
            {
                bmp.Alignment = (X, Y);
            }
        }
    }
}
