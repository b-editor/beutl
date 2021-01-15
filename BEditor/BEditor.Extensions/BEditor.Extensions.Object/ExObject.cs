using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using BEditor.Core.Data;

namespace BEditor.Extensions.Object
{
    public class Exobject
    {
        public Exobject(ExobjectHeader header, Exeffect exeffects)
        {
            Header = header;
            Effects = exeffects;
        }

        public ExobjectHeader Header { get; }
        public Exeffect Effects { get; }
    }
}
