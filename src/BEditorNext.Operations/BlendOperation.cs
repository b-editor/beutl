using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;
using BEditorNext.Rendering;

namespace BEditorNext.Operations;

public sealed class BlendOperation : RenderOperation
{
    public static readonly PropertyDefine<BlendMode> BlendModeProperty;

    static BlendOperation()
    {
        BlendModeProperty = RegisterProperty<BlendMode, BlendOperation>(nameof(BlendMode), (o, v) => o.BlendMode = v, o => o.BlendMode)
            .EnableEditor()
            .DefaultValue(BlendMode.SrcOver)
            .Header("BlendString")
            .JsonName("blendMode");
    }

    public BlendMode BlendMode { get; set; }

    public override void Render(in OperationRenderArgs args)
    {
        for (int i = 0; i < args.List.Count; i++)
        {
            IRenderable item = args.List[i];

            if (item is IDrawable drawable)
            {
                drawable.BlendMode = BlendMode;
            }
        }
    }
}
