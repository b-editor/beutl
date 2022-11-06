// See https://aka.ms/new-console-template for more information
using Beutl.Media;

DateTime start = DateTime.Now;
IEnumerable<FontFamily> families = FontManager.Instance.FontFamilies;

Console.WriteLine(DateTime.Now - start);

foreach (FontFamily item in families)
{
    Console.WriteLine("==================");
    Console.WriteLine(item.Name);
    Console.WriteLine("==================");

    foreach (Typeface face in item.Typefaces)
    {
        Console.WriteLine($"{face.Weight:g}, {face.Style:g}");
    }

    Console.WriteLine();
}
