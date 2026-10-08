using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace KSPVE.Pure.Tests;

public class JsonWriterTests
{
    [Test]
    public void EmptyObject()
    {
        Assert.That(new JsonWriter().BeginObject().EndObject().ToString(), Is.EqualTo("{}"));
    }

    [Test]
    public void ObjectWithMixedProperties()
    {
        string json = new JsonWriter().BeginObject()
            .Property("a", 1).Property("b", "x").Property("c", true).Property("d", 2.5)
            .EndObject().ToString();
        Assert.That(json, Is.EqualTo("{\"a\":1,\"b\":\"x\",\"c\":true,\"d\":2.5}"));
    }

    [Test]
    public void ArrayOfObjectsAndNestedArrays()
    {
        var w = new JsonWriter();
        w.BeginObject().Name("items").BeginArray();
        w.BeginObject().Property("n", 1).EndObject();
        w.BeginObject().Property("n", 2).Name("tags").BeginArray().Value("x").Value("y").EndArray().EndObject();
        w.EndArray().EndObject();
        Assert.That(w.ToString(), Is.EqualTo("{\"items\":[{\"n\":1},{\"n\":2,\"tags\":[\"x\",\"y\"]}]}"));
    }

    [Test]
    public void StringsAreEscaped()
    {
        string json = new JsonWriter().Value("q\"b\\\n\t\u0001").ToString();
        Assert.That(json, Is.EqualTo("\"q\\\"b\\\\\\n\\t\\u0001\""));
    }

    [Test]
    public void NullStringAndNonFiniteNumbersBecomeNull()
    {
        string json = new JsonWriter().BeginArray()
            .Value((string)null).Value(double.NaN).Value(double.PositiveInfinity)
            .EndArray().ToString();
        Assert.That(json, Is.EqualTo("[null,null,null]"));
    }

    [Test]
    public void NumbersUseInvariantCulture()
    {
        CultureInfo saved = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Assert.That(new JsonWriter().Value(1.5).ToString(), Is.EqualTo("1.5"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = saved;
        }
    }

    [Test]
    public void RawValueIsEmbeddedVerbatimWithCorrectCommas()
    {
        string json = new JsonWriter().BeginObject()
            .Property("a", 1)
            .Name("b").RawValue("{\"x\":[1,2]}")
            .Property("c", 3)
            .EndObject().ToString();
        Assert.That(json, Is.EqualTo("{\"a\":1,\"b\":{\"x\":[1,2]},\"c\":3}"));
    }
}
