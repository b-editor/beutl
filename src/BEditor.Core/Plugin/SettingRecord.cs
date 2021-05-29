// SettingRecord.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Runtime.Serialization;

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents the base class of the plugin settings.
    /// </summary>
    /// <example>
    /// public record CustomSetting(string value) : SettingRecord;
    ///
    /// public SettingRecord Settings { get; set; } = new CustomSetting("Sample text");.
    /// </example>
    [DataContract]
    public record SettingRecord()
    {
    }
}