using System.Text;

namespace KSPVE.Pure.Space.Procedural;

/// <summary>Platform-independent hashes for deriving deterministic seeds.</summary>
public static class StableHash
{
    /// <summary>FNV-1a 64-bit of the UTF-8 bytes of <paramref name="text"/>.</summary>
    public static ulong Fnv1a64(string text)
    {
        ulong hash = 0xcbf29ce484222325UL;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            unchecked { hash *= 0x100000001b3UL; }
        }
        return hash;
    }

    /// <summary>
    /// Order-sensitive 64-bit mix: SplitMix64 finaliser (Steele, Lea, Flood 2014) applied to
    /// a + golden * (b + 1), where golden = 0x9E3779B97F4A7C15.
    /// </summary>
    public static ulong Combine(ulong a, ulong b)
    {
        unchecked
        {
            ulong z = a + 0x9E3779B97F4A7C15UL * (b + 1UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
