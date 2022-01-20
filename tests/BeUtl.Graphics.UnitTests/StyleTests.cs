using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Animation;
using BeUtl.Styling;

using NUnit.Framework;

namespace BeUtl.Graphics.UnitTests;

#nullable disable

public class StyleTests
{
    private Styleable _obj;
    private Style[] _styles;

    [SetUp]
    public void Setup()
    {
        _obj = new Styleable();
        var style1 = new Style
        {
            Setters =
            {
                new Setter<string>(Styleable.String1Property, "first1"),
                new Setter<string>(Styleable.String2Property, "first2"),
            }
        };
        var style2 = new Style
        {
            Setters =
            {
                new Setter<string>(Styleable.String2Property, "second1"),
                new Setter<string>(Styleable.String3Property, "second2"),
            }
        };
        var style3 = new Style
        {
            Setters =
            {
                new Setter<string>(Styleable.String3Property, "third1"),
                new Setter<string>(Styleable.String1Property, "third2"),
            }
        };

        _styles = new Style[]
        {
            style1,
            style2,
            style3
        };
    }

    [Test]
    public void Apply()
    {
        IStyleInstance prev = null;
        foreach (Style item in _styles)
        {
            prev = item.Instance(_obj, prev);
        }

        prev.Apply(new Clock());

        Assert.AreEqual("third2", _obj.String1);
        Assert.AreEqual("second1", _obj.String2);
        Assert.AreEqual("third1", _obj.String3);
    }

    public class Clock : IClock
    {
    }

    public class Styleable : CoreObject, IStyleable
    {
        public static readonly CoreProperty<string> String1Property = ConfigureProperty<string, Styleable>(nameof(String1)).Register();
        public static readonly CoreProperty<string> String2Property = ConfigureProperty<string, Styleable>(nameof(String2)).Register();
        public static readonly CoreProperty<string> String3Property = ConfigureProperty<string, Styleable>(nameof(String3)).Register();

        public string String1
        {
            get => GetValue(String1Property);
            set => SetValue(String1Property, value);
        }

        public string String2
        {
            get => GetValue(String2Property);
            set => SetValue(String2Property, value);
        }

        public string String3
        {
            get => GetValue(String3Property);
            set => SetValue(String3Property, value);
        }

        public IList<IStyle> Styles { get; } = new List<IStyle>();

        public IStyleInstance GetStyleInstance(IStyle style) => throw new NotImplementedException();
    }
}
