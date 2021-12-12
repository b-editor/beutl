using BEditorNext.ProjectSystem;

namespace BEditorNext.Operations;

internal class TestOperation : RenderOperation
{
    public static readonly PropertyDefine<bool> BooleanProperty;
    public static readonly PropertyDefine<float> NumberProperty;

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
    }

    public override void Render(in OperationRenderArgs args)
    {
    }
}
