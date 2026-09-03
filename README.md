# How to Fish: Ranked PvP (BepInEx mod)

Ranked PvP for *How to Fish*: 1v1, 2v2, 3v3, and free-for-all on small symmetric arenas with spawn cover, plus a
local fishing-rank ladder. Each player picks their own guns. Four maps: Rust, Nuketown, Shipment, Killhouse.

## Ranked (main menu)

A **Ranked** button sits under Character on the main menu. It opens a full-screen ranked page (My Rank, Rank Rewards,
Gameplay, Match Format, Maps) with your rank emblem, stats and match history. **Matchmake** creates an invite-only
Steam lobby (no save file is touched); **Solo practice** hosts an offline session.

Once you are in the world the **lobby screen** opens by itself: team columns (or a free-for-all grid) of player cards
with rank emblems and ready badges, an **Invite Friends** button that opens the Steam overlay, your loadout picker and
**Ready Up**. Each chosen gun gets an attachments block: sight, barrel (compensator / suppressor), bullet tier
(damage), extended magazine and laser, using the game's own attachment options for that gun.

## Sharing the mod and updates

Run `tools\make-release.ps1 -Notes "what changed"` after bumping `Plugin.Version`. It builds, produces
`dist\HowToFishRanked-<version>.zip`, publishes a GitHub release with the zip and the two DLLs at
`github.com/gpetersenco-create/how-to-fish-ranked`, and pushes `updates/manifest.json`. Friends install the zip once
(extract into the folder with `How to Fish.exe`); after that the mod checks the manifest at startup, downloads newer
DLLs itself, and shows "Mod updated, restart the game" on the main menu. Ranks live in
`BepInEx\config\HowToFish1v1.ranks.json` and are untouched by updates. `-NoPublish` skips GitHub.

## Killcam, skins, scoreboard

Dying to a player replays their first-person view from 5 s before the kill (aim zoom, scope overlay, shots with
sound, half speed for the final moment, a ghost of you). The match-winning kill replays for everyone at match end.
Each gun in a loadout has a skin slot (Neon Blue, Toxic, Magma, Ultraviolet, Gold, Ghost, Rainbow) visible to all.
Hold Tab for the scoreboard; the game's fish journal is disabled during matches. Friends who join see the same screen. The host picks mode and map at the bottom and presses
**Start Match** once everyone is ready; the screen closes at the countdown and comes back after the match. F5 hides
or shows it.

| Mode | Rules |
|---|---|
| 1v1, 2v2, 3v3 | Round-based. A round ends when a whole team is dead. First to 6 rounds. Teams auto-balance by join order; the host can move players. |
| Free-for-all | 2 to 8 players. First to 10 kills. 3-second respawn at the spawn farthest from everyone else. |

**Ranks** are stored per Steam account in `BepInEx\config\HowToFish1v1.ranks.json`. Win +20, loss -10 (free-for-all
loss -5), never below 0, a new tier every 100 points:
Master Baiter, Bottom Feeder, Small Fry, Chum Chucker, Reel Deal, Hook Line and Sinker, Big Fish, Apex Angler,
Kraken, Poseidon. Names are editable in the config (`RankNames`).

## Install (both players)

1. Install BepInEx 5.4.23.x (x64) into `...\Steam\steamapps\common\How to Fish\How to Fish\` (the folder that contains
   `How to Fish.exe`). Run the game once so BepInEx creates its folders.
2. Copy `HowToFish1v1.dll` and `HowToFish1v1.Core.dll` into `BepInEx\plugins\HowToFish1v1\`.
3. Start the game. `BepInEx\LogOutput.log` should contain `HowToFish1v1 0.1.0 loaded`.

## Play

1. The host creates a normal **Invite Only** multiplayer game and invites a friend. Both must have the mod.
2. Once both are in the world, the host presses **F5** (configurable) to open the 1v1 panel. The friend presses F5 too.
3. The host picks the map with the `<` `>` buttons. Everyone picks up to 2 guns and clicks **Ready up**.
   The host clicks **Start match**.
4. The arena appears and the island unloads. 3-second countdown, then fight. A kill ends the round; both players
   respawn on opposite pads with fresh guns and full magazines. Sides swap every round.
5. After a match the panel returns to the lobby: ready up again for a rematch (same or a different map), or the host
   clicks **Quit 1v1** to reload the island.

Saving is disabled while a match runs, so your real save is never touched. Fish, birds, bosses, NPCs, loot, hunger,
and autosave are all paused inside the arena.

## Config

`BepInEx\config\com.gavin.howtofish1v1.cfg`

| Key | Default | Meaning |
|---|---|---|
| `PanelKey` | F5 | Opens/closes the 1v1 panel |
| `RoundsToWin` | 6 | Round wins needed to take a team-mode match |
| `KillsToWin` | 10 | Kills needed to win a free-for-all |
| `CountdownSeconds` | 3 | Freeze time before each round goes live |
| `FfaRespawnSeconds` | 3 | Respawn delay in free-for-all |
| `RankNames`, `PointsPerTier` | ten names / 100 | The rank ladder |
| `DamageMultiplier` | 1.0 | Player-vs-player damage scale. 1.0 = full weapon damage (the game normally uses 0.25 between players) |
| `MaxLoadoutGuns` | 2 | Guns per player |
| `SoloDebug` | false | Allow starting with one player, for testing |
| `AutoHostOffline`, `AutoSoloMatch`, `AutoSoloMap` | false / false / 0 | Testing only: auto-host an offline game and script a solo match, logging every step |

## Build from source

```
dotnet build HowToFish1v1.sln -c Release
dotnet test tests/HowToFish1v1.Tests
```

The build copies both DLLs into the game's plugin folder. Override the game path with `-p:GameDir="..."`.
Layout: `src/HowToFish1v1.Core` (pure C# state machine + map layouts, unit tested), `src/HowToFish1v1` (the plugin),
`tests/`, `docs/superpowers/` (design spec, implementation plan, game internals reference).

## Two-player test checklist

- [ ] Both players see `mod OK` next to both names in the panel.
- [ ] Host cannot start until both are ready; the status line says why.
- [ ] Both players land on opposite pads facing each other, with their chosen guns and full ammo.
- [ ] Movement and shooting are frozen during the countdown and released on FIGHT.
- [ ] A kill shows "<name> wins the round", the score updates on both screens, both respawn, sides swap.
- [ ] Guns from the previous round do not litter the arena.
- [ ] Reaching 6 shows the match winner, then returns to the lobby; rematch works, including on another map.
- [ ] One player leaving mid-match returns the other to the lobby without errors.
- [ ] Quit 1v1 reloads the island for both players.
- [ ] After quitting to the menu, the save file's last-save time did not change during the match.
