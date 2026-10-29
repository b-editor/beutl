/*
UNICODE LICENSE V3

COPYRIGHT AND PERMISSION NOTICE

Copyright © 1991-2026 Unicode, Inc.

NOTICE TO USER: Carefully read the following legal agreement. BY
DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

Permission is hereby granted, free of charge, to any person obtaining a
copy of data files and any associated documentation (the "Data Files") or
software and any associated documentation (the "Software") to deal in the
Data Files or Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, and/or sell
copies of the Data Files or Software, and to permit persons to whom the
Data Files or Software are furnished to do so, provided that either (a)
this copyright and permission notice appear with all copies of the Data
Files or Software, or (b) this copyright and permission notice appear in
associated Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall
not be used in advertising or otherwise to promote the sale, use or other
dealings in these Data Files or Software without prior written
authorization of the copyright holder.
*/
// Derived from Unicode 17.0 VerticalOrientation.txt (UAX #50).
// https://www.unicode.org/Public/17.0.0/ucd/VerticalOrientation.txt
// Copyright Unicode, Inc. Distributed under https://www.unicode.org/license.txt.
using System.Globalization;
using System.Text;

namespace Beutl.Media.TextFormatting;

internal static class VerticalOrientation
{
    // 0: rotated, 1: upright (including Tu), 2: vertical alternate or rotated (Tr).
    public static int Get(ReadOnlySpan<char> cluster)
    {
        int orientation = 0;
        bool first = true;
        foreach (Rune rune in cluster.EnumerateRunes())
        {
            // UAX #50 section 3.2.1: an enclosing mark makes the entire cluster upright.
            // This includes keycap emoji, whose initial digit alone would be rotated.
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.EnclosingMark)
                return 1;
            if (first)
            {
                orientation = Get(rune.Value);
                first = false;
            }
        }
        return orientation;
    }

    private static int Get(int scalar)
    {
        int low = 0;
        int high = Ranges.Length - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            var range = Ranges[mid];
            if (scalar < range.Start) high = mid - 1;
            else if (scalar > range.End) low = mid + 1;
            else return range.Orientation;
        }
        return 0;
    }

    private static readonly (int Start, int End, int Orientation)[] Ranges =
    [
        (0xA7, 0xA7, 1),
        (0xA9, 0xA9, 1),
        (0xAE, 0xAE, 1),
        (0xB1, 0xB1, 1),
        (0xBC, 0xBE, 1),
        (0xD7, 0xD7, 1),
        (0xF7, 0xF7, 1),
        (0x2EA, 0x2EB, 1),
        (0x1100, 0x11FF, 1),
        (0x1401, 0x167F, 1),
        (0x18B0, 0x18FF, 1),
        (0x2016, 0x2016, 1),
        (0x2018, 0x2019, 2),
        (0x201C, 0x201D, 2),
        (0x2020, 0x2021, 1),
        (0x2030, 0x2031, 1),
        (0x203B, 0x203C, 1),
        (0x2042, 0x2042, 1),
        (0x2047, 0x2049, 1),
        (0x2051, 0x2051, 1),
        (0x2065, 0x2065, 1),
        (0x20DD, 0x20E0, 1),
        (0x20E2, 0x20E4, 1),
        (0x2100, 0x2101, 1),
        (0x2103, 0x2109, 1),
        (0x210F, 0x210F, 1),
        (0x2113, 0x2114, 1),
        (0x2116, 0x2117, 1),
        (0x211E, 0x2123, 1),
        (0x2125, 0x2125, 1),
        (0x2127, 0x2127, 1),
        (0x2129, 0x2129, 1),
        (0x212E, 0x212E, 1),
        (0x2135, 0x213F, 1),
        (0x2145, 0x214A, 1),
        (0x214C, 0x214D, 1),
        (0x214F, 0x2189, 1),
        (0x218C, 0x218F, 1),
        (0x221E, 0x221E, 1),
        (0x2234, 0x2235, 1),
        (0x2300, 0x2307, 1),
        (0x230C, 0x231F, 1),
        (0x2324, 0x2328, 1),
        (0x2329, 0x232A, 2),
        (0x232B, 0x232B, 1),
        (0x237D, 0x239A, 1),
        (0x23BE, 0x23CD, 1),
        (0x23CF, 0x23CF, 1),
        (0x23D1, 0x23DB, 1),
        (0x23E2, 0x2422, 1),
        (0x2424, 0x24FF, 1),
        (0x25A0, 0x2619, 1),
        (0x2620, 0x2767, 1),
        (0x2776, 0x2793, 1),
        (0x2B12, 0x2B2F, 1),
        (0x2B50, 0x2B59, 1),
        (0x2B97, 0x2B97, 1),
        (0x2BB8, 0x2BD1, 1),
        (0x2BD3, 0x2BEB, 1),
        (0x2BF0, 0x2BFF, 1),
        (0x2E50, 0x2E51, 1),
        (0x2E80, 0x3007, 1),
        (0x3008, 0x3011, 2),
        (0x3012, 0x3013, 1),
        (0x3014, 0x301F, 2),
        (0x3020, 0x302F, 1),
        (0x3030, 0x3030, 2),
        (0x3031, 0x309F, 1),
        (0x30A0, 0x30A0, 2),
        (0x30A1, 0x30FB, 1),
        (0x30FC, 0x30FC, 2),
        (0x30FD, 0xA4CF, 1),
        (0xA960, 0xA97F, 1),
        (0xAC00, 0xD7FF, 1),
        (0xE000, 0xFAFF, 1),
        (0xFE10, 0xFE1F, 1),
        (0xFE30, 0xFE48, 1),
        (0xFE50, 0xFE57, 1),
        (0xFE59, 0xFE5E, 2),
        (0xFE5F, 0xFE62, 1),
        (0xFE67, 0xFE6F, 1),
        (0xFF01, 0xFF07, 1),
        (0xFF08, 0xFF09, 2),
        (0xFF0A, 0xFF0C, 1),
        (0xFF0E, 0xFF19, 1),
        (0xFF1A, 0xFF1B, 2),
        (0xFF1F, 0xFF3A, 1),
        (0xFF3B, 0xFF3B, 2),
        (0xFF3C, 0xFF3C, 1),
        (0xFF3D, 0xFF3D, 2),
        (0xFF3E, 0xFF3E, 1),
        (0xFF3F, 0xFF3F, 2),
        (0xFF40, 0xFF5A, 1),
        (0xFF5B, 0xFF60, 2),
        (0xFFE0, 0xFFE2, 1),
        (0xFFE3, 0xFFE3, 2),
        (0xFFE4, 0xFFE7, 1),
        (0xFFF0, 0xFFF8, 1),
        (0xFFFC, 0xFFFD, 1),
        (0x10980, 0x1099F, 1),
        (0x11580, 0x115FF, 1),
        (0x11A00, 0x11ABF, 1),
        (0x13000, 0x1467F, 1),
        (0x16FE0, 0x18DFF, 1),
        (0x1AFF0, 0x1B2FF, 1),
        (0x1CEC0, 0x1CFCF, 1),
        (0x1D000, 0x1D1FF, 1),
        (0x1D2E0, 0x1D37F, 1),
        (0x1D800, 0x1DAAF, 1),
        (0x1F000, 0x1F7FF, 1),
        (0x1F900, 0x1FAFF, 1),
        (0x20000, 0x2FFFD, 1),
        (0x30000, 0x3FFFD, 1),
        (0xF0000, 0xFFFFD, 1),
        (0x100000, 0x10FFFD, 1),
    ];
}
