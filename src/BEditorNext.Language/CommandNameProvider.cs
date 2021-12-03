using Avalonia;
using Avalonia.Controls;

namespace BEditorNext.Language;

public class CommandNameProvider
{
    public static CommandNameProvider Instance => LanguageProvider.Instance.CommandName;

    public virtual string AddRenderTask => (string?)Application.Current.FindResource("AddRenderOperationString") ?? "Undefined";

    public virtual string RemoveRenderTask => (string?)Application.Current.FindResource("RemoveRenderOperationString") ?? "Undefined";
}
