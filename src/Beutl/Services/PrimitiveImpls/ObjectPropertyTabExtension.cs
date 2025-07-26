﻿using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ObjectPropertyTabExtension : ToolTabExtension
{
    public static readonly ObjectPropertyTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "Property editor";

    public override string DisplayName => "Property editor";

    public override string? Header => Strings.Properties;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.WrenchScrewdriver };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new ObjectPropertyEditor();
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
            context = new ObjectPropertyEditorViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
