# Jaunt

Find venues that are open now and travel to them from one list.

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for Final Fantasy XIV.

## Installing

In game, open `/xlsettings` → Experimental, add this to Custom Plugin Repositories and save:

```
https://raw.githubusercontent.com/Cellar-Mander/jaunt/main/repo.json
```

Then find Jaunt in `/xlplugins` and install it.

## Using it

`/jaunt` opens the listing browser, as does the plugin installer's Open button. On the left it lists venues and events by day: what is open now, then later today, tomorrow and the coming dates, in your local time. Venues show their current and next opening; one-off events show up to 30 days ahead. Select one to see its details on the right: when it is open, its address with Copy and, with [Lifestream](https://github.com/NightmareXIV/Lifestream) installed, Travel, its page, and for Party Finder ads the ad's text, with "Party Finder" in its sources opening the ad in the game's Party Finder while it is listed. Each listing shows an icon and a banner image, taken from FFXIV Venues, the venue's Discord server, the website it gives on FFXIV Venues or Partake, or the Partake event, and kept on disk so they aren't downloaded again. Listings come from Party Finder ads you browse in the None category, venue adverts shouted near you, [xivpf](https://xivpf.com), [FFXIV Venues](https://ffxivvenues.com), [Partake](https://partake.gg) and the [FFXIV RP Event Calendar](https://ffxiv-rp.org/); Jaunt is not affiliated with or endorsed by Partake.

The list also shows venues from [FFXIV Venues](https://ffxivvenues.com), Party Finder ads from [xivpf](https://xivpf.com) and roleplay events from the [FFXIV RP Event Calendar](https://ffxiv-rp.org/), whose event listings are under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/).

`/jaunt config` opens the settings: how often Jaunt fetches listings, how times and dates are shown, and the venues you have hidden. The plugin installer's settings button and the cog in the browser's title bar open them too.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) to build, test and release from source.

## License

Copyright (c) 2026 Cellar Mander.

Jaunt is free software under the GNU Affero General Public License, version 3 or (at your option) any later version (AGPL-3.0-or-later). See [LICENSE.md](LICENSE.md).
