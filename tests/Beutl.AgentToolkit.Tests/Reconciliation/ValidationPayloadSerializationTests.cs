using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

public sealed class ValidationPayloadSerializationTests
{
    private static readonly JsonSerializerOptions s_toolResultOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public void Validation_payload_for_a_replaced_effect_is_serializable()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element element = TestWorkspace.AddElement(scene, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var rect = new RectShape
        {
            Name = "Filtered rect",
            Width = { Value = 200 },
            Height = { Value = 100 },
            // Only a null FilterEffect makes the patch replace the whole property, so validation sees
            // a live Blur instead of a leaf value.
            FilterEffect = { Value = null }
        };
        element.AddObject(rect);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = element.Id.ToString(),
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = rect.Id.ToString(),
                        [nameof(Drawable.FilterEffect)] = new JsonObject
                        {
                            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(Blur)),
                            [nameof(Blur.Sigma)] = "9, 9"
                        }
                    })
                })
            },
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Valid, Is.True);
            Assert.That(rect.FilterEffect.Value, Is.InstanceOf<Blur>());
            Assert.That(apply.Value.Validation, Is.Not.Null.And.Not.Empty);
            Assert.That(() => JsonSerializer.Serialize(apply, s_toolResultOptions), Throws.Nothing);
            Assert.That(SerializeValidation(apply.Value), Does.Contain("\"Sigma\":\"9, 9\""));
            Assert.That(SerializeValidation(apply.Value), Does.Contain("\"status\":\"Ok\""));
        });
    }

    [Test]
    public void Validation_payload_for_an_inserted_media_reference_is_serializable()
    {
        Scene scene = TestWorkspace.CreateScene();
        TestWorkspace.AddElement(scene, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        string mediaPath = Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "image.png");
        File.WriteAllBytes(mediaPath, []);
        using var session = new AgentToolkitTestSession(scene);
        EditTools tools = session.CreateEditTools();

        ToolResult<ApplyEditResponse> apply = tools.ApplyEdit(
            patch: new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    ["$type"] = "[Beutl.ProjectSystem]:Element",
                    [nameof(CoreObject.Name)] = "photo",
                    [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
                    [nameof(Element.Length)] = TimeSpan.FromSeconds(5).ToString("c"),
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        ["$type"] = IdentityHelper.WriteDiscriminator(typeof(SourceImage)),
                        [nameof(SourceImage.Source)] = new Uri(mediaPath).ToString()
                    })
                })
            },
            schemaVersion: SchemaVersion.Current);

        Assert.Multiple(() =>
        {
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(apply.Value!.Valid, Is.True);
            Assert.That(scene.Children, Has.Count.EqualTo(2));
            Assert.That(apply.Value.Validation, Is.Not.Null.And.Not.Empty);
            Assert.That(() => JsonSerializer.Serialize(apply, s_toolResultOptions), Throws.Nothing);
            Assert.That(SerializeValidation(apply.Value), Does.Contain("image.png"));
            // The document reports media relative to the scene, so the validation payload must not
            // leak the host path alongside it.
            Assert.That(SerializeValidation(apply.Value), Does.Not.Contain("file://"));
        });
    }

    private static string SerializeValidation(ApplyEditResponse response)
    {
        return JsonSerializer.Serialize(response.Validation, s_toolResultOptions);
    }
}
