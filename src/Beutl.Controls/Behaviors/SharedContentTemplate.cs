using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using Avalonia.Styling;

namespace Beutl.Controls.Behaviors;

public class SharedContentTemplate : ITemplate<SharedContent>
{
    [Content]
    [TemplateContent]
    public object Content { get; set; }

    public SharedContent Build()
    {
        return (SharedContent)Load(Content!).Control;
    }

    object ITemplate.Build() => Build().Content;

    private static ControlTemplateResult Load(object templateContent)
    {
        if (templateContent is Func<IServiceProvider, object> direct)
        {
            return (ControlTemplateResult)direct(null!);
        }

        throw new ArgumentException(null, nameof(templateContent));
    }
}
