using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

using BEditor.Data;
using BEditor.LangResources;

namespace BEditor
{
    public sealed class SupportedLanguage : EditingObject, IEquatable<SupportedLanguage?>
    {
        public static readonly EditingProperty<CultureInfo> CultureProperty
            = EditingProperty.Register<CultureInfo, SupportedLanguage>(
                nameof(Culture),
                EditingPropertyOptions<CultureInfo>.Create().Serialize(Write_Culture, Read_Culture));
        
        public static readonly EditingProperty<string> DisplayNameProperty
            = EditingProperty.Register<string, SupportedLanguage>(
                nameof(DisplayName),
                EditingPropertyOptions<string>.Create()!.Serialize()!);
        
        public static readonly EditingProperty<string> BaseNameProperty
            = EditingProperty.Register<string, SupportedLanguage>(
                nameof(BaseName),
                EditingPropertyOptions<string>.Create()!.Serialize()!);
        
        public static readonly EditingProperty<Assembly> AssemblyProperty
            = EditingProperty.Register<Assembly, SupportedLanguage>(
                nameof(Assembly),
                EditingPropertyOptions<Assembly>.Create()!.Serialize(Write_Assembly, Read_Assembly)!);

        public SupportedLanguage(CultureInfo culture, string displayName, string baseName, Assembly assembly)
        {
            Culture = culture;
            DisplayName = displayName;
            BaseName = baseName;
            Assembly = assembly;
        }

        public static SupportedLanguage Default { get; } = new(CultureInfo.InvariantCulture, string.Empty, "BEditor.LangResources.Strings", typeof(Strings).Assembly);

        public CultureInfo Culture
        {
            get => GetValue(CultureProperty);
            private set => SetValue(CultureProperty, value);
        }
        
        public string DisplayName
        {
            get => GetValue(DisplayNameProperty);
            private set => SetValue(DisplayNameProperty, value);
        }
        
        public string BaseName
        {
            get => GetValue(BaseNameProperty);
            private set => SetValue(BaseNameProperty, value);
        }
        
        public Assembly Assembly
        {
            get => GetValue(AssemblyProperty);
            private set => SetValue(AssemblyProperty, value);
        }

        private static Assembly Read_Assembly(DeserializeContext arg)
        {
            var name = arg.Element.GetString()!;
            return Assembly.LoadFrom(name);
        }

        private static void Write_Assembly(Utf8JsonWriter arg1, Assembly arg2)
        {
            arg1.WriteStringValue(arg2.Location);
        }

        private static CultureInfo Read_Culture(DeserializeContext arg)
        {
            var name = arg.Element.GetString();
            if (name == null)
                return CultureInfo.CurrentCulture;

            return new CultureInfo(name);
        }

        private static void Write_Culture(Utf8JsonWriter arg1, CultureInfo arg2)
        {
            arg1.WriteStringValue(arg2.Name);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as SupportedLanguage);
        }

        public bool Equals(SupportedLanguage? other)
        {
            return other != null &&
                   EqualityComparer<CultureInfo>.Default.Equals(Culture, other.Culture) &&
                   BaseName == other.BaseName &&
                   EqualityComparer<Assembly>.Default.Equals(Assembly, other.Assembly);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Culture, BaseName, Assembly);
        }

        public static bool operator ==(SupportedLanguage? left, SupportedLanguage? right)
        {
            return EqualityComparer<SupportedLanguage>.Default.Equals(left, right);
        }

        public static bool operator !=(SupportedLanguage? left, SupportedLanguage? right)
        {
            return !(left == right);
        }
    }
}