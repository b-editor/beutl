using System.Text.Json.Nodes;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

// 1.1.0 skipped a decorator or group operator that was turned off and drew every other operator whatever its
// toggle said, so only those two migrate turned off (#2980).
[TestFixture]
public class ElementMigrationTests
{
    [TestCase("[Beutl.ProjectSystem]Beutl.Operation:DecorateOperator", typeof(DrawableDecorator), false, false)]
    [TestCase("[Beutl.ProjectSystem]Beutl.Operation:GroupOperator", typeof(DrawableGroup), false, false)]
    [TestCase("[Beutl.ProjectSystem]Beutl.Operation:GroupOperator", typeof(DrawableGroup), true, true)]
    [TestCase("[Beutl.Operators].Source:RectOperator", typeof(RectShape), false, true)]
    public void An_operator_migrates_turned_off_only_where_1_1_0_skipped_it(
        string operatorType, Type migratedType, bool operatorEnabled, bool migratedEnabled)
    {
        var json = new JsonObject
        {
            ["Operation"] = new JsonObject
            {
                ["Children"] = new JsonArray(new JsonObject
                {
                    ["$type"] = operatorType,
                    ["IsEnabled"] = operatorEnabled,
                    ["Value"] = new JsonObject(),
                }),
            },
        };

        var element = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));

        Assert.Multiple(() =>
        {
            Assert.That(element.Objects.Single(), Is.TypeOf(migratedType));
            Assert.That(element.Objects.Single().IsEnabled, Is.EqualTo(migratedEnabled));
        });
    }
}
