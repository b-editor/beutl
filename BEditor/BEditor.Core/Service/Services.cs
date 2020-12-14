using System;
using System.Reflection;

namespace BEditor.Core.Service
{
    public static class Services
    {
        public static string Path { get; } = System.IO.Path.GetDirectoryName(AppContext.BaseDirectory);

        public static IFileDialogService FileDialogService { get; set; }
        public static IGraphicsContextService GraphicsContextService { get; set; } = new GraphicsContextService();
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
