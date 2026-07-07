using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using MergePatchApplier = Beutl.AgentToolkit.MergePatch.MergePatch;

namespace Beutl.AgentToolkit.Tests.MergePatch;

public sealed class MergePatchTests
{
    [Test]
    public void Object_patch_deletes_null_and_merges_nested_objects()
    {
        JsonNode result = MergePatchApplier.Apply(
            JsonNode.Parse("""{"a":1,"b":{"c":2,"d":3}}""")!,
            JsonNode.Parse("""{"a":null,"b":{"c":4}}""")!)!;

        Assert.Multiple(() =>
        {
            Assert.That(result["a"], Is.Null);
            Assert.That(result["b"]!["c"]!.GetValue<int>(), Is.EqualTo(4));
            Assert.That(result["b"]!["d"]!.GetValue<int>(), Is.EqualTo(3));
        });
    }

    [Test]
    public void Nested_directives_inside_a_newly_inserted_member_are_rejected()
    {
        Guid existingId = Guid.NewGuid();
        Guid nestedId = Guid.NewGuid();

        ReconcileException ex = Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
            JsonNode.Parse($$"""{"Elements":[{"Id":"{{existingId}}"}]}""")!,
            JsonNode.Parse($$"""
                {
                  "Elements": [
                    {
                      "Name": "new",
                      "Objects": [
                        { "Id": "{{nestedId}}", "$delete": true }
                      ]
                    }
                  ]
                }
                """)!))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("$delete"));
        });
    }

    [Test]
    public void Nested_directives_inside_replacement_members_are_rejected()
    {
        ReconcileException ex = Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
            JsonNode.Parse("""{"Elements":[]}""")!,
            JsonNode.Parse($$"""
                {
                  "Elements": [
                    { "$replace": true },
                    {
                      "Name": "a",
                      "Objects": [
                        { "Id": "{{Guid.NewGuid()}}", "$delete": true }
                      ]
                    }
                  ]
                }
                """)!))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("$delete"));
        });
    }

    [Test]
    public void Nested_directives_inside_a_typed_object_replacement_are_rejected()
    {
        ReconcileException ex = Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
            JsonNode.Parse("""{"FilterEffect":{"$type":"Blur"}}""")!,
            JsonNode.Parse($$"""
                {
                  "FilterEffect": {
                    "$type": "FilterEffectGroup",
                    "Children": [
                      { "Id": "{{Guid.NewGuid()}}", "$delete": true }
                    ]
                  }
                }
                """)!))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("$delete"));
        });
    }

    [Test]
    public void Nested_replace_directive_inside_a_newly_inserted_member_is_rejected()
    {
        Guid existingId = Guid.NewGuid();

        ReconcileException ex = Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
            JsonNode.Parse($$"""{"Elements":[{"Id":"{{existingId}}"}]}""")!,
            JsonNode.Parse("""
                {
                  "Elements": [
                    {
                      "Name": "new",
                      "Objects": [
                        { "$replace": true },
                        { "Name": "child" }
                      ]
                    }
                  ]
                }
                """)!))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("$replace"));
        });
    }

    [Test]
    public void Typed_object_null_patch_deletes_property()
    {
        Guid id = Guid.NewGuid();
        JsonNode result = MergePatchApplier.Apply(
            JsonNode.Parse($$"""
                {
                  "Objects": [
                    {
                      "Id": "{{id}}",
                      "FilterEffect": {
                        "$type": "FilterEffectGroup",
                        "Children": [
                          {
                            "$type": "Blur"
                          }
                        ]
                      }
                    }
                  ]
                }
                """)!,
            JsonNode.Parse($$"""
                {
                  "Objects": [
                    {
                      "Id": "{{id}}",
                      "FilterEffect": null
                    }
                  ]
                }
                """)!)!;

        JsonObject shape = (JsonObject)((JsonArray)result["Objects"]!)[0]!;

        Assert.That(shape["FilterEffect"], Is.Null);
    }

    [Test]
    public void Typed_object_patch_without_id_replaces_when_discriminator_changes()
    {
        JsonNode result = MergePatchApplier.Apply(
            JsonNode.Parse("""{"Fill":{"$type":"SolidColorBrush","Id":"existing-brush","Color":"White"}}""")!,
            JsonNode.Parse("""{"Fill":{"$type":"LinearGradientBrush","GradientStops":[{"$type":"GradientStop","Offset":0,"Color":"Blue"}]}}""")!)!;

        JsonObject fill = (JsonObject)result["Fill"]!;

        Assert.Multiple(() =>
        {
            Assert.That(fill["$type"]!.GetValue<string>(), Is.EqualTo("LinearGradientBrush"));
            Assert.That(fill["Id"], Is.Null);
            Assert.That(fill["Color"], Is.Null);
            Assert.That((JsonArray)fill["GradientStops"]!, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Id_keyed_array_patch_updates_one_member_without_dropping_siblings()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        JsonNode result = MergePatchApplier.Apply(
            JsonNode.Parse($$"""{"items":[{"Id":"{{first}}","Name":"A"},{"Id":"{{second}}","Name":"B"}]}""")!,
            JsonNode.Parse($$"""{"items":[{"Id":"{{second}}","Name":"B2"}]}""")!)!;

        JsonArray items = (JsonArray)result["items"]!;

        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0]!["Name"]!.GetValue<string>(), Is.EqualTo("A"));
            Assert.That(items[1]!["Name"]!.GetValue<string>(), Is.EqualTo("B2"));
        });
    }

    [Test]
    public void Delete_missing_id_is_idempotent_but_plain_unknown_id_is_stale()
    {
        Guid unknown = Guid.NewGuid();
        JsonNode target = JsonNode.Parse("""{"items":[]}""")!;

        JsonNode deleted = MergePatchApplier.Apply(
            target,
            JsonNode.Parse($$"""{"items":[{"Id":"{{unknown}}","$delete":true}]}""")!)!;

        Assert.That((JsonArray)deleted["items"]!, Is.Empty);

        var ex = Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
            target,
            JsonNode.Parse($$"""{"items":[{"Id":"{{unknown}}","Name":"missing"}]}""")!));
        Assert.Multiple(() =>
        {
            Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.StaleHandle));
            Assert.That(ex.Error.Hint, Does.Contain("Omit Id to create"));
            Assert.That(ex.Error.Hint, Does.Contain("keep the parent Element Id"));
            Assert.That(ex.Error.Hint, Does.Contain("read_document"));
        });
    }

    [Test]
    public void Omitted_id_inserts_and_supplied_type_mismatch_rejects()
    {
        Guid existing = Guid.NewGuid();
        JsonNode inserted = MergePatchApplier.Apply(
            JsonNode.Parse($$"""{"items":[{"$type":"A","Id":"{{existing}}"}]}""")!,
            JsonNode.Parse("""{"items":[{"$type":"A","Name":"new"}]}""")!)!;

        JsonArray items = (JsonArray)inserted["items"]!;
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[1]!["Id"]!.GetValue<string>(), Is.Not.Empty);
        });

        var ex = Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
            JsonNode.Parse($$"""{"items":[{"$type":"A","Id":"{{existing}}"}]}""")!,
            JsonNode.Parse($$"""{"items":[{"$type":"B","Id":"{{existing}}"}]}""")!));
        Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
    }

    [Test]
    public void Ordering_directives_move_members_and_bad_directives_fail()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid c = Guid.NewGuid();
        JsonNode target = JsonNode.Parse($$"""{"items":[{"Id":"{{a}}"},{"Id":"{{b}}"},{"Id":"{{c}}"}]}""")!;

        JsonNode moved = MergePatchApplier.Apply(
            target,
            JsonNode.Parse($$"""{"items":[{"Id":"{{c}}","$before":"{{a}}"}]}""")!)!;
        JsonArray items = (JsonArray)moved["items"]!;
        Assert.That(items[0]!["Id"]!.GetValue<string>(), Is.EqualTo(c.ToString()));

        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
                target,
                JsonNode.Parse($$"""{"items":[{"Id":"{{b}}","$index":0,"$after":"{{a}}"}]}""")!))!.Error.Code,
                Is.EqualTo(ErrorCode.ValidationRejected));

            ReconcileException staleSibling = Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
                target,
                JsonNode.Parse($$"""{"items":[{"Id":"{{b}}","$after":"{{Guid.NewGuid()}}"}]}""")!))!;
            Assert.That(staleSibling.Error.Code, Is.EqualTo(ErrorCode.StaleHandle));
            Assert.That(staleSibling.Error.Hint, Does.Contain("Omit Id to create"));
            Assert.That(staleSibling.Error.Hint, Does.Contain("keep the parent Element Id"));
        });
    }

    [Test]
    public void Scalar_and_non_id_arrays_replace_wholesale()
    {
        JsonNode result = MergePatchApplier.Apply(
            JsonNode.Parse("""{"values":[1,2,3]}""")!,
            JsonNode.Parse("""{"values":[4]}""")!)!;

        JsonArray values = (JsonArray)result["values"]!;
        Assert.Multiple(() =>
        {
            Assert.That(values, Has.Count.EqualTo(1));
            Assert.That(values[0]!.GetValue<int>(), Is.EqualTo(4));
        });
    }

    [Test]
    public void Replace_sentinel_rebuilds_id_bearing_array_instead_of_merging()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid c = Guid.NewGuid();
        JsonNode result = MergePatchApplier.Apply(
            JsonNode.Parse($$"""{"Children":[{"Id":"{{a}}","$type":"Blur"},{"Id":"{{b}}","$type":"Blur"},{"Id":"{{c}}","$type":"Blur"}]}""")!,
            JsonNode.Parse("""{"Children":[{"$replace":true},{"$type":"DropShadow"}]}""")!)!;

        JsonArray children = (JsonArray)result["Children"]!;
        Assert.Multiple(() =>
        {
            Assert.That(children, Has.Count.EqualTo(1));
            Assert.That(children[0]!["$type"]!.GetValue<string>(), Is.EqualTo("DropShadow"));
            Assert.That(children[0]!["Id"], Is.Null);
        });
    }

    [Test]
    public void Replace_sentinel_with_empty_body_clears_array()
    {
        Guid a = Guid.NewGuid();
        JsonNode result = MergePatchApplier.Apply(
            JsonNode.Parse($$"""{"Children":[{"Id":"{{a}}","$type":"Blur"}]}""")!,
            JsonNode.Parse("""{"Children":[{"$replace":true}]}""")!)!;

        Assert.That((JsonArray)result["Children"]!, Is.Empty);
    }

    [Test]
    public void Replace_sentinel_must_be_first_and_standalone()
    {
        JsonNode target = JsonNode.Parse("""{"Children":[{"$type":"Blur"}]}""")!;

        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
                target,
                JsonNode.Parse("""{"Children":[{"$type":"Blur"},{"$replace":true}]}""")!))!.Error.Code,
                Is.EqualTo(ErrorCode.ValidationRejected));

            Assert.That(Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
                target,
                JsonNode.Parse("""{"Children":[{"$replace":true},{"$type":"Blur","$delete":true}]}""")!))!.Error.Code,
                Is.EqualTo(ErrorCode.ValidationRejected));

            Assert.That(Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
                target,
                JsonNode.Parse("""{"Children":[{"$replace":true,"$type":"Blur"}]}""")!))!.Error.Code,
                Is.EqualTo(ErrorCode.ValidationRejected));
        });
    }

    [Test]
    public void Non_boolean_delete_directive_returns_validation_error()
    {
        Guid id = Guid.NewGuid();
        JsonNode target = JsonNode.Parse($$"""{"Children":[{"Id":"{{id}}","$type":"Blur"}]}""")!;

        ReconcileException ex = Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
            target,
            JsonNode.Parse($$"""{"Children":[{"Id":"{{id}}","$delete":"true"}]}""")!))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("$delete"));
        });
    }

    [Test]
    public void Non_integer_index_directive_returns_validation_error()
    {
        JsonNode target = JsonNode.Parse("""{"Children":[]}""")!;

        ReconcileException ex = Assert.Throws<ReconcileException>(() => MergePatchApplier.Apply(
            target,
            JsonNode.Parse("""{"Children":[{"$type":"Blur","$index":"first"}]}""")!))!;

        Assert.Multiple(() =>
        {
            Assert.That(ex.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(ex.Error.Message, Does.Contain("$index"));
        });
    }
}
