using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents the base class of the plugin settings.
    /// </summary>
    /// <example>
    /// public record CustomSetting(string value) : SettingRecord;
    ///
    /// public SettingRecord Settings { get; set; } = new CustomSetting("Sample text");
    /// </example>
    [DataContract]
    public record SettingRecord()
    {
    }
}
