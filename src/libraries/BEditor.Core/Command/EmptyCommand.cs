// EmptyCommand.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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