using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BEditor.Views.SettingsControl
{
    /// <summary>
    /// AppInfo.xaml の相互作用ロジック
    /// </summary>
    public partial class AppInfo : UserControl
    {
        public AppInfo()
        {
            InitializeComponent();
            version.DataContext = new AppInfoViewModel();
        }

        public class AppInfoViewModel
        {
            public AppInfoViewModel()
            {
                var assembly = typeof(AppInfoViewModel).Assembly;
                var name = assembly.GetName();
                var a = $"{name.Name} - {name.Version}";

                Versions = assembly.GetReferencedAssemblies().Select(name => $"{name.Name} - {name.Version}").Append(a).OrderBy(a => a);
            }

            public IEnumerable<string> Versions { get; }
        }
    }
}
