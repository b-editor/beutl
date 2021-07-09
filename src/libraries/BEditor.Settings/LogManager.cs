
using System;

using Microsoft.Extensions.Logging;

namespace BEditor
{
    [Obsolete("Use ServicesLocator.")]
    public class LogManager
    {
        [Obsolete("Use ServicesLocator.Current.Logger")]
        public static ILogger? Logger
        {
            get => ServicesLocator.Current.Logger;
            set { }
        }
    }
}