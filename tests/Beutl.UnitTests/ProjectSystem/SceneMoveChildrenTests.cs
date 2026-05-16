using Beutl.ProjectSystem;
using NUnit.Framework.Legacy;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class SceneMoveChildrenTests
{
    private static string GetTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"beutl_scene_movechildren_{Guid.NewGuid():N}");
    }

    private static Scene CreateScene(string basePath)
    {
        Directory.CreateDirectory(basePath);
        return new Scene(100, 100, string.Empty)
        {
            Uri = new Uri(Path.Combine(basePath, "test.scene"))
        };
    }

    private static Element CreateElement(string basePath, TimeSpan start, TimeSpan length, int zIndex = 0)
    {
        return new Element
        {
            Start = start,
            Length = length,
            ZIndex = zIndex,
            Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.layer"))
        };
    }

    [Test]
    public void MoveChildren_SingleElement_ShiftsStartByDelta()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element element = CreateElement(basePath, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
            scene.Children.Add(element);

            scene.MoveChildren(0, TimeSpan.FromSeconds(1), [element]);

            ClassicAssert.AreEqual(TimeSpan.FromSeconds(3), element.Start);
            ClassicAssert.AreEqual(0, element.ZIndex);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void MoveChildren_EmptyArray_Throws()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);

            ClassicAssert.Throws<ArgumentOutOfRangeException>(
                () => scene.MoveChildren(0, TimeSpan.FromSeconds(1), []));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void MoveChildren_NonZeroDeltaIndex_ShiftsZIndex()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element element = CreateElement(basePath, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 1);
            scene.Children.Add(element);

            scene.MoveChildren(+2, TimeSpan.Zero, [element]);

            ClassicAssert.AreEqual(3, element.ZIndex);
            ClassicAssert.AreEqual(TimeSpan.FromSeconds(2), element.Start);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void MoveChildren_ConflictingShift_LeavesElementUnchanged()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element movable = CreateElement(basePath, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 0);
            Element obstacle = CreateElement(basePath, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 1);
            scene.Children.Add(movable);
            scene.Children.Add(obstacle);

            // movable を obstacle の ZIndex に重ねようとすると衝突する。
            // 時間方向の自動補正は効かないので no-op になる契約。
            scene.MoveChildren(+1, TimeSpan.Zero, [movable]);

            ClassicAssert.AreEqual(0, movable.ZIndex);
            ClassicAssert.AreEqual(TimeSpan.FromSeconds(2), movable.Start);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public void MoveChildren_MultipleElements_AllShiftTogether()
    {
        string basePath = GetTempPath();
        try
        {
            Scene scene = CreateScene(basePath);
            Element a = CreateElement(basePath, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), zIndex: 0);
            Element b = CreateElement(basePath, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1), zIndex: 1);
            scene.Children.Add(a);
            scene.Children.Add(b);

            scene.MoveChildren(0, TimeSpan.FromSeconds(1), [a, b]);

            ClassicAssert.AreEqual(TimeSpan.FromSeconds(3), a.Start);
            ClassicAssert.AreEqual(TimeSpan.FromSeconds(5), b.Start);
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }
}
