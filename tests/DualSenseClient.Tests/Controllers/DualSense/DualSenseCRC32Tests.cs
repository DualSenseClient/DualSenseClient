using DualSenseClient.Controllers.DualSense.Utilities;

namespace DualSenseClient.Tests.Controllers.DualSense;

public class DualSenseCRC32Tests
{
    [Test]
    public void Compute_EmptyData_ReturnsSeedUnmodified()
    {
        // With zero bytes processed, the running state should equal the seed itself -
        // this pins down that Compute doesn't apply any hidden pre/post transform
        // when there's nothing to hash.
        uint result = DualSenseCRC32.Compute([], 0, 0);
        Assert.That(result, Is.EqualTo(0xEADA2D49));
    }

    [Test]
    public void Compute_SingleByte_MatchesKnownVector()
    {
        // Known-good vector for a single 0x31 byte (Xbox 360-style report ID),
        // cross-checked against an independent bit-by-bit CRC32 implementation
        // seeded per the DualSense output-report spec (seed 0xEADA2D49).
        byte[] data = [0x31];
        uint result = DualSenseCRC32.Compute(data, 0, 1);
        Assert.That(result, Is.EqualTo(0x8C36CCAE));
    }

    [Test]
    public void Compute_SeventyFourBytes_MatchesKnownVector()
    {
        // 74 bytes mirrors the real input length for a Bluetooth output report CRC:
        // the first 74 bytes of the 78-byte report, seeded 0xEADA2D49.
        byte[] data = new byte[74];
        data[0] = 0x31;
        uint result = DualSenseCRC32.Compute(data, 0, data.Length);
        Assert.That(result, Is.EqualTo(0xC30E1F7B));
    }

    [Test]
    public void Compute_Subrange_OnlyHashesRequestedBytes()
    {
        byte[] data = [0x00, 0x31, 0x00];

        uint full = DualSenseCRC32.Compute(data, 0, 3);
        uint middle = DualSenseCRC32.Compute(data, 1, 1);

        // Middle must match the standalone single-byte vector for 0x31.
        Assert.That(middle, Is.EqualTo(0x8C36CCAE));

        // Full is asserted against its own known vector rather than just
        // "not equal to middle" - a bug that coincidentally produced some
        // other wrong-but-different value would have slipped past the old check.
        Assert.That(full, Is.EqualTo(0x007B0920));
    }

    [Test]
    public void Compute_ZeroLength_ReturnsSeedRegardlessOfOffset()
    {
        byte[] data = [0xFF, 0xFF, 0xFF];

        Assert.That(DualSenseCRC32.Compute(data, 0, 0), Is.EqualTo(0xEADA2D49));
        Assert.That(DualSenseCRC32.Compute(data, 2, 0), Is.EqualTo(0xEADA2D49));
    }

    [Test]
    public void Compute_DifferentData_DifferentResults()
    {
        byte[] a = [0x00];
        byte[] b = [0x01];

        uint hashA = DualSenseCRC32.Compute(a, 0, 1);
        uint hashB = DualSenseCRC32.Compute(b, 0, 1);

        Assert.That(hashA, Is.Not.EqualTo(hashB));
    }

    [Test]
    public void Compute_LongDeterministicPayload_MatchesKnownVector()
    {
        // Fixed, reproducible pattern instead of Random.Shared - anyone re-running
        // this test five years from now gets the exact same input bytes and the
        // exact same expected CRC, with no PRNG implementation to keep in sync.
        byte[] data = new byte[1024];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 7 + 3) % 256);
        }

        uint result = DualSenseCRC32.Compute(data, 0, data.Length);
        Assert.That(result, Is.EqualTo(0xF2C16BDD));
    }

    [Test]
    public void Compute_MidSizeDeterministicPayload_MatchesKnownVector()
    {
        // A second, differently-generated deterministic pattern at a size that
        // doesn't line up with the 74-byte output-report case, to catch bugs
        // that only manifest at "irregular" lengths (e.g. off-by-one in the
        // loop bound, or an index issue tied to length % 8).
        byte[] data = new byte[47];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 13 + 5) % 256);
        }

        uint result = DualSenseCRC32.Compute(data, 0, data.Length);
        Assert.That(result, Is.EqualTo(0xB3ACC3AE));
    }

    [Test]
    public void Compute_OffsetPlusLengthExceedsBuffer_Throws()
    {
        // Compute does no bounds validation of its own - this test documents
        // (and will catch any accidental change to) the current behavior of
        // relying on the underlying array access to fail fast on out-of-range reads.
        byte[] data = [0x00, 0x01];

        Assert.Throws<IndexOutOfRangeException>(() =>
            DualSenseCRC32.Compute(data, 0, 5));
    }

    [Test]
    public void Compute_NegativeLength_SilentlyReturnsSeedInsteadOfThrowing()
    {
        // Unlike an over-long buffer, a negative length never enters the loop
        // (offset < offset + length is false), so this does NOT throw - it
        // silently returns the seed as if nothing were hashed. That's a real
        // footgun for callers (a bad length looks like a valid all-zero-length
        // CRC instead of failing loudly), so this test exists to make the
        // behavior explicit rather than let it be discovered by accident.
        byte[] data = [0x00, 0x01, 0x02];

        uint result = DualSenseCRC32.Compute(data, 0, -1);

        Assert.That(result, Is.EqualTo(0xEADA2D49));
    }
}