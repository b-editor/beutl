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


        public Exobject(ExobjectHeader header)
        {
            Header = header;
        }

        public ExobjectHeader Header { get; }
        public List<Exeffect> Effects { get; } = new();
    }
}
