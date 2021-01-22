using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;

namespace BEditor.Extensions.Object.Datas
{
    public interface IExEffect
    {
        public EffectElement ToEffectElement(Exobject exobject);
    }
}
