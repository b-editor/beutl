using System;
using System.Reflection;

namespace BEditor.Core.Service
{
    public static class Services
    {
        public static IFileDialogService? FileDialogService { get; set; }
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
        /// 出力中であることを表します
        /// </summary>
        Output
    }
}
