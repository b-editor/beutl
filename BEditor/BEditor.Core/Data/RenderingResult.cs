using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Media;

namespace BEditor.Core.Data
{
    public readonly ref struct RenderingResult
    {
        public Image Image { get; init; }
    }
}
