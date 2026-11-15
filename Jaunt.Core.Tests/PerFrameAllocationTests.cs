using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

/// <summary>
/// Per-frame code that allocates makes the garbage collector pause the game. Each test here runs a
/// per-frame method like the game would and checks it allocates nothing; see "Per-frame code" in
/// CONTRIBUTING.md.
/// </summary>
public class PerFrameAllocationTests
{
    // The first runs allocate while the JIT compiles the code, so they don't count
    private const int WarmUpRuns = 3;
    private const int Frames = 100_000;

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);
    private static readonly AddressKey Tavern = new("SIREN", District.Empyreum, 16, 20, null, false);

    private static readonly EntryMark[] Marks = [EntryMark.Favourite, EntryMark.Visited, EntryMark.Hidden];

    // The list's star and tick for each row drawn, and each mark button's tooltip in the detail pane
    [Fact]
    public void EntryMarksDoNotAllocatePerFrame()
    {
        var cache = new ListingCache();
        var favouriteAd = cache.SeenAd(ListingSource.InGamePartyFinder, "a", "Tavern Empyreum W16 P20", "Siren", Now);
        favouriteAd.Favourite = true;
        var plainAd = cache.SeenAd(ListingSource.InGamePartyFinder, "b", "Tavern Empyreum W16 P20", "Siren", Now);
        var calendarEvent = cache.Seen(ListingSource.Partake, "e", "Gala", Now, oneOff: true);
        var place = cache.Place(Tavern);
        place.Visited = true;

        // Marked by its place, by a listing past the first, and not at all; each target too
        MarkEntry[] entries =
        [
            Entry([Tavern], plainAd),
            Entry([], plainAd, favouriteAd),
            Entry([], plainAd, calendarEvent),
            Entry([], plainAd),
        ];
        PlaceMark?[] places = [place, null, null, null];

        AssertNoAllocation(() => DrawMarks(entries, places));
    }

    // Each row drawn colours its status line by whether it is open now
    [Fact]
    public void OccurrenceIsOpenDoesNotAllocatePerFrame()
    {
        Occurrence[] openings =
        [
            new(Now.AddHours(-1), Now.AddHours(1)),
            new(Now.AddHours(1), Now.AddHours(2)),
            new(null, null),
            new(Now.AddHours(-2), Now.AddHours(-1)),
        ];

        AssertNoAllocation(() => CountOpen(openings));
    }

    private static MarkEntry Entry(AddressKey[] at, params CachedListing[] members) => new(members, at, "Tavern", "Siren, Empyreum, W16, P20", []);

    private static int DrawMarks(MarkEntry[] entries, PlaceMark?[] places)
    {
        var shown = 0;
        for (var frame = 0; frame < Frames; frame++)
        {
            var i = frame % entries.Length;
            var mark = Marks[frame % Marks.Length];
            if (EntryMarks.Has(entries[i], places[i], mark))
                shown++;
            if (EntryMarks.Target(entries[i], mark) != MarkTarget.Listings)
                shown++;
        }

        return shown;
    }

    private static int CountOpen(Occurrence[] openings)
    {
        var open = 0;
        for (var frame = 0; frame < Frames; frame++)
        {
            if (openings[frame % openings.Length].IsOpen(Now))
                open++;
        }

        return open;
    }

    private static void AssertNoAllocation(Func<int> runFrames)
    {
        for (var i = 0; i < WarmUpRuns; i++)
        {
            runFrames();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = runFrames();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(result > 0, "the measured frames should reach the code under test");
        Assert.Equal(0, allocated);
    }
}
