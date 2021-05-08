using System.Runtime.Serialization;

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