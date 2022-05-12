using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace BeUtl.Framework;

public abstract class PageExtension : Extension
{
    public abstract Geometry FilledIcon { get; }

    public abstract Geometry RegularIcon { get; }

    public abstract ResourceReference<string> Header { get; }

    public abstract Type Control { get; }

    public abstract Type Context { get; }
}
