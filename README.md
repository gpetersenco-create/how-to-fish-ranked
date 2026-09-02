# How to Fish: 1v1 Mode (BepInEx mod)

Round-based 1v1 PvP for *How to Fish*. One kill wins the round, first to 6 rounds wins the match, played on small
symmetric arenas with spawn cover. Each player picks their own guns. Four maps: Rust, Nuketown, Shipment, Killhouse.

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
| `RoundsToWin` | 6 | Round wins needed to take the match |
| `CountdownSeconds` | 3 | Freeze time before each round goes live |
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
