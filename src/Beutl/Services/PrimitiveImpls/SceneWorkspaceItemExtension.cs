using System.Diagnostics.CodeAnalysis;

using Avalonia.Platform.Storage;

using Beutl.Models;
using Beutl.ProjectSystem;

using FluentAvalonia.UI.Controls;

using Serilog;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SceneProjectItemExtension : ProjectItemExtension
{
    private readonly ILogger _logger = Log.ForContext<SceneProjectItemExtension>();
    public static readonly SceneProjectItemExtension Instance = new();

    public override string Name => "Make the scene a project item.";

    public override string DisplayName => "Make the scene a project item.";

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

    public override bool IsSupported(string file)
    {
        return file.EndsWith($".{Constants.SceneFileExtension}");
    }

    public override bool TryCreateItem(string file, [NotNullWhen(true)] out ProjectItem? result)
    {
        result = null;
        if (file.EndsWith($".{Constants.SceneFileExtension}"))
        {
            Scene? scene;
            try
            {
                scene = new Scene();
                scene.Restore(file);
            }
            catch (Exception ex)
            {
                Telemetry.Exception(ex);
                _logger.Error(ex, "Unable to restore the scene.");
                return false;
            }
            result = scene;
            return true;
        }
        else
        {
            return false;
        }
    }
}
