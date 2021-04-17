using System.Threading;

using BEditor.Data;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BEditor
{
    public class App : IApplication
    {
        public static readonly App Current = new();

        public Status AppStatus { get; set; }
        public IServiceCollection Services { get; } = new ServiceCollection();
        public ILoggerFactory LoggingFactory { get; } = new LoggerFactory();
#nullable disable
        public SynchronizationContext UIThread { get; set; }
#nullable enable

        public void RestoreAppConfig(Project project, string directory)
        {

        }
        public void SaveAppConfig(Project project, string directory)
        {

        }
    }
}