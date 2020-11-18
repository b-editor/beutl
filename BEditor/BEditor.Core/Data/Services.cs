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
using BEditor.Core.Graphics;
using BEditor.Core.DI;
using BEditor.Core.Media;
using BEditor.Core.Plugin;
using BEditor.Core.Renderings;

namespace BEditor.Core.Data
{
    public static class Services
    {
        public static string Path { get; } = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

        public static IFileDialogService FileDialogService { get; set; }
        public static IImageRenderService ImageRenderService { get; set; } = new ImageRenderService();
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
