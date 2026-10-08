using KSPVE.Pure.Space.Procedural;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Procedural;

[TestFixture]
public sealed class StableHashTests
{
    // FNV-1a 64 test vectors from the FNV reference (isthe.com/chongo/tech/comp/fnv).
    [Test]
    public void Fnv1aVectors()
    {
        Assert.That(StableHash.Fnv1a64(""), Is.EqualTo(0xcbf29ce484222325UL));
        Assert.That(StableHash.Fnv1a64("a"), Is.EqualTo(0xaf63dc4c8601ec8cUL));
        Assert.That(StableHash.Fnv1a64("foobar"), Is.EqualTo(0x85944171f73967e8UL));
    }

    [Test]
    public void CombineIsDeterministicAndOrderSensitive()
    {
        Assert.That(StableHash.Combine(1, 2), Is.EqualTo(StableHash.Combine(1, 2)));
        Assert.That(StableHash.Combine(1, 2), Is.Not.EqualTo(StableHash.Combine(2, 1)));
        Assert.That(StableHash.Combine(1, 0), Is.Not.EqualTo(StableHash.Combine(1, 1)));
    }
}
