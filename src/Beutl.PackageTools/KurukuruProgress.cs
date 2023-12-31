namespace Beutl.PackageTools;

public class KurukuruProgress(Spinner spinner, string message) : IProgress<double>
{
    public void Report(double value)
    {
        spinner.Text = $"{message} {value:P}";
    }
}
