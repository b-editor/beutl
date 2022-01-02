using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

public sealed class BlendOperation : ConfigureOperation<IDrawable>
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

    public override void Configure(in OperationRenderArgs args, IDrawable obj)
    {
        obj.BlendMode = BlendMode;
    }
}
