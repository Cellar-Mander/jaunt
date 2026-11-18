using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Browser;

/// <summary>What Copy link copies, from the most to the least specific to the venue.</summary>
public enum CopyLinkKind
{
    /// <summary>The entry has nothing to copy.</summary>
    None,

    /// <summary>The venue's or organiser's own website.</summary>
    Website,

    /// <summary>The event's page on a third-party site (Partake).</summary>
    EventPage,

    /// <summary>The venue's page on a third-party site (FFXIV Venues).</summary>
    VenuePage,
}

/// <summary>What Copy link copies.</summary>
/// <param name="Uri">The page; null for <see cref="CopyLinkKind.None"/>.</param>
public readonly record struct CopyLink(CopyLinkKind Kind, Uri? Uri = null);

/// <summary>
/// Picks what Copy link copies for an entry: the first of its own website, an event page and a venue
/// page. Never the address, which Copy beside it copies. See "Copy link and Copy" under "Listing
/// browser" in <c>docs/design.md</c>.
/// </summary>
public static class CopyLinks
{
    private static readonly string[] EventHosts = ["partake.gg", "prtk.gg"];
    private static readonly string[] VenueHosts = ["ffxivvenues.com"];

    /// <param name="links">The entry's links, the title's source first.</param>
    /// <param name="members">The entry's listings, the title's first.</param>
    public static CopyLink Choose(IReadOnlyList<WebLink> links, IReadOnlyList<(ListingSource Source, string Id)> members)
    {
        // A venue's own site, not a stream, a profile or a listing site; xiv.fyi forwards to the organiser's page
        foreach (var link in links)
        {
            if (!WebLinks.IsDiscordInvite(link.Uri) && PlaceMarks.WebsiteKey(link.Uri) is not null && !OnHost(link.Uri, VenueHosts))
                return new CopyLink(CopyLinkKind.Website, link.Uri);
        }

        if (Page(members, ListingSource.Partake, links, EventHosts, "events/", Partake.SiteUrl) is { } eventPage)
            return new CopyLink(CopyLinkKind.EventPage, eventPage);
        if (Page(members, ListingSource.FfxivVenues, links, VenueHosts, "venue/", FfxivVenues.SiteUrl) is { } venuePage)
            return new CopyLink(CopyLinkKind.VenuePage, venuePage);

        // Linking a Party Finder ad in chat needs the game's relay (spike); the address has Copy of its own
        return new CopyLink(CopyLinkKind.None);
    }

    // The member's page on the source's site, else a link on one of the site's hosts
    private static Uri? Page(
        IReadOnlyList<(ListingSource Source, string Id)> members, ListingSource source, IReadOnlyList<WebLink> links, string[] hosts, string path, Uri site)
    {
        foreach (var member in members)
        {
            if (member.Source == source && member.Id.Length > 0)
                return new Uri(site, path + Uri.EscapeDataString(member.Id));
        }

        foreach (var link in links)
        {
            if (OnHost(link.Uri, hosts))
                return link.Uri;
        }

        return null;
    }

    private static bool OnHost(Uri uri, string[] hosts)
    {
        if (!WebLinks.IsWeb(uri))
            return false;
        var host = uri.Host;
        foreach (var candidate in hosts)
        {
            if (host.Equals(candidate, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
