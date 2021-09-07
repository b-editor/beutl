using System.Diagnostics;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.Primitive;

using NUnit.Framework;

using TestObject = BEditor.Primitive.Objects.PropertyTest;

namespace NUnitTestProject1
{
    public class EditingPropertyTest
    {
        [SetUp]
        public void Setup()
        {
            foreach (var obj in PrimitiveTypes.EnumerateAllObjectMetadata())
            {
                ObjectMetadata.LoadedObjects.Add(obj);
            }

            foreach (var effect in PrimitiveTypes.EnumerateAllEffectMetadata())
            {
                EffectMetadata.LoadedEffects.Add(effect);
            }

            foreach (var ease in PrimitiveTypes.EnumerateAllEasingMetadata())
            {
                EasingMetadata.LoadedEasingFunc.Add(ease);
            }
            EasingMetadata.LoadedEasingFunc.Add(EasingMetadata.Create<PrimitiveEasing>(""));
        }

        [Test]
        public void PropertyTest()
        {
            var obj = new TestObject
            {
                Parent = null
            };

            Debug.Assert(obj.Children.FirstOrDefault(i => i is null) is null, "全てのEditingPropertyに値がsetされていない。");

            obj.Load();

            Debug.Assert(obj.Children.FirstOrDefault(i => !i.IsLoaded) is null, "EditingPropertyがすべてLoadされていない。");

            obj.Unload();

            Debug.Assert(obj.Children.FirstOrDefault(i => i.IsLoaded) is null, "EditingPropertyがすべてUnloadされていない。");
        }

        [Test]
        public void SetGetTest()
        {
            var obj = new TestObject
            {
                Parent = null
            };

            var @new = new ButtonComponent(new("New"));

            obj.SetValue(TestObject.ButtonProperty, @new);


            Debug.Assert(obj.GetValue(TestObject.ButtonProperty) == @new, "プロパティに値が設定されていない");
        }

        [Test]
        public void GetRootTest()
        {
            var obj = new TestObject
            {
                Parent = null
            };

            var root = obj.Ease.EasingType.GetRoot();

            Debug.Assert(root == obj);
        }
    }
}