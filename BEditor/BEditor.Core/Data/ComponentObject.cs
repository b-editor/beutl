using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq.Expressions;
using System.Runtime.Serialization;

namespace BEditor.Core.Data
{
    /// <summary>
    /// 継承するクラスに対応するUIのデータのキャッシュを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class ComponentObject : BasePropertyChanged, IExtensibleDataObject
    {
        private Dictionary<string, dynamic> componentData;

        /// <summary>
        /// UIなどのキャッシュを入れる配列を取得します
        /// </summary>
        public Dictionary<string, dynamic> ComponentData => componentData ??= new Dictionary<string, dynamic>();

        /// <inheritdoc/>
        public virtual ExtensionDataObject ExtensionData { get; set; }
    }
}
