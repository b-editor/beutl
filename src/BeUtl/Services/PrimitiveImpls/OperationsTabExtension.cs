using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;

using BeUtl.Framework;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;
using BeUtl.Views.Editors;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class OperationsTabExtension : ToolTabExtension
{
    public static readonly OperationsTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Operations editor";

    public override string DisplayName => "Operations editor";

    public override ResourceReference<string>? Header => "S.Common.Operations";

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new OperationsEditor();
            return true;
        }
        else
        {
            control = null;
            return false;
        }
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel)
        {
            context = new OperationsEditorViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
