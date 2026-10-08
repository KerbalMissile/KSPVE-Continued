using System.Linq;
using KSPVE.Pure.Pipeline;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Pipeline;

[TestFixture]
public sealed class PassGraphTests
{
    private static PassNode P(string id, int order, string[] reads, string[] writes) => new(id, order, reads, writes);
    private static string Ids(PassOrderResult r) => string.Join(",", r.Ordered.Select(p => p.Id));

    [Test]
    public void IndependentPassesFollowDeclaredOrderThenId()
    {
        var r = PassGraph.Order(new[]
        {
            P("c", 2, new string[0], new[] { "x" }),
            P("b", 1, new string[0], new[] { "y" }),
            P("a", 1, new string[0], new[] { "z" }),
        });
        Assert.That(Ids(r), Is.EqualTo("a,b,c"));
        Assert.That(r.Errors, Is.Empty);
    }

    [Test]
    public void ConsumerFollowsProducerDespiteDeclaredOrder()
    {
        var r = PassGraph.Order(new[]
        {
            P("consumer", 0, new[] { "hdr" }, new string[0]),
            P("producer", 5, new string[0], new[] { "hdr" }),
        });
        Assert.That(Ids(r), Is.EqualTo("producer,consumer"));
    }

    [Test]
    public void ModifiersChainByDeclaredOrderAfterProducersBeforeConsumers()
    {
        var r = PassGraph.Order(new[]
        {
            P("cons", 0, new[] { "t" }, new string[0]),
            P("modB", 2, new[] { "t" }, new[] { "t" }),
            P("modA", 1, new[] { "t" }, new[] { "t" }),
            P("prod", 9, new string[0], new[] { "t" }),
        });
        Assert.That(Ids(r), Is.EqualTo("prod,modA,modB,cons"));
        Assert.That(r.Errors, Is.Empty);
    }

    [Test]
    public void ReadOfExternalResourceIsAllowed()
    {
        var r = PassGraph.Order(new[] { P("a", 0, new[] { "gbuffer" }, new[] { "out" }) });
        Assert.That(Ids(r), Is.EqualTo("a"));
        Assert.That(r.Errors, Is.Empty);
    }

    [Test]
    public void CycleReportsErrorAndAppendsRemaining()
    {
        var r = PassGraph.Order(new[]
        {
            P("free", 0, new string[0], new[] { "q" }),
            P("a", 1, new[] { "y" }, new[] { "x" }),
            P("b", 2, new[] { "x" }, new[] { "y" }),
        });
        Assert.That(r.Errors.Count, Is.EqualTo(1));
        Assert.That(r.Errors[0], Does.Contain("a").And.Contain("b"));
        Assert.That(Ids(r), Is.EqualTo("free,a,b"));
    }

    [Test]
    public void DuplicateIdIsErrorAndFirstKept()
    {
        var r = PassGraph.Order(new[]
        {
            P("a", 0, new string[0], new[] { "x" }),
            P("a", 1, new string[0], new[] { "y" }),
        });
        Assert.That(r.Ordered.Count, Is.EqualTo(1));
        Assert.That(r.Ordered[0].DeclaredOrder, Is.EqualTo(0));
        Assert.That(r.Errors.Count, Is.EqualTo(1));
    }

    [Test]
    public void NullInputDoesNotThrow()
    {
        var r = PassGraph.Order(null!);
        Assert.That(r.Ordered, Is.Empty);
    }

    [Test]
    public void OrderIsDeterministicAcrossInputPermutations()
    {
        var passes = new[]
        {
            P("a", 3, new[] { "r" }, new string[0]),
            P("b", 1, new string[0], new[] { "r" }),
            P("c", 2, new[] { "r" }, new[] { "r" }),
            P("d", 0, new[] { "r" }, new string[0]),
        };
        var expected = Ids(PassGraph.Order(passes));
        Assert.That(Ids(PassGraph.Order(passes.Reverse().ToArray())), Is.EqualTo(expected));
        Assert.That(expected, Is.EqualTo("b,c,d,a"));
    }
}
