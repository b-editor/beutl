using System;
using System.Collections.Generic;
using System.Text;

namespace BEditor.NET.Plugin {
    public class PluginLoadedEventArgs : EventArgs {

        /// <summary>
        /// PluginLoadedEventArgsのコンストラクタ
        /// </summary>
        /// <param name="filename">Dllのファイル</param>
        /// <param name="issuccesful">成功したかのブーリアン</param>
        /// <param name="e">失敗した場合のException</param>
        public PluginLoadedEventArgs(string filename, bool issuccesful, Exception e) {
            FileName = filename;
            IsSuccessful = issuccesful;
            Exception = e;
        }

        /// <summary>
        /// Dllのファイル名
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// 成功したかのブーリアン
        /// </summary>
        public bool IsSuccessful { get; }

        /// <summary>
        /// 失敗した場合のException
        /// </summary>
        public Exception Exception { get; }
    }
}
