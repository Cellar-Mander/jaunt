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

`/jaunt` opens the venue list, as does the plugin installer's Open button. Jaunt reads venue ads while you browse the Party Finder's None category and lists those with an address, newest first. Each address has Copy address and, with [Lifestream](https://github.com/NightmareXIV/Lifestream) installed, Travel; each website link has Open. It also lists upcoming events from [Partake](https://partake.gg); Jaunt is not affiliated with or endorsed by Partake.

The list also shows venues from [FFXIV Venues](https://ffxivvenues.com), Party Finder ads from [xivpf](https://xivpf.com) and roleplay events from the [FFXIV RP Event Calendar](https://ffxiv-rp.org/), whose event listings are under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/).

`/jaunt config` opens the settings: how often Jaunt fetches listings, how times and dates are shown, and the venues you have hidden. The plugin installer's settings button opens them too.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) to build, test and release from source.
