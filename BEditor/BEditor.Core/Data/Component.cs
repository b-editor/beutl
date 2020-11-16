using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Interfaces;
using BEditor.Core.Media;
using BEditor.Core.Plugin;
using BEditor.Core.Rendering;

namespace BEditor.Core.Data
{
    /// <summary>
    /// シングルトンで現在のプロジェクトやステータスなどを取得できるクラスを表します
    /// </summary>
    public static class Component
    {
        /// <summary>
        /// プラットフォームに依存する関数を共有するフィールド
        /// </summary>
        public static class Funcs
        {
            private static Func<int, int, BaseGraphicsContext> createRenderingContext = (width, height) => new GraphicsContext(width, height);

            /// <summary>
            /// レンダリングコンテキストを作成する関数を取得または設定します
            /// </summary>
            public static Func<int, int, BaseGraphicsContext> CreateGraphicsContext
            {
                get => createRenderingContext;
                set
                {
                    createRenderingContext = value;
                    //ImageHelper.renderer = createRenderingContext(1, 1);
                }
            }
            /// <summary>
            /// ファイルを保存するダイアログを作成する関数を取得または設定します
            /// </summary>
            public static Func<ISaveFileDialog> SaveFileDialog { get; set; }
            public static Func<IApplication> GetApp { get; set; }
        }
    }

    /// <summary>
    /// アプリケーションのステータスを表します
    /// </summary>
    public enum Status
    {
        /// <summary>
        /// 作業をしていない状態を表します
        /// </summary>
        Idle,
        /// <summary>
        /// 編集中であることを表します
        /// </summary>
        Edit,
        /// <summary>
        /// 保存直後であることを表します
        /// </summary>
        Saved,
        /// <summary>
        /// プレビュー再生中であることを表します
        /// </summary>
        Playing,
        /// <summary>
        /// 一時停止している状態を表します
        /// </summary>
        Pause,
        /// <summary>
        /// 出力中であることを表します
        /// </summary>
        Output
    }
}
