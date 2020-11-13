using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BEditor.Core.Native
{
    internal static class TextConvert
    {
        internal static int UTF8Size(string str)
        {
            Debug.Assert(str != null);
            return (str.Length * 4) + 1;
        }
        internal static int UTF8SizeNullable(string str)
        {
            return str != null ? (str.Length * 4) + 1 : 0;
        }
        internal static unsafe byte* UTF8Encode(string str, byte* buffer, int bufferSize)
        {
            Debug.Assert(str != null);
            fixed (char* strPtr = str)
            {
                Encoding.UTF8.GetBytes(strPtr, str.Length + 1, buffer, bufferSize);
            }
            return buffer;
        }
        internal static unsafe byte* UTF8EncodeNullable(string str, byte* buffer, int bufferSize)
        {
            if (str == null)
            {
                return (byte*)0;
            }
            fixed (char* strPtr = str)
            {
                Encoding.UTF8.GetBytes(strPtr, str.Length + 1, buffer, bufferSize);
            }
            return buffer;
        }

        internal static unsafe byte* UTF8Encode(string str)
        {
            Debug.Assert(str != null);
            int bufferSize = UTF8Size(str);
            byte* buffer = (byte*)Marshal.AllocHGlobal(bufferSize);
            fixed (char* strPtr = str)
            {
                Encoding.UTF8.GetBytes(strPtr, str.Length + 1, buffer, bufferSize);
            }
            return buffer;
        }
        internal static unsafe byte* UTF8EncodeNullable(string str)
        {
            if (str == null)
            {
                return (byte*)0;
            }
            int bufferSize = UTF8Size(str);
            byte* buffer = (byte*)Marshal.AllocHGlobal(bufferSize);
            fixed (char* strPtr = str)
            {
                Encoding.UTF8.GetBytes(
                    strPtr,
                    (str != null) ? (str.Length + 1) : 0,
                    buffer,
                    bufferSize
                );
            }
            return buffer;
        }

        internal static unsafe string UTF8_ToManaged(IntPtr s)
        {
            if (s == IntPtr.Zero)
            {
                return null;
            }

            /* We get to do strlen ourselves! */
            byte* ptr = (byte*)s;
            while (*ptr != 0)
            {
                ptr++;
            }

#if NETSTANDARD2_0
			/* Modern C# lets you just send the byte*, nice! */
			string result = System.Text.Encoding.UTF8.GetString(
				(byte*) s,
				(int) (ptr - (byte*) s)
			);
#else
            /* Old C# requires an extra memcpy, bleh! */
            int len = (int)(ptr - (byte*)s);
            if (len == 0)
            {
                return string.Empty;
            }
            char* chars = stackalloc char[len];
            int strLen = Encoding.UTF8.GetChars((byte*)s, len, chars, len);
            string result = new string(chars, 0, strLen);
#endif

            return result;
        }
    }
}
