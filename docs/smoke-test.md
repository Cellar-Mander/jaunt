# In-game smoke test

Run before merging anything that changes what the plugin does in-game. Each feature adds its cases here.

Setup: deploy with `dotnet buildingway dev-plugin deploy <task-ref>` (see "Testing in-game" in [CONTRIBUTING.md](../CONTRIBUTING.md)), log in, open `/xllog`. Release the dev folder with `dotnet buildingway dev-plugin release` when done.

## Loading

1. `/xllog` shows `Jaunt <version>+<commit> loaded`, with this checkout's commit.
2. `/jaunt` opens the settings window; running it again closes it.
3. `/xlplugins` → Installed Plugins shows a settings button for the plugin, which opens the same window.
4. Change a setting, disable and re-enable the plugin: the setting is kept.
