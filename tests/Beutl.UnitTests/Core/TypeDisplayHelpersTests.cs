using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Beutl.UnitTests.Core;

public class TypeDisplayHelpersTests
{
    [Display(Name = "Cool Type", Description = "A cool type for testing.")]
    private sealed class TypeWithDisplay
    {
        [Display(Name = "Cool Member", Description = "A cool member.")]
        public int MemberWithDisplay { get; set; }

        public int PlainMember { get; set; }
    }

    private sealed class TypeWithoutDisplay
    {
        public int PlainMember { get; set; }
    }

    [Test]
    public void GetLocalizedName_Type_ReturnsDisplayName()
    {
        Assert.That(
            TypeDisplayHelpers.GetLocalizedName(typeof(TypeWithDisplay)),
            Is.EqualTo("Cool Type")
        );
    }

    [Test]
    public void GetLocalizedDescription_Type_ReturnsDisplayDescription()
    {
        Assert.That(
            TypeDisplayHelpers.GetLocalizedDescription(typeof(TypeWithDisplay)),
            Is.EqualTo("A cool type for testing.")
        );
    }

    [Test]
    public void GetLocalizedName_Type_FallsBackToTypeName()
    {
        Assert.That(
            TypeDisplayHelpers.GetLocalizedName(typeof(TypeWithoutDisplay)),
            Is.EqualTo(nameof(TypeWithoutDisplay))
        );
    }

    [Test]
    public void GetLocalizedDescription_Type_NoAttribute_ReturnsNull()
    {
        Assert.That(
            TypeDisplayHelpers.GetLocalizedDescription(typeof(TypeWithoutDisplay)),
            Is.Null
        );
    }

    [Test]
    public void GetLocalizedName_Member_ReturnsDisplayName()
    {
        MemberInfo member = typeof(TypeWithDisplay).GetProperty(
            nameof(TypeWithDisplay.MemberWithDisplay)
        )!;
        Assert.That(TypeDisplayHelpers.GetLocalizedName(member), Is.EqualTo("Cool Member"));
    }

    [Test]
    public void GetLocalizedDescription_Member_ReturnsDisplayDescription()
    {
        MemberInfo member = typeof(TypeWithDisplay).GetProperty(
            nameof(TypeWithDisplay.MemberWithDisplay)
        )!;
        Assert.That(
            TypeDisplayHelpers.GetLocalizedDescription(member),
            Is.EqualTo("A cool member.")
        );
    }

    [Test]
    public void GetLocalizedName_Member_NoAttribute_FallsBackToMemberName()
    {
        MemberInfo member = typeof(TypeWithDisplay).GetProperty(
            nameof(TypeWithDisplay.PlainMember)
        )!;
        Assert.That(TypeDisplayHelpers.GetLocalizedName(member), Is.EqualTo("PlainMember"));
    }

    [Test]
    public void GetLocalizedDescription_Member_NoAttribute_ReturnsNull()
    {
        MemberInfo member = typeof(TypeWithDisplay).GetProperty(
            nameof(TypeWithDisplay.PlainMember)
        )!;
        Assert.That(TypeDisplayHelpers.GetLocalizedDescription(member), Is.Null);
    }

    [Test]
    public void GetLocalizedName_NullType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TypeDisplayHelpers.GetLocalizedName((Type)null!)
        );
    }

    [Test]
    public void GetLocalizedDescription_NullType_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TypeDisplayHelpers.GetLocalizedDescription((Type)null!)
        );
    }

    [Test]
    public void GetLocalizedName_NullMember_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TypeDisplayHelpers.GetLocalizedName((MemberInfo)null!)
        );
    }

    [Test]
    public void GetLocalizedDescription_NullMember_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TypeDisplayHelpers.GetLocalizedDescription((MemberInfo)null!)
        );
    }

    [Test]
    public void GetLocalizedName_Type_CachesResult()
    {
        // Calling twice should return identical (cached) string instance.
        string a = TypeDisplayHelpers.GetLocalizedName(typeof(TypeWithDisplay));
        string b = TypeDisplayHelpers.GetLocalizedName(typeof(TypeWithDisplay));
        Assert.That(a, Is.SameAs(b));
    }
}
