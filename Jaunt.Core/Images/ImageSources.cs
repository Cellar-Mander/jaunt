using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Images;

/// <summary>The two images a listing shows.</summary>
public enum ImageKind
{
    /// <summary>The small square at the left of its row.</summary>
    Icon,

    /// <summary>The wide band at the top of the detail pane.</summary>
    Splash,
}

/// <summary>Where one image may come from, tried in turn until one gives a picture.</summary>
public abstract record ImageSource
{
    /// <summary>Identifies the source in an <see cref="ImageSlot"/>'s key.</summary>
    public abstract string Key { get; }
}

/// <summary>An image at a known address, such as an FFXIV Venues banner or a Partake event's image.</summary>
public sealed record PictureSource(Uri Uri) : ImageSource
{
    public override string Key => "url:" + Uri.AbsoluteUri;
}

/// <summary>The server behind a Discord invite: its icon for <see cref="ImageKind.Icon"/>, else its banner or invite splash.</summary>
/// <param name="Code">The invite's code, case kept.</param>
public sealed record DiscordSource(string Code) : ImageSource
{
    public override string Key => "discord:" + Code;
}

/// <summary>A website's own icon, which its home page names (see <see cref="Favicons"/>).</summary>
/// <param name="Site">The site's home page: its scheme and host, path "/".</param>
public sealed record FaviconSource(Uri Site) : ImageSource
{
    public override string Key => "favicon:" + Site.AbsoluteUri;
}

/// <summary>
/// Which sources an entry's icon and splash image are tried from, in order. The rules are in
/// <c>docs/design.md</c> under "Icons and splash images".
/// </summary>
public static class ImageSources
{
    /// <summary>At most this many of each kind of source, so one entry with many links makes few requests.</summary>
    internal const int MaxEach = 2;

    /// <summary>
    /// The sources for an entry with these listings and links. Icon: each Discord invite's server icon,
    /// then the icon of each website FFXIV Venues or Partake gives, then a square from the FFXIV Venues
    /// banner. Splash: the FFXIV Venues banner, then each invite's server banner or splash, then the
    /// Partake event's images. Empty when it has none, so it shows the placeholder.
    /// </summary>
    /// <remarks>
    /// Websites in Party Finder ads aren't used: any player writes them, and fetching their icon would
    /// tell the ad's author the address of everyone who scrolls past it (decision with the user). Invites
    /// from ads are, as they are looked up only on Discord.
    /// </remarks>
    /// <param name="listings">The entry's listings, the one whose title it shows first.</param>
    /// <param name="links">The entry's links, http or https only, in the order shown.</param>
    public static IReadOnlyList<ImageSource> For(ImageKind kind, IEnumerable<CachedListing> listings, IReadOnlyList<Uri> links)
    {
        var banners = new List<ImageSource>();
        var partake = new List<ImageSource>();
        var sites = new List<ImageSource>();
        foreach (var listing in listings)
        {
            if (Picture(listing.Venue?.BannerUri) is { } banner)
                AddUpTo(banners, banner);
            foreach (var link in CuratedLinks(listing))
            {
                // A link to a person or a single event (Twitch, Partake) would show their icon, not the venue's
                // Stryker disable once String : an address with no path gets "/", so the home page is the same either way
                if (PlaceMarks.DiscordCode(link) is null && PlaceMarks.WebsiteKey(link) is not null)
                    AddUpTo(sites, new FaviconSource(new Uri(link.GetLeftPart(UriPartial.Authority) + "/")));
            }

            foreach (var name in listing.Attachments)
            {
                if (Partake.AttachmentUri(name) is { } uri)
                    AddUpTo(partake, new PictureSource(uri));
            }

            foreach (var written in listing.DescriptionImages)
            {
                if (Picture(written) is { } image)
                    AddUpTo(partake, image);
            }
        }

        var discords = new List<ImageSource>();
        foreach (var link in links)
        {
            if (PlaceMarks.DiscordCode(link) is { } code && IsInviteCode(code))
                AddUpTo(discords, new DiscordSource(code));
        }

        return kind == ImageKind.Icon ? [.. discords, .. sites, .. banners] : [.. banners, .. discords, .. partake];
    }

    /// <summary>The links a venue's owner or an event's host gave on FFXIV Venues or Partake; none for other sources.</summary>
    private static IReadOnlyList<Uri> CuratedLinks(CachedListing listing) => listing.Source switch
    {
        ListingSource.FfxivVenues => FfxivVenues.Links(listing.Venue?.Website),
        ListingSource.Partake => FfxivVenues.Links([.. listing.LinkTexts]),
        _ => [],
    };

    /// <summary>An http or https image address as a source; null for anything else.</summary>
    internal static PictureSource? Picture(string? written) =>
        Uri.TryCreate(written?.Trim(), UriKind.Absolute, out var uri) && WebLinks.IsWeb(uri) ? new PictureSource(uri) : null;

    /// <summary>Whether an invite code is safe to put in an API path: letters, digits and "-", at most 32.</summary>
    // Stryker disable once Equality : PlaceMarks.DiscordCode never gives an empty code, as an invite link needs a path
    internal static bool IsInviteCode(string code) =>
        code.Length is > 0 and <= 32 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static void AddUpTo(List<ImageSource> sources, ImageSource source)
    {
        if (sources.Count < MaxEach && !sources.Contains(source))
            sources.Add(source);
    }
}
