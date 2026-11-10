using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// Issue #4186: a member the document leaves out means the property's default, which is null only for
// a property that defaults to null. A SourceSound whose channel map became null could not be exported.
// Issue #4204: on a new object it means what the constructor assigned. An AudioSpectrumDrawable whose
// Shape became null drew nothing.
[TestFixture]
public sealed class AbsentPropertyDefaultTests
{
    private static readonly Type[] s_typesWithConstructorValues =
    [
        typeof(AudioSpectrumDrawable),
        typeof(AudioWaveformDrawable),
        typeof(Scene3D),
        typeof(TextBlock),
        typeof(RectShape),
        typeof(GeometryShape),
        typeof(SourceSound),
        typeof(EqualizerEffect)
    ];

    [TestCaseSource(nameof(s_typesWithConstructorValues))]
    public void New_object_keeps_what_its_constructor_assigns(Type type)
    {
        Scene scene = TestWorkspace.CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        JsonObject inserted = NewObject(type);
        inserted[nameof(CoreObject.Name)] = "inserted";
        // A flow operator takes its input from the PortalObject before it.
        JsonObject patch = typeof(IFlowOperator).IsAssignableFrom(type)
            ? InsertElementWith(NewObject(typeof(PortalObject)), inserted)
            : InsertElementWith(inserted);

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(patch: patch, schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        EngineObject actual = scene.Children.Single().Objects.Single(obj => obj.GetType() == type);
        var expected = (EngineObject)Activator.CreateInstance(type)!;
        Assert.Multiple(() =>
        {
            // Each member that holds an object or a list holds what a new instance has.
            foreach (IProperty property in expected.Properties.Where(
                         property => !property.ValueType.IsValueType && property.ValueType != typeof(string)))
            {
                IProperty actualProperty = actual.Properties.Single(item => item.Name == property.Name);
                if (property is IListProperty expectedItems)
                {
                    Assert.That(((IListProperty)actualProperty).Count, Is.EqualTo(expectedItems.Count), property.Name);
                }
                else
                {
                    Assert.That(actualProperty.Value?.GetType(), Is.EqualTo(property.Value?.GetType()), property.Name);
                }
            }
        });
    }

    [Test]
    public void New_text_with_a_null_fill_has_no_fill()
    {
        Scene scene = TestWorkspace.CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();
        JsonObject inserted = NewObject(typeof(TextBlock));
        inserted[nameof(TextBlock.Fill)] = null;

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: InsertElementWith(inserted),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        var text = (TextBlock)scene.Children.Single().Objects.Single();
        Assert.That(text.Fill.Value, Is.Null);
    }

    [Test]
    public void New_sound_without_a_channel_map_uses_auto()
    {
        Scene scene = TestWorkspace.CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: InsertElementWith<SourceSound>(),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        var sound = (SourceSound)scene.Children.Single().Objects.Single();
        Assert.That(sound.ChannelMap.Value, Is.EqualTo(AudioChannelMap.Auto));
    }

    [Test]
    public void New_text_without_a_font_family_uses_the_default_font()
    {
        Scene scene = TestWorkspace.CreateScene();
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: InsertElementWith<TextBlock>(),
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        var text = (TextBlock)scene.Children.Single().Objects.Single();
        Assert.That(text.FontFamily.Value, Is.EqualTo(FontFamily.Default));
    }

    [Test]
    public void Patching_a_channel_map_to_null_returns_it_to_auto()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, MediaTime.Zero, MediaTime.FromSeconds(1));
        var sound = new SourceSound();
        sound.ChannelMap.Value = new AudioChannelMap(AudioChannelFormat.Mono, AudioChannelLayout.Stereo, [0]);
        element.AddObject(sound);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Id.Value.ToString(),
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = sound.Id.Value.ToString(),
                        [nameof(SourceSound.ChannelMap)] = null
                    })
                })
            },
            schemaVersion: SchemaVersion.Current);

        Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
        Assert.That(sound.ChannelMap.Value, Is.EqualTo(AudioChannelMap.Auto));
    }

    private static JsonObject InsertElementWith<T>() => InsertElementWith(NewObject(typeof(T)));

    private static JsonObject InsertElementWith(params JsonObject[] objects)
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Element)),
                [nameof(Element.Start)] = "00:00:00",
                [nameof(Element.Length)] = "00:00:01",
                [nameof(Element.Objects)] = new JsonArray(objects)
            })
        };
    }

    private static JsonObject NewObject(Type type)
    {
        return new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(type)
        };
    }
}
