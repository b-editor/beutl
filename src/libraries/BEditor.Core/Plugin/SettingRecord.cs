// SettingRecord.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

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
    public record SettingRecord()
    {
        /// <summary>
        /// Saves this configuration to the specified file.
        /// </summary>
        /// <param name="filename">The file to save.</param>
        /// <returns>Returns <see langword="true"/> if the save was successful, <see langword="false"/> otherwise.</returns>
        public bool Save(string filename)
        {
            try
            {
                using var stream = new FileStream(filename, FileMode.Create);
                using var writer = new Utf8JsonWriter(stream, Serialize._options);

                writer.WriteStartObject();

                foreach (var (param, prop) in GetSerializable(GetType()))
                {
                    var value = prop.GetValue(this);
                    if (value is null) continue;
                    WriteSerializable(writer, param.Name!, value);
                }

                writer.WriteEndObject();
                writer.Flush();
                return true;
            }
            catch (Exception e)
            {
                LogManager.Logger.LogError(e, "Failed to save {0}.", GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// Saves this configuration to the specified file.
        /// </summary>
        /// <param name="filename">The file to save.</param>
        /// <returns>Returns <see langword="true"/> if the save was successful, <see langword="false"/> otherwise.</returns>
        public async ValueTask<bool> SaveAsync(string filename)
        {
            try
            {
                await using var stream = new FileStream(filename, FileMode.Create);
                await using var writer = new Utf8JsonWriter(stream, Serialize._options);

                writer.WriteStartObject();

                foreach (var (param, prop) in GetSerializable(GetType()))
                {
                    var value = prop.GetValue(this);
                    if (value is null) continue;
                    WriteSerializable(writer, param.Name!, value);
                }

                writer.WriteEndObject();
                await writer.FlushAsync();
                return true;
            }
            catch (Exception e)
            {
                LogManager.Logger.LogError(e, "Failed to save {0}.", GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// Loads the settings from a file.
        /// </summary>
        /// <typeparam name="T">Type of the object to be loaded.</typeparam>
        /// <param name="filename">The name of the file to load.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static T? LoadFrom<T>(string filename)
            where T : SettingRecord
        {
            if (!File.Exists(filename)) return default;
            try
            {
                using var stream = new FileStream(filename, FileMode.Open);

                using var doc = JsonDocument.Parse(stream);
                var type = typeof(T);
                var args = new List<object>();

                foreach (var (param, prop) in GetSerializable(type))
                {
                    if (doc.RootElement.TryGetProperty(param.Name!, out var json))
                    {
                        var value = ReadSerializable(json, param.ParameterType);
                        args.Add(value);
                    }
                    else
                    {
                        args.Add(GetDefault(type));
                    }
                }

                return (T)Activator.CreateInstance(type, BindingFlags.CreateInstance, null, args.ToArray(), null)!;
            }
            catch (Exception e)
            {
                LogManager.Logger.LogError(e, "Failed to load {0}.", filename);
                return default;
            }
        }

        /// <summary>
        /// Loads the settings from a file.
        /// </summary>
        /// <typeparam name="T">Type of the object to be loaded.</typeparam>
        /// <param name="filename">The name of the file to load.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static async ValueTask<T?> LoadFromAsync<T>(string filename)
            where T : SettingRecord
        {
            if (!File.Exists(filename)) return default;
            try
            {
                await using var stream = new FileStream(filename, FileMode.Open);

                using var doc = await JsonDocument.ParseAsync(stream);
                var type = typeof(T);
                var args = new List<object>();

                foreach (var (param, prop) in GetSerializable(type))
                {
                    if (doc.RootElement.TryGetProperty(param.Name!, out var json))
                    {
                        var value = ReadSerializable(json, param.ParameterType);
                        args.Add(value);
                    }
                    else
                    {
                        args.Add(GetDefault(type));
                    }
                }

                return (T)Activator.CreateInstance(type, BindingFlags.CreateInstance, null, args.ToArray(), null)!;
            }
            catch (Exception e)
            {
                LogManager.Logger.LogError(e, "Failed to load {0}.", filename);
                return default;
            }
        }

        private static IEnumerable<(ParameterInfo Parameter, PropertyInfo Property)> GetSerializable(Type type)
        {
            var constructor = type.GetConstructors()[0];
            foreach (var param in constructor.GetParameters())
            {
                var name = param.Name;

                if (name is not null && type.GetProperty(name) is var prop && prop is not null && IsSerializable(prop.PropertyType))
                {
                    yield return (param, prop);
                }
            }
        }

        private static void WriteSerializable(Utf8JsonWriter writer, string name, object obj)
        {
            if (obj is bool @bool) writer.WriteBoolean(name, @bool);
            else if (obj is byte @byte) writer.WriteNumber(name, @byte);
            else if (obj is sbyte @sbyte) writer.WriteNumber(name, @sbyte);
            else if (obj is short @short) writer.WriteNumber(name, @short);
            else if (obj is ushort @ushort) writer.WriteNumber(name, @ushort);
            else if (obj is int @int) writer.WriteNumber(name, @int);
            else if (obj is uint @uint) writer.WriteNumber(name, @uint);
            else if (obj is long @long) writer.WriteNumber(name, @long);
            else if (obj is ulong @ulong) writer.WriteNumber(name, @ulong);
            else if (obj is char @char) writer.WriteString(name, @char.ToString());
            else if (obj is double @double) writer.WriteNumber(name, @double);
            else if (obj is float @float) writer.WriteNumber(name, @float);
            else if (obj is string @string) writer.WriteString(name, @string);
            else if (obj is DateTime dateTime) writer.WriteString(name, dateTime);
            else if (obj is DateTimeOffset dateTimeOffset) writer.WriteString(name, dateTimeOffset);
            else if (obj is Guid guid) writer.WriteString(name, guid);
            else if (obj is Enum @enum) writer.WriteNumber(name, ((IConvertible)@enum).ToInt32(CultureInfo.InvariantCulture));
        }

        private static object ReadSerializable(JsonElement element, Type type)
        {
            if (type == typeof(bool)) return element.GetBoolean();
            else if (type == typeof(byte)) return element.GetByte();
            else if (type == typeof(sbyte)) return element.GetSByte();
            else if (type == typeof(short)) return element.GetInt16();
            else if (type == typeof(ushort)) return element.GetUInt16();
            else if (type == typeof(int)) return element.GetInt32();
            else if (type == typeof(uint)) return element.GetUInt32();
            else if (type == typeof(long)) return element.GetInt64();
            else if (type == typeof(ulong)) return element.GetUInt64();
            else if (type == typeof(char)) return element.GetString()?.First() ?? '\u200B';
            else if (type == typeof(double)) return element.GetDouble();
            else if (type == typeof(float)) return element.GetSingle();
            else if (type == typeof(string)) return element.GetString() ?? string.Empty;
            else if (type == typeof(DateTime)) return element.GetDateTime();
            else if (type == typeof(DateTimeOffset)) return element.GetDateTimeOffset();
            else if (type == typeof(Guid)) return element.GetGuid();
            else if (type.IsEnum) return Enum.ToObject(type, element.GetInt32());
            else throw new NotSupportedException();
        }

        private static object GetDefault(Type type)
        {
            if (type == typeof(bool)) return false;
            else if (type == typeof(byte)) return (byte)0;
            else if (type == typeof(sbyte)) return (sbyte)0;
            else if (type == typeof(short)) return (short)0;
            else if (type == typeof(ushort)) return (ushort)0;
            else if (type == typeof(int)) return 0;
            else if (type == typeof(uint)) return 0U;
            else if (type == typeof(long)) return 0L;
            else if (type == typeof(ulong)) return 0UL;
            else if (type == typeof(char)) return '\u200B';
            else if (type == typeof(double)) return 0D;
            else if (type == typeof(float)) return 0F;
            else if (type == typeof(string)) return string.Empty;
            else if (type == typeof(DateTime)) return DateTime.Now;
            else if (type == typeof(DateTimeOffset)) return DateTimeOffset.Now;
            else if (type == typeof(Guid)) return Guid.Empty;
            else if (type.IsEnum) return type.GetEnumValues().GetValue(0)!;
            else throw new NotSupportedException();
        }

        private static bool IsSerializable(Type type)
        {
            if (type == typeof(bool)) return true;
            else if (type == typeof(byte)) return true;
            else if (type == typeof(sbyte)) return true;
            else if (type == typeof(short)) return true;
            else if (type == typeof(ushort)) return true;
            else if (type == typeof(int)) return true;
            else if (type == typeof(uint)) return true;
            else if (type == typeof(long)) return true;
            else if (type == typeof(ulong)) return true;
            else if (type == typeof(char)) return true;
            else if (type == typeof(double)) return true;
            else if (type == typeof(float)) return true;
            else if (type == typeof(string)) return true;
            else if (type == typeof(DateTime)) return true;
            else if (type == typeof(DateTimeOffset)) return true;
            else if (type == typeof(Guid)) return true;
            else if (type.IsEnum) return true;
            else return false;
        }
    }
}