using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Beutl.Utilities;

public static class StringFormats
{
    // https://teratail.com/questions/136799#reply-207332
    public static string ToHumanReadableSize(double size, int scale = 0, int standard = 1024)
    {
        string[] unit = ["B", "KB", "MB", "GB"];
        if (scale == unit.Length - 1 || size <= standard) { return $"{size:F} {unit[scale]}"; }
        return ToHumanReadableSize(size / standard, scale + 1, standard);
    }
}
