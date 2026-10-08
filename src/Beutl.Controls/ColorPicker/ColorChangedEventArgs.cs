using System;
using FluentAvalonia.UI.Media;

namespace FluentAvalonia.UI.Controls;

/// <summary>
/// Data for the <see cref="FAColorPicker.ColorChanged"/> event
/// </summary>
public sealed class ColorChangedEventArgs : EventArgs
{
    internal ColorChangedEventArgs(Color2 newC)
    {
        NewColor = newC;
    }

    /// <summary>
    /// The new Color
    /// </summary>
    public Color2 NewColor { get; }
}
