using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BEditor.Page.Languages {
    public class Japanese : IResources {
        public string OpenSource => "オープンソース";
        public string Free => "無料";
        public string Extension => "拡張";

        public string OpenSourceDescription => "BEditorはオープンソースです。Viewに依存しない形で開発しているので .NET Core に対応しているプラットフォームなら動かすことができます";
        public string FreeDescription => "BEditorは無料で使うことができます。ライセンスは MIT Licenseです。";
        public string ExtensionDescription => "ユーザーが作った拡張機能でエフェクトなどを追加することができます。<br/> BEditor.Core をCOM参照するだけで拡張機能開発をすることができます";
    }
}
