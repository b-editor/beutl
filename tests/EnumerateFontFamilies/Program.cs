// See https://aka.ms/new-console-template for more information
using BeUtl.Media;

var start = DateTime.Now;
var families = FontManager.Instance.FontFamilies;

Console.WriteLine(DateTime.Now - start);

foreach (var item in families)
{
    Console.WriteLine("==================");
    Console.WriteLine(item.Name);
    Console.WriteLine("==================");

    foreach (var face in item.Typefaces)
    {
        Console.WriteLine($"{face.Weight:g}, {face.Style:g}");
    }

    Console.WriteLine();
}
