// Extension.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Compute.OpenCL;

namespace BEditor.Compute
{
    internal static class Extension
    {
        public static void CheckError(this int status)
        {
            var code = (CLStatusCode)status;
            if (code != CLStatusCode.CL_SUCCESS)
            {
                throw new Exception(code.ToString("g"));
            }
        }
    }
}