namespace Beutl.PackageTools;

public class KurukuruProgress : IProgress<double>
{
    private readonly Spinner _spinner;
    private readonly string _message;

    public KurukuruProgress(Spinner spinner, string message)
    {
        _spinner = spinner;
        _message = message;
    }

    public void Report(double value)
    {
        _spinner.Text = $"{_message} {value:P}";
    }
}
