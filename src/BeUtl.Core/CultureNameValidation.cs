using System.Globalization;

namespace BeUtl;

public static class CultureNameValidation
{
    private static readonly CultureInfo[] s_cultures;

    static CultureNameValidation()
    {
        s_cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
    }

    public static bool IsValid(string name)
    {
        foreach (CultureInfo item in s_cultures)
        {
            if (item.Name == name)
            {
                return true;
            }
        }

        return false;
    }
}
