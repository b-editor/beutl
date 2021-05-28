using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Reactive.Bindings;

namespace BEditor.Models.Start
{
    public sealed record ProjectModel(string Name, string Thumbnail, string FileName)
    {
        public ReactivePropertySlim<bool> IsLoading { get; } = new();
    }
}