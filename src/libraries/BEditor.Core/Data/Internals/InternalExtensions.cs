// InternalExtensions.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;

namespace BEditor.Data
{
    internal static class InternalExtensions
    {
        public static void SetParent<T1, T2>(this IEnumerable<T2>? children, Action<T2> set)
            where T2 : IChild<T1>
        {
            if (children is null) return;
            foreach (var item in children)
            {
                if (item != null) set(item);
            }
        }
    }
}