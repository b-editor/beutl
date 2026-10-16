using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

public sealed partial class PackageApiCheckerTests
{
    private const string MembersV1 = """
        namespace HostLib;
        public class Widget
        {
            public int Size;
            public string? Label;
            public static Widget Create() => new();
            public void Resize(int width) { }
            public void Removed() { }
            public int Measure(int x) => x;
            public int Count { get; set; }
            public void Pulled() { }
            public void Hidden() { }
        }
        public class Counter { public int Get() => 0; }
        public class Box<T> { public T? Value; public T? Get() => Value; public void Put(T value) { } }
        """;

    // Moves Pulled and Counter.Get to base types, drops Removed, retypes Label and Measure, makes Count init-only, hides Hidden.
    private const string MembersV2 = """
        namespace HostLib;
        public class WidgetBase { public void Pulled() { } }
        public class Widget : WidgetBase
        {
            public int Size;
            public object? Label;
            public static Widget Create() => new();
            public void Resize(int width) { }
            public void Resize(int width, int height) { }
            public long Measure(int x) => x;
            public int Count { get; init; }
            internal void Hidden() { }
        }
        public class Counter : Box<int> { }
        public class Box<T> { public T? Value; public T? Get() => Value; public void Put(T value) { } }
        """;

    private const string PluginUsingEveryMember = """
        using HostLib;
        public static class Uses
        {
            public static void Run()
            {
                Widget widget = Widget.Create();
                widget.Size = 1;
                widget.Label = "label";
                widget.Resize(2);
                widget.Removed();
                widget.Measure(3);
                widget.Count = 4;
                widget.Pulled();
                widget.Hidden();
                new Counter().Get();
                var box = new Box<Widget>();
                box.Put(widget);
                box.Value = box.Get();
            }
        }
        """;

    private const string PluginUsingTheUnchangedMembers = """
        using HostLib;
        public static class Uses
        {
            public static void Run()
            {
                Widget widget = Widget.Create();
                widget.Size = 1;
                widget.Resize(2);
                widget.Pulled();
                new Counter().Get();
                var box = new Box<Widget>();
                box.Put(widget);
                box.Value = box.Get();
            }
        }
        """;

    [Test]
    public void Passes_members_that_are_unchanged_or_were_pulled_up_into_a_base_type()
    {
        string v1 = Compile("v1", "HostLib", "1.0.0.0", MembersV1);
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", PluginUsingTheUnchangedMembers, v1);
        string pluginForV1 = Compile("plugin-v1", "Plugin", "1.0.0.0", PluginUsingEveryMember, v1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(PackageApiChecker.Check([pluginForV1], Host(v1)), Is.Empty);
            Assert.That(PackageApiChecker.Check([plugin], Host(Compile("v2", "HostLib", "2.0.0.0", MembersV2))), Is.Empty);
        }
    }

    [Test]
    public void Reports_each_removed_or_changed_member_that_a_package_built_for_an_older_host_uses()
    {
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", PluginUsingEveryMember, Compile("v1", "HostLib", "1.0.0.0", MembersV1));
        string v2 = Compile("v2", "HostLib", "2.0.0.0", MembersV2);
        string v2GrantingPlugin = Compile(
            "v2-friend", "HostLib", "2.0.0.0", "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"Plugin\")]\n" + MembersV2);
        PackageApiProblem[] changed =
        [
            new(PackageApiProblemKind.MissingMethod, "HostLib", "HostLib.Widget.Measure(int)"),
            new(PackageApiProblemKind.MissingMethod, "HostLib", "HostLib.Widget.Removed()"),
            new(PackageApiProblemKind.MissingMethod, "HostLib", "HostLib.Widget.set_Count(int)"),
            new(PackageApiProblemKind.MissingField, "HostLib", "HostLib.Widget.Label"),
        ];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                PackageApiChecker.Check([plugin], Host(v2)),
                Is.EqualTo(changed.Prepend(new(PackageApiProblemKind.MissingMethod, "HostLib", "HostLib.Widget.Hidden()"))));
            Assert.That(PackageApiChecker.Check([plugin], Host(v2GrantingPlugin)), Is.EqualTo(changed));
        }
    }

    [Test]
    public void Reports_the_members_that_a_package_built_for_a_newer_host_uses_and_the_host_lacks()
    {
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", """
            public static class Uses
            {
                public static void Run()
                {
                    HostLib.Widget widget = HostLib.Widget.Create();
                    widget.Resize(1);
                    widget.Resize(1, 2);
                    widget.Pulled();
                }
            }
            """, Compile("v2", "HostLib", "2.0.0.0", MembersV2));

        Assert.That(PackageApiChecker.Check([plugin], Host(Compile("v1", "HostLib", "1.0.0.0", MembersV1))), Is.EqualTo(new PackageApiProblem[]
        {
            new(PackageApiProblemKind.MissingType, "HostLib", "HostLib.WidgetBase"),
            new(PackageApiProblemKind.MissingMethod, "HostLib", "HostLib.Widget.Resize(int, int)"),
        }));
    }

    [Test]
    public void Reports_a_constructor_that_a_type_dropped_even_when_its_base_type_still_has_one()
    {
        string v1 = Compile("v1", "HostLib", "1.0.0.0", """
            namespace HostLib;
            public class Base { public Base(int size) { } }
            public class Derived : Base { public Derived(int size) : base(size) { } }
            """);
        string v2 = Compile("v2", "HostLib", "2.0.0.0", """
            namespace HostLib;
            public class Base { public Base(int size) { } }
            public class Derived : Base { public Derived() : base(0) { } }
            """);
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", "public class Uses { public object Make() => new HostLib.Derived(1); }", v1);

        Assert.That(
            PackageApiChecker.Check([plugin], Host(v2)),
            Is.EqualTo(new PackageApiProblem[] { new(PackageApiProblemKind.MissingMethod, "HostLib", "HostLib.Derived..ctor(int)") }));
    }

    // Like a package compiled against netstandard and a host compiled against System.Runtime, neither in the host map.
    [Test]
    public void Matches_a_type_outside_the_host_by_name_whichever_assembly_each_side_names()
    {
        const string Clock = "namespace HostLib; public static class Clock { public static void Take(Interop.Stamp stamp) { } }";
        string stampV1 = Compile("netstandard", "netstandard", "1.0.0.0", "namespace Interop; public struct Stamp { }");
        string stampV2 = Compile("runtime", "System.Runtime", "1.0.0.0", "namespace Interop; public struct Stamp { }");
        string v1 = Compile("v1", "HostLib", "1.0.0.0", Clock, stampV1);
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", "public static class Uses { public static void Run() => HostLib.Clock.Take(default); }", v1, stampV1);

        Assert.That(PackageApiChecker.Check([plugin], Host(Compile("v2", "HostLib", "2.0.0.0", Clock, stampV2))), Is.Empty);
    }
}
