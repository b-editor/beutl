using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Beutl.Framework;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.Views;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SceneOutputExtension : OutputExtension
{
    public static readonly SceneOutputExtension Instance = new();

    public override string Name => "Scene Output";

    public override string DisplayName => "Scene Output";

    public override FilePickerFileType GetFilePickerFileType()
    {
        return new FilePickerFileType(Strings.SceneFile)
        {
            Patterns = new string[]
            {
                "*.scene"
            }
        };
    }

    public override IconSource? GetIcon()
    {
        return new SymbolIconSource
        {
            Symbol = Symbol.Document
        };
    }

    public override bool MatchFileExtension(string ext)
    {
        return ext is ".scene";
    }

    public override bool TryCreateContext(string file, [NotNullWhen(true)] out IOutputContext? context)
    {
        if (file.EndsWith(".scene") && File.Exists(file))
        {
            context = new OutputViewModel(new SceneFile(file));
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }

    public override bool TryCreateControl(string file, [NotNullWhen(true)] out Control? control)
    {
        if (file.EndsWith(".scene"))
        {
            control = new OutputView();
            return true;
        }
        else
        {
            control = null;
            return false;
        }
    }
}
