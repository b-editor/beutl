using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

internal class TestOperation : RenderOperation
{
    public static readonly PropertyDefine<bool> BooleanProperty;
    public static readonly PropertyDefine<float> NumberProperty;
    public static readonly PropertyDefine<Size> SizeProperty;

    static TestOperation()
    {
        BooleanProperty = RegisterProperty<bool, TestOperation>("Boolean")
            .DefaultValue(false)
            .EnableAnimation()
            .JsonName("boolean")
            .EnableEditor();

        NumberProperty = RegisterProperty<float, TestOperation>("Number")
            .DefaultValue(0)
            .EnableAnimation()
            .JsonName("number")
            .EnableEditor();

        SizeProperty = RegisterProperty<Size, TestOperation>("Size")
            .DefaultValue(new Size(100, 100))
            .EnableAnimation()
            .JsonName("size")
            .EnableEditor();
    }

    public override void Render(in OperationRenderArgs args)
    {
    }
}
