using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BEditor.Page.Languages
{
    public static class ResourceManager
    {
        public static IResources Current { get; set; }
        public static IResources Japanese { get; } = new Japanese();
        public static IResources English { get; } = new Japanese();

        //TODO : クエリパラメータで切り替え

        static ResourceManager()
        {
            Current = Japanese;
        }
    }
}
