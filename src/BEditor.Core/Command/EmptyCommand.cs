namespace BEditor.Command
{
    /// <summary>
    /// Represents a class that implements an empty <see cref="IRecordCommand"/>.
    /// </summary>
    internal class EmptyCommand : IRecordCommand
    {
        /// <inheritdoc/>
        public void Do()
        {
        }

        /// <inheritdoc/>
        public void Redo()
        {
        }

        /// <inheritdoc/>
        public void Undo()
        {
        }
    }
}