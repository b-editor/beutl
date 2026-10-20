using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beutl.Animation;
using Beutl.Api.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Media.Decoding;
using Beutl.Serialization;
using Beutl.ViewModels.Dock;

namespace Beutl.HeadlessUITests;

// Builds FormatManifest.txt: the names that first-party code writes into project files and the app's stores, and
// resolves by exact match when it reads them back. FormatManifestTests compares it with the checked-in file, so a
// rename, or a change to what a type writes, shows up in the diff of the pull request that makes it (#2875).
internal static partial class FormatManifest
{
    // Every project under src/ that the manifest reads, with the name of the assembly the app loads. The Windows app
    // builds the Media Foundation decoder in under its Embedding name, and this test project builds it the same way.
    public static readonly IReadOnlyDictionary<string, string> ScannedProjects = new Dictionary<string, string>
    {
        ["Beutl"] = "Beutl",
        ["Beutl.AgentToolkit"] = "Beutl.AgentToolkit",
        ["Beutl.Api"] = "Beutl.Api",
        ["Beutl.Configuration"] = "Beutl.Configuration",
        ["Beutl.Controls"] = "Beutl.Controls",
        ["Beutl.Core"] = "Beutl.Core",
        ["Beutl.Editor"] = "Beutl.Editor",
        ["Beutl.Editor.Components"] = "Beutl.Editor.Components",
        ["Beutl.Engine"] = "Beutl.Engine",
        ["Beutl.ExceptionHandler"] = "Beutl.ExceptionHandler",
        ["Beutl.Extensibility"] = "Beutl.Extensibility",
        ["Beutl.Extensions.AVFoundation"] = "Beutl.Extensions.AVFoundation",
        ["Beutl.Extensions.FFmpeg"] = "Beutl.Extensions.FFmpeg",
        ["Beutl.Extensions.FFmpeg.Core"] = "Beutl.Extensions.FFmpeg.Core",
        ["Beutl.Extensions.MediaFoundation"] = "Beutl.Embedding.MediaFoundation",
        ["Beutl.FFmpegIpc"] = "Beutl.FFmpegIpc",
        ["Beutl.Language"] = "Beutl.Language",
        ["Beutl.NodeGraph"] = "Beutl.NodeGraph",
        ["Beutl.PackageTools.UI"] = "Beutl.PackageTools.UI",
        ["Beutl.ProjectSystem"] = "Beutl.ProjectSystem",
        ["Beutl.Threading"] = "Beutl.Threading",
        ["Beutl.Utilities"] = "Beutl.Utilities",
    };

    // The projects under src/ that it leaves out, and why.
    public static readonly IReadOnlyDictionary<string, string> SkippedProjects = new Dictionary<string, string>
    {
        ["Beutl.AgentToolkit.Mcp"] =
            "the stdio MCP host of Beutl.AgentToolkit, which is read; it declares no serializable types and no store",
        ["Beutl.Engine.SourceGenerators"] = "a Roslyn analyzer, which runs in the compiler rather than in the app",
        ["Beutl.FFmpegWorker"] =
            "the GPL worker process, which MIT test projects must not reference; it declares no serializable types",
    };

    private const string Header =
        """
        # Beutl format manifest
        #
        # The names that first-party code writes into project files and the app's stores, and resolves by exact
        # match when it reads them back. A change here is a change to what Beutl writes: what was saved under an
        # old name no longer resolves after a rename, and an older build cannot read what a newer one writes (#2875).
        #
        # FormatManifestTests builds this file from the assemblies built from src/ and fails when it differs. After
        # an intentional change, regenerate it and commit it with the change:
        #
        #   BEUTL_UPDATE_FORMAT_MANIFEST=1 dotnet test tests/Beutl.HeadlessUITests/Beutl.HeadlessUITests.csproj -f net10.0 --filter "FullyQualifiedName~FormatManifestTests"
        """;

    public static string Build()
    {
        // TypeNameFormatter compares namespaces in the current culture. Both cultures are pinned so that nothing here
        // can vary by machine.
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            Assembly[] assemblies = ScannedProjects.Values
                .Order(StringComparer.Ordinal)
                .Select(Assembly.Load)
                .ToArray();
            Type[] types = assemblies.SelectMany(assembly => assembly.GetTypes()).ToArray();

            var text = new ManifestText();
            text.Raw(Header);
            text.Section(
                "Assemblies",
                "The assemblies built from src/ that this manifest reads. FormatManifest.SkippedProjects says why the",
                "other projects under src/ are left out.");
            foreach (Assembly assembly in assemblies)
            {
                text.Line(assembly.GetName().Name!);
            }

            WriteSerializableTypes(text, types);
            WriteExtensionNames(text, types);
            return text.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static void WriteSerializableTypes(ManifestText text, Type[] types)
    {
        text.Section(
            "Serializable types",
            "Every ICoreSerializable class in these assemblies, by the $type that project files (.bep, .scene, .belm),",
            "settings.json, extensionsSettings.json, output profiles and presets, templates and library pins write",
            "for it. Closed generic types are listed where a default instance writes them: the KeyFrameAnimation<T>",
            "and KeyFrame<T> of each animatable property, and those nested in the saved instance, such as node ports.",
            "A generic definition, which TypeNameFormatter cannot format, is listed with its parameter names.",
            "  sealed    A member declared as this type is written without $type and read back as this type.",
            "  unsealed  A member declared as this type is written with $type.",
            "  abstract  The same as unsealed. There is no default instance.",
            "  keys      The top-level keys of a default instance saved as the root of a file.");

        // A file location that is never created. A type saved as a file of its own, such as a scene, cannot be saved
        // without one, but nothing is written to it.
        string missingDirectory = Path.Combine(Path.GetTempPath(), $"beutl-format-manifest-{Guid.NewGuid():N}");
        var entries = new SortedDictionary<string, (Type Type, string Kind, string? Keys)>(StringComparer.Ordinal);
        var pending = new Queue<Type>(types.Where(IsSerializableClass));
        while (pending.TryDequeue(out Type? type))
        {
            string discriminator = Discriminator(type);
            if (entries.TryGetValue(discriminator, out (Type Type, string Kind, string? Keys) existing))
            {
                if (existing.Type != type)
                {
                    throw new InvalidOperationException(
                        $"{existing.Type} and {type} are both written as {discriminator}.");
                }

                continue;
            }

            string kind = type.IsAbstract ? "abstract" : type.IsSealed ? "sealed" : "unsealed";
            string? keys = DescribeDefaultInstance(type, missingDirectory, out Type[] written);
            entries.Add(discriminator, (type, kind, keys));
            foreach (Type closed in written)
            {
                pending.Enqueue(closed);
            }
        }

        foreach ((string discriminator, (Type _, string kind, string? keys)) in entries)
        {
            text.Line(discriminator);
            text.Line($"  {kind}");
            if (keys is not null)
                text.Line($"  keys: {keys}");
        }
    }

    private static bool IsSerializableClass(Type type)
    {
        return type.IsClass
               && typeof(ICoreSerializable).IsAssignableFrom(type)
               && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
    }

    // The top-level keys of a default instance saved as the root of a file, or why there is no default instance, and
    // the closed generic types the instance writes. Null for an abstract type or a generic definition.
    private static string? DescribeDefaultInstance(Type type, string missingDirectory, out Type[] written)
    {
        written = [];
        if (type.IsAbstract || type.ContainsGenericParameters)
            return null;
        if (type.GetConstructor(Type.EmptyTypes) is null)
            return "(no public parameterless constructor)";

        object instance;
        try
        {
            instance = Activator.CreateInstance(type)!;
        }
        catch (TargetInvocationException ex)
        {
            return $"(the constructor throws {ex.InnerException!.GetType().Name})";
        }

        try
        {
            if (instance is CoreObject coreObject)
            {
                coreObject.Uri = new Uri(Path.Combine(missingDirectory, $"{type.Name}.json"));
            }

            JsonObject json;
            try
            {
                json = CoreSerializer.SerializeToJsonObject((ICoreSerializable)instance);
            }
            catch (Exception ex)
            {
                return $"(saving throws {ex.GetType().Name})";
            }

            written = [.. AnimationTypes(instance), .. NestedGenericTypes(json, root: true)];
            string[] keys = json
                .Select(property => property.Key)
                .Where(key => key != "$type")
                .Order(StringComparer.Ordinal)
                .ToArray();
            return keys.Length == 0 ? "(none)" : string.Join(", ", keys);
        }
        finally
        {
            (instance as IDisposable)?.Dispose();
        }
    }

    // EngineObject writes the animation of an animatable property as a KeyFrameAnimation<T> of KeyFrame<T>, where T
    // is the type of the property.
    private static IEnumerable<Type> AnimationTypes(object instance)
    {
        if (instance is not EngineObject engineObject)
            yield break;

        foreach (IProperty property in engineObject.Properties)
        {
            if (!property.IsAnimatable
                || property.GetAttributes()?.Any(attribute => attribute is NotAutoSerializedAttribute) == true)
            {
                continue;
            }

            yield return typeof(KeyFrameAnimation<>).MakeGenericType(property.ValueType);
            yield return typeof(KeyFrame<>).MakeGenericType(property.ValueType);
        }
    }

    // The closed generic types written below the root of a saved instance, such as the ports of a node.
    private static IEnumerable<Type> NestedGenericTypes(JsonNode? node, bool root)
    {
        switch (node)
        {
            case JsonObject obj:
                if (!root
                    && obj.TryGetDiscriminator(out Type? type)
                    && type.IsConstructedGenericType
                    && typeof(ICoreSerializable).IsAssignableFrom(type))
                {
                    yield return type;
                }

                foreach ((string _, JsonNode? value) in obj)
                {
                    foreach (Type nested in NestedGenericTypes(value, root: false))
                        yield return nested;
                }

                break;
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    foreach (Type nested in NestedGenericTypes(item, root: false))
                        yield return nested;
                }

                break;
        }
    }

    private static void WriteExtensionNames(ManifestText text, Type[] types)
    {
        Type[] classes = types
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .ToArray();
        Dictionary<Type, Extension?> extensions = classes
            .Where(typeof(Extension).IsAssignableFrom)
            .ToDictionary(type => type, CreateExtension);

        text.Section(
            "Tool tabs",
            "Every ToolTabExtension, by the $type a dock layout writes for it (tools[].extension in the view state of",
            "a scene and in dock-layout-presets.json), which is also the key that pins it (View.PinnedToolTabs in",
            "settings.json). The id is the tools[].id that BeutlToolDockable.CreateId gives it, with <guid> in place",
            "of a GUID it generates.");
        var toolTabs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((Type type, Extension? extension) in extensions)
        {
            if (!typeof(ToolTabExtension).IsAssignableFrom(type))
                continue;

            string discriminator = Discriminator(type);
            if (extension is not ToolTabExtension toolTab)
            {
                toolTabs.Add(discriminator, "(no instance)");
                continue;
            }

            if (NewToolTabDockable.GetPinKey(toolTab) != discriminator)
                throw new InvalidOperationException($"The pin key of {type} is no longer its $type.");

            toolTabs.Add(discriminator, DockId(toolTab));
        }

        foreach ((string discriminator, string id) in toolTabs)
        {
            text.Line(discriminator);
            text.Line($"  id: {id}");
        }

        text.Section(
            "Keymap",
            "keymap.json: the id of every context command of every ViewExtension.");
        var keymap = new List<string>();
        foreach (ViewExtension extension in extensions.Values.OfType<ViewExtension>())
        {
            foreach (ContextCommandDefinition definition in extension.ContextCommands)
            {
                keymap.Add(definition.Id);
            }
        }

        WriteLines(text, keymap);

        text.Section(
            "Decoders",
            "Extension.DecoderPriority in settings.json: the $type of every IDecoderInfo.");
        WriteLines(text, classes.Where(typeof(IDecoderInfo).IsAssignableFrom).Select(Discriminator));

        text.Section(
            "Editors",
            "Extension.EditorExtensions in settings.json: the $type of every EditorExtension.");
        WriteLines(text, classes.Where(typeof(EditorExtension).IsAssignableFrom).Select(Discriminator));

        text.Section(
            "Outputs",
            "Extension in output-profile.json and output-presets.json: the $type of every OutputExtension.");
        WriteLines(text, classes.Where(typeof(OutputExtension).IsAssignableFrom).Select(Discriminator));

        text.Section(
            "Encoders",
            "SelectedEncoder in output-profile.json and output-presets.json: the $type of every",
            "ControllableEncodingExtension.");
        WriteLines(text, classes.Where(typeof(ControllableEncodingExtension).IsAssignableFrom).Select(Discriminator));

        text.Section(
            "Extension settings",
            "extensionsSettings.json: the full name of every extension that has settings, which keys them, and the",
            "$type of the settings.");
        var settings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((Type type, Extension? extension) in extensions)
        {
            if (extension?.Settings is { } extensionSettings)
            {
                settings.Add(type.FullName!, Discriminator(extensionSettings.GetType()));
            }
        }

        foreach ((string name, string settingsType) in settings)
        {
            text.Line(name);
            text.Line($"  settings: {settingsType}");
        }

        text.Section(
            "Extensions without an instance",
            "Extensions with neither a static Instance nor a public parameterless constructor. Their context commands",
            "and settings are not listed above.");
        WriteLines(text, extensions.Where(pair => pair.Value is null).Select(pair => pair.Key.FullName!));
    }

    // The id BeutlToolDockable gives a new tool, which a dock layout saves as tools[].id. A tool that can be open more
    // than once gets a new GUID each time, so the GUID is replaced, and nothing else.
    private static string DockId(ToolTabExtension extension)
        => GeneratedGuid().Replace(BeutlToolDockable.CreateId(extension), "<guid>");

    [GeneratedRegex("[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}")]
    private static partial Regex GeneratedGuid();

    // The instance the app registers: the extension's static Instance, or one made with its public parameterless
    // constructor, as the media backends built into the app are.
    private static Extension? CreateExtension(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        object? instance = type.GetField("Instance", flags)?.GetValue(null)
                           ?? type.GetProperty("Instance", flags)?.GetValue(null);
        if (instance is Extension extension && extension.GetType() == type)
            return extension;

        return type.GetConstructor(Type.EmptyTypes) is null ? null : (Extension)Activator.CreateInstance(type)!;
    }

    private static void WriteLines(ManifestText text, IEnumerable<string> lines)
    {
        foreach (string line in lines.Distinct().Order(StringComparer.Ordinal))
        {
            text.Line(line);
        }
    }

    // The $type JsonHelper.WriteDiscriminator writes. TypeNameFormatter cannot format an open generic type (it
    // recurses into the declaring type of each generic parameter), so a generic definition is written by the same
    // rules with its parameter names. Those rules are checked against TypeNameFormatter for every other type here.
    private static string Discriminator(Type type)
    {
        if (type.ContainsGenericParameters)
            return FormatName(type);

        var node = new JsonObject();
        node.WriteDiscriminator(type);
        string discriminator = node["$type"]!.GetValue<string>();
        if (FormatName(type) != discriminator)
        {
            throw new InvalidOperationException(
                $"FormatManifest.FormatName no longer follows TypeNameFormatter for {type}: "
                + $"'{FormatName(type)}' instead of '{discriminator}'.");
        }

        return discriminator;
    }

    // TypeNameFormatter's rules: the assembly in [], the namespace without the assembly name it starts with
    // ("global:" when there is none), then ':' and the name of each declaring type and of the type itself, with the
    // generic arguments in <>.
    private static string FormatName(Type type)
    {
        if (type.IsGenericParameter)
            return type.Name;

        string assembly = type.Assembly.GetName().Name!;
        string? ns = type.Namespace;
        var builder = new StringBuilder();
        builder.Append('[').Append(assembly).Append(']');
        builder.Append(ns is null
            ? "global:"
            : ns.StartsWith(assembly, StringComparison.Ordinal) ? ns[assembly.Length..] : ns);
        AppendTypeName(builder, type);
        return builder.ToString();
    }

    private static void AppendTypeName(StringBuilder builder, Type type)
    {
        if (type.IsNested && type.DeclaringType is { } declaringType)
        {
            AppendTypeName(builder, declaringType);
        }

        int arity = type.Name.IndexOf('`');
        builder.Append(':').Append(arity < 0 ? type.Name : type.Name[..arity]);
        if (type.IsGenericType)
        {
            builder.Append('<').AppendJoin(", ", type.GetGenericArguments().Select(FormatName)).Append('>');
        }
    }

    // Writes "\n" line endings and no trailing whitespace on every OS.
    private sealed class ManifestText
    {
        private readonly StringBuilder _builder = new();

        public void Raw(string text)
        {
            foreach (string line in text.ReplaceLineEndings("\n").Split('\n'))
            {
                Line(line);
            }
        }

        public void Section(string title, params string[] description)
        {
            Line("");
            Line($"## {title}");
            foreach (string line in description)
            {
                Line($"# {line}");
            }

            Line("");
        }

        public void Line(string line)
        {
            _builder.Append(line.TrimEnd()).Append('\n');
        }

        public override string ToString() => _builder.ToString();
    }
}
