using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Compute.OpenCL;

namespace BEditor.Compute
{
    static class Extension
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