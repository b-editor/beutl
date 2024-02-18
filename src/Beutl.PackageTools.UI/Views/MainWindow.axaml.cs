using Avalonia.Controls;

using Beutl.PackageTools.UI.ViewModels;

using FluentAvalonia.UI.Controls;

namespace Beutl.PackageTools.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        BackRequested += (s, e) =>
        {
            e.Handled = true;
        };

        InitializeComponent();

        var viewmodel = new MainViewModel();

        DataContext = viewmodel;
        frame.NavigationPageFactory = new MyNavigationPageFactory();
        frame.NavigateFromObject(viewmodel);
    }

    public sealed class MyNavigationPageFactory : INavigationPageFactory
    {
        public Control GetPage(Type srcType)
        {
            return null!;
        }

        public Control GetPageFromObject(object target)
        {
            return target switch
            {
                MainViewModel => new DisplayPackagesPage() { DataContext = target },
                InstallViewModel => new InstallPage() { DataContext = target },
                UninstallViewModel => new UninstallPage() { DataContext = target },
                ResultViewModel => new ResultPage() { DataContext = target },
                CleanViewModel => new CleanPage() { DataContext = target },
                _ => null!
            };
        }
    }
}
