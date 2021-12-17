using BEditorNext.Framework.Service;
using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

namespace BEditorNext.Operations;

internal class TestOperation : RenderOperation
{
    public static readonly PropertyDefine<bool> BooleanProperty;
    public static readonly PropertyDefine<float> NumberProperty;
    public static readonly PropertyDefine<Size> SizeProperty;
    private readonly INotificationService _service;

    static TestOperation()
    {
        BooleanProperty = RegisterProperty<bool, TestOperation>("Boolean")
            .DefaultValue(false)
            .Animatable()
            .JsonName("boolean")
            .EnableEditor();

        NumberProperty = RegisterProperty<float, TestOperation>("Number")
            .DefaultValue(0)
            .Animatable()
            .JsonName("number")
            .EnableEditor();

        SizeProperty = RegisterProperty<Size, TestOperation>("Size")
            .DefaultValue(new Size(100, 100))
            .Animatable()
            .JsonName("size")
            .EnableEditor();
    }

    public TestOperation()
    {
        _service = ServiceLocator.Current.GetRequiredService<INotificationService>();

        Setters.FirstOrDefault(i => i.Property == BooleanProperty)?.GetObservable().Subscribe(_ =>
        {
            _service.Show(
                new Notification("Change Boolean", "Booleanが変更された", (NotificationType)Random.Shared.Next(0, 4)));
        });
    }

    public override void Render(in OperationRenderArgs args)
    {
    }
}
