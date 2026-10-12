using System.Windows.Input;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal sealed record VersionControlPrimaryAction(
    string Label,
    ICommand Command);
