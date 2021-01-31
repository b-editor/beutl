using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;

namespace BEditor.Core.Data.Primitive
{
    /// <summary>
    /// 
    /// </summary>
    [DataContract]
    public abstract class MultipleImageObject : ImageObject
    {
        /// <summary>
        /// このオブジェクトをマルチプルオブジェクトとして使うかを取得します。
        /// </summary>
        public virtual bool IsMultiple => true;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public abstract IEnumerable<ImageInfo> MultipleRender(EffectRenderArgs args);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <param name="index"></param>
        public virtual void Rendered(EffectRenderArgs<ImageInfo[]> args, int index) { }
        /// <inheritdoc/>
        protected override Image<BGRA32>? OnRender(EffectRenderArgs args)
        {
            return MultipleRender(args).FirstOrDefault()?.Source;
        }
    }
}
