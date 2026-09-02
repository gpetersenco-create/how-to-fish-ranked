# How to Fish 1v1 mod: Ranked Mode Design

Date: 2026-09-02
Status: approved in chat ("Build it")
Builds on: `2026-09-01-1v1-mode-design.md` (the 1v1 mode, arenas, networking, patches)

## 1. Goal

A "Ranked" entry on the game's main menu that hosts a friends-only lobby straight into the arena mode, with four
modes (1v1, 2v2, 3v3, Free-for-all), all tracked by a local rank ladder with fishing-pun rank names.

## 2. Decisions

| Topic | Decision |
|---|---|
| Menu | A "Ranked" button cloned from an existing main-menu button (native look), placed after Character. Opens the Ranked panel. |
| Hosting | Host Ranked creates a normal invite-only Steam lobby without writing a save file. Joining is by Steam invite, as in the base game. |
| Modes | 1v1 / 2v2 / 3v3: round-based, round ends when a whole team is dead, first to 6 rounds. FFA: 2 to 8 players, first to 10 kills, 3 s respawn at the spawn farthest from others. |
| Teams | Auto-assigned alternating by join order; host can move a player to the other team in the panel. Teammates share the team pad, spaced 2 m apart. |
| Ranking | Local per Steam account. Points start at 0. Team modes: win +20, loss -10. FFA: winner +20, others -5. Floor 0. Rank tier every 100 points. |
| Rank names | Master Baiter, Bottom Feeder, Small Fry, Chum Chucker, Reel Deal, Hook Line and Sinker, Big Fish, Apex Angler, Kraken, Poseidon. Editable in config (comma-separated). |
| Existing F5 flow | Still works inside any session; the match panel is the same panel with a mode picker. |

## 3. Architecture changes

- **Core.MatchMachine** generalized: `List<PlayerSlot> Players` (max 8), `Mode`, `Team` per slot (0/1; FFA ignores),
  `Kills` per slot, team scores, `KillsToWin`. Effects become `Effect { Kind, PlayerId }` with kinds
  `BuildArena`, `DestroyArena`, `ResetPlayers` (all), `RespawnPlayer` (FFA, one player, after delay), `Broadcast`.
  Round-end rule in team modes: all present players of one team `DeadThisRound`. FFA: `Kill(victim, killer)` adds a kill
  to the killer (a suicide or unknown killer counts nothing) and schedules `RespawnPlayer(victim)`.
- **Core.ArenaLayout**: `TeamSpawn(side, index, count)` returns the pad position with z spacing of 2 m; `FfaSpawns`
  is 6 points per map (the two pads plus four spread points near cover), each facing the map center.
- **Core.RankLadder**: pure functions `TierFor(points)`, `Apply(points, result)`, names from a string array.
- **Net**: `MatchStateBroadcast` carries `Mode`, `KillsToWin`, and up to 8 player entries (id, name, team, score,
  kills, ready, hasMod, rankPoints, loadout). `LoadoutBroadcast` gains `RankPoints`. New `MatchResultBroadcast`
  (host to all at match end): winning team or winner id and mode, so every client applies its own rank change.
- **Match.HostMatchController**: team spawn assignment, FFA respawn coroutine (3 s, farthest spawn from alive players),
  kill attribution via a postfix on the hit RPC (last attacker per victim).
- **Match.RankService**: loads/saves `BepInEx/config/HowToFish1v1.ranks.json` keyed by Steam id; applies results.
- **UI.RankedMenu**: clones a menu button; the panel shows the rank card, mode, map, Host Ranked. After hosting,
  `PendingHostSetup` (mode, map) is applied when the local player exists: open the match, set mode and map.
- **UI.LobbyPanel**: mode picker (host), team list with Move buttons, ranks next to names, auto-opens on clients when
  a Lobby state arrives during a ranked session; FFA shows a kill table.
- **UI.Hud**: FFA scoreboard shows "YOU 4 kills, leader NAME 6"; team modes show team scores; match end shows the rank change.
- **Patches**: saves stay blocked for the whole ranked session (`ModState.RankedSession`), not only while a match runs.

## 4. Verification

- Unit tests for the generalized machine (team wipe, FFA kills, respawn effect, team moves) and RankLadder.
- Scripted solo run (existing debug flags) on FFA and 1v1 to prove the loops still work with the new machine.
- Menu button verified by launching to the main menu and reading the log plus a screenshot if the user provides one.
