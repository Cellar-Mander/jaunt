using System;

namespace Jaunt.Windows;

/// <summary>
/// What a window's <c>Draw</c> allocated on its last frame, for the self-test that checks steady
/// frames allocate nothing; see "Per-frame code" in CONTRIBUTING.md. A frame that worked something
/// out for a cache (<see cref="Filled"/>) may allocate, so it isn't measured. Use from the draw thread only.
/// </summary>
internal sealed class AllocationWatch
{
    // Bumped by each cache that draw code fills
    private static int Fills;

    private long bytesBefore;
    private int fillsBefore;

    /// <summary>Frames drawn.</summary>
    public int Frames { get; private set; }

    /// <summary>Bytes the last frame allocated, or null if it filled a cache.</summary>
    public long? Last { get; private set; }

    /// <summary>Call when draw code works out a value it keeps, as that frame may allocate.</summary>
    public static void Filled() => Fills++;

    public void Start()
    {
        fillsBefore = Fills;
        bytesBefore = GC.GetAllocatedBytesForCurrentThread();
    }

    public void Stop()
    {
        var bytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
        Last = Fills == fillsBefore ? bytes : null;
        Frames++;
    }
}
