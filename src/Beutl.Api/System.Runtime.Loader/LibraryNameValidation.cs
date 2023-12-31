// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.Loader
{
    internal partial struct LibraryNameVariation(string prefix, string suffix)
    {
        public string Prefix = prefix;
        public string Suffix = suffix;
    }
}
