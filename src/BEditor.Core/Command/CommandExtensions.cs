// CommandExtensions.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;

namespace BEditor
{
    /// <summary>
    /// Represents a class that provides an extension method.
    /// </summary>
    public static class CommandExtensions
    {
        /// <summary>
        /// Execute IRecordCommand.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        public static void Execute(this IRecordCommand command)
        {
            CommandManager.Default.Do(command);
        }

        /// <summary>
        /// Execute IRecordCommand with CommandManager specified.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        /// <param name="manager">The command manager for executing commands.</param>
        public static void Execute(this IRecordCommand command, CommandManager manager)
        {
            manager.Do(command);
        }
    }
}