# Changelog

All notable changes to Jaunt. Versions match the plugin version shown in `/xlplugins`.

## Unreleased

- Venue list (`/jaunt`): Party Finder venue ads with an address, newest first, read while you browse the Party Finder's None category. Copy an address, travel there with Lifestream if it is installed, or open the venue's website.
- Venues from [FFXIV Venues](https://ffxivvenues.com) in the venue list, fetched when you open it at most every 8 hours, with their address, website, Discord and whether they are open 24/7.
- Settings window (`/jaunt config`): a refresh cooldown for fetched listing sources (default 10 minutes; some sources wait longer), 12- or 24-hour times and a date format, Clear cached listings, and a list of hidden venues with Un-hide buttons. Settings save as you change them. Listings that aren't favourited, visited or hidden are dropped from the cache a month after they were last seen.

- Groundwork for reading venue addresses from Party Finder ads: Jaunt now knows worlds, districts and the shorthand players use for them. Nothing visible changes yet.
- Groundwork for opening a venue's website: Jaunt can now find the links in Party Finder ads, including ones written without `https://` such as `discord.gg/…` or `venue.carrd.co`. Nothing visible changes yet.
- Groundwork for copying addresses and travelling to venues: Jaunt can now write an address as text to copy and turn it into a Lifestream destination, and keeps each Party Finder ad's text in its listing cache. Nothing visible changes yet.
