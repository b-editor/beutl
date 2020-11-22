namespace BEditor.Core.Command
{
    /// <summary>
    /// 実行、元に戻す(undo)、やり直す(redo)の動作を表します
    /// </summary>
    public interface IRecordCommand
    {
        /// <summary>
        /// 操作を実行します
        /// <para>例外を投げた場合キャンセルされます</para>
        /// </summary>
        public void Do();

        /// <summary>
        /// 操作を元に戻します
        /// </summary>
        public void Undo();

        /// <summary>
        /// 操作をやり直します
        /// </summary>
        public void Redo();
    }
}
