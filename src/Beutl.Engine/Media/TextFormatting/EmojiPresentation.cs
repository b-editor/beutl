using System.Text;

namespace Beutl.Media.TextFormatting;

internal static class EmojiPresentation
{
    public static bool IsEmoji(ReadOnlySpan<char> cluster)
    {
        if (cluster.Contains('\uFE0E'))
            return false;
        // Keycap bases only become emoji with the enclosing keycap, not a lone VS16.
        if (cluster.Contains('\u20E3') || cluster.Contains('\u200D')
            || (cluster.Contains('\uFE0F') && cluster[0] is not (>= '0' and <= '9' or '#' or '*')))
            return true;
        foreach (Rune rune in cluster.EnumerateRunes())
        {
            if (IsDefaultEmoji(rune.Value))
                return true;
        }
        return false;
    }

    // Coalesced Emoji_Presentation ranges from Unicode 17.0:
    // https://www.unicode.org/Public/17.0.0/ucd/emoji/emoji-data.txt
    // Covered by the Unicode license in THIRD_PARTY_NOTICES.md.
    private static bool IsDefaultEmoji(int value) => value is
        >= 0x231A and <= 0x231B or
        >= 0x23E9 and <= 0x23EC or
        0x23F0 or
        0x23F3 or
        >= 0x25FD and <= 0x25FE or
        >= 0x2614 and <= 0x2615 or
        >= 0x2648 and <= 0x2653 or
        0x267F or
        0x2693 or
        0x26A1 or
        >= 0x26AA and <= 0x26AB or
        >= 0x26BD and <= 0x26BE or
        >= 0x26C4 and <= 0x26C5 or
        0x26CE or
        0x26D4 or
        0x26EA or
        >= 0x26F2 and <= 0x26F3 or
        0x26F5 or
        0x26FA or
        0x26FD or
        0x2705 or
        >= 0x270A and <= 0x270B or
        0x2728 or
        0x274C or
        0x274E or
        >= 0x2753 and <= 0x2755 or
        0x2757 or
        >= 0x2795 and <= 0x2797 or
        0x27B0 or
        0x27BF or
        >= 0x2B1B and <= 0x2B1C or
        0x2B50 or
        0x2B55 or
        0x1F004 or
        0x1F0CF or
        0x1F18E or
        >= 0x1F191 and <= 0x1F19A or
        >= 0x1F1E6 and <= 0x1F1FF or
        0x1F201 or
        0x1F21A or
        0x1F22F or
        >= 0x1F232 and <= 0x1F236 or
        >= 0x1F238 and <= 0x1F23A or
        >= 0x1F250 and <= 0x1F251 or
        >= 0x1F300 and <= 0x1F320 or
        >= 0x1F32D and <= 0x1F335 or
        >= 0x1F337 and <= 0x1F37C or
        >= 0x1F37E and <= 0x1F393 or
        >= 0x1F3A0 and <= 0x1F3CA or
        >= 0x1F3CF and <= 0x1F3D3 or
        >= 0x1F3E0 and <= 0x1F3F0 or
        0x1F3F4 or
        >= 0x1F3F8 and <= 0x1F43E or
        0x1F440 or
        >= 0x1F442 and <= 0x1F4FC or
        >= 0x1F4FF and <= 0x1F53D or
        >= 0x1F54B and <= 0x1F54E or
        >= 0x1F550 and <= 0x1F567 or
        0x1F57A or
        >= 0x1F595 and <= 0x1F596 or
        0x1F5A4 or
        >= 0x1F5FB and <= 0x1F64F or
        >= 0x1F680 and <= 0x1F6C5 or
        0x1F6CC or
        >= 0x1F6D0 and <= 0x1F6D2 or
        >= 0x1F6D5 and <= 0x1F6D8 or
        >= 0x1F6DC and <= 0x1F6DF or
        >= 0x1F6EB and <= 0x1F6EC or
        >= 0x1F6F4 and <= 0x1F6FC or
        >= 0x1F7E0 and <= 0x1F7EB or
        0x1F7F0 or
        >= 0x1F90C and <= 0x1F93A or
        >= 0x1F93C and <= 0x1F945 or
        >= 0x1F947 and <= 0x1F9FF or
        >= 0x1FA70 and <= 0x1FA7C or
        >= 0x1FA80 and <= 0x1FA8A or
        >= 0x1FA8E and <= 0x1FAC6 or
        0x1FAC8 or
        >= 0x1FACD and <= 0x1FADC or
        >= 0x1FADF and <= 0x1FAEA or
        >= 0x1FAEF and <= 0x1FAF8;
}
