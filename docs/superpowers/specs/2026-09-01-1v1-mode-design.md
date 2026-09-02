# How to Fish — 1v1 Mode Mod: Design

Date: 2026-09-01
Status: approved in brainstorming, pending written-spec review

## 1. Goal

A BepInEx 5 mod for *How to Fish* (Steam app 4001890, Unity 6000.4.4f1, Mono,
FishNet 4 over Steam) that adds a round-based 1v1 mode played on a small,
symmetric, Call-of-Duty-style arena. Two players who both have the mod join a
normal invite-only lobby, pick loadouts, and fight rounds: one kill wins the
round, first to 6 rounds wins the match.

## 2. Decisions made with the user

| Topic | Decision |
|---|---|
| Rules | Classic CoD 1v1: kill wins the round, full reset each round, first to 6. 3 s countdown between rounds. |
| Loadout | Each player picks their own guns (up to 2) from every weapon the game has, before the match. Fixed for the match. |
| Map | Code-built arena from Unity primitives, floating above the ocean. No Unity Editor asset bundle. |
| Networking | Host-authoritative match logic; FishNet `Broadcast` structs for mod messages. Both players need the mod. |
| Entry | Host presses F5 (configurable) in a running multiplayer session to open the 1v1 panel. |

## 3. Constraints from the game (verified in decompiled code)

- Mono runtime, so BepInEx 5.4.23 + HarmonyX work directly. The plugin references
  the game's `Assembly-CSharp.dll`, `FishNet.Runtime.dll`, and `UnityEngine.*` at build time.
- Islands are additive Unity scenes (build index 1..N); the main scene (index 0)
  is persistent and holds all managers, water, canvases. `IslandManager.UnloadIslands()`
  unloads every island scene. A BepInEx mod cannot add build-index scenes.
- The ocean is an infinite, re-tiling water grid. Water height is
  `WaterManager.WaterHeight`.
- PvP already works: `ServerSettings.UseFriendlyFire` (SyncVar, default true)
  gates player-on-player damage on both client (`PlayerVitals.LocalHit`) and
  server (`Server.RpcLogic___HitPlayer___2449261505`).
- Player-on-player damage is multiplied by `PlayerVitals._playerDamageMultiplier`
  (0.25f) in `LocalHit`.
- Server death funnel: `PlayerDying.ServerDie(Vector3)`. Death is not an RPC;
  clients react to `_syncedHealth` reaching 0. Reset is
  `PlayerVitals.ServerResetVitals()` (health 100 means "respawned" on clients,
  which triggers `PlayerDying.ResurrectEffect(true)` and a teleport to
  `SpawnManager.PlayerSpawnPos`).
- Position is owner-authoritative. Move a player with
  `Server.Instance.TeleportPlayer(player, pos, yaw)` (ServerRpc, no ownership
  required), which becomes a TargetRpc to the owner and `Player.LocalTeleport`.
- Give a gun: `Server.Instance.BuyItem(itemId, player, player.Holding.HeldItem, pos, rot, isFree: true)`
  or, on the server, `Instantiate` + `item.SetSyncedHolder(player, forced: true)` +
  `Spawn` + `player.Holding.SetHeldItem(item)` / `player.Inventory.AddItem(slot, item)`.
- Weapons are `Item`s with a non-null `.Weapon`; enumerate via
  `Resources.LoadAll<Item>("Items")`. Identity is `Item.ID` (byte) and `Item.name`.
- Ammo (`Weapon.Ammo`) is client-local, private setter, no server validation.
- HUD text: instantiate `PlayerUI.CanvasTextPrefab` under `PlayerUI.FXCanvasTrans`.
- Saves: `SaveSystem.SaveServer/SaveLocal/DeleteServer` are the only file writers.
- Full reference: `docs/superpowers/notes/game-internals.md`.

## 4. Architecture

```
HowToFish1v1.dll (BepInEx plugin)
├── Plugin            BepInEx entry: config, Harmony.PatchAll, wires singletons
├── Core (pure C#, no Unity)
│   ├── MatchState / MatchPhase      snapshot of the match
│   ├── MatchRules                   roundsToWin, countdownSeconds, roundEndSeconds, matchEndSeconds
│   ├── MatchMachine                 state machine: events in, new state + effects out
│   └── ArenaLayout                  list of ArenaBox (center, size, kind) + 2 spawns
├── Net
│   ├── Messages                     FishNet broadcast structs (section 5)
│   └── ModNet                       register/send helpers, handshake tracking
├── Match
│   ├── HostMatchController          host-only: feeds MatchMachine with real time, kills, joins/leaves; applies effects
│   ├── ClientMatchView              every client: latest MatchState from host, drives HUD
│   └── LoadoutService               weapon catalog, give/clear loadout (server-side), local ammo refill
├── Arena
│   ├── ArenaBuilder                 builds/destroys arena GameObjects from ArenaLayout
│   └── ArenaMaterials               3 URP Lit materials
├── Patches (Harmony)
│   ├── CombatPatches                friendly-fire force-on, damage multiplier
│   ├── DeathPatches                 ServerDie postfix (kill event); ResurrectEffect spawn override; no give-up respawn
│   ├── SuppressionPatches           fish/birds/albatross/boss/NPC/loot/island triggers/tutorial/hunger/autosave
│   ├── SavePatches                  block SaveSystem writes while active
│   └── InputPatches                 BlockInputs during countdown / panel open
└── UI
    ├── Hud                          scoreboard + center banner (game text prefab)
    └── LobbyPanel                   IMGUI panel: players, loadout picker, Ready, Start, Rematch, Quit
```

All game access goes through `Match`, `Arena`, `Patches`, `UI`. `Core` never
references UnityEngine or the game, so it is unit-testable.

## 5. Networking

FishNet broadcasts (`ServerManager.Broadcast<T>` / `ClientManager.Broadcast<T>`,
`RegisterBroadcast<T>`), structs implementing `IBroadcast`. Serialization is
FishNet's default (primitive fields, strings, and arrays of primitives).

| Message | Direction | Fields |
|---|---|---|
| `HelloBroadcast` | client to host, on client start | modVersion (string) |
| `LoadoutBroadcast` | client to host | itemIds (byte[] up to 2), ready (bool) |
| `LobbyActionBroadcast` | client to host | action (byte: Start, Rematch, Quit). Host-only actions are validated server-side. |
| `MatchStateBroadcast` | host to all (reliable) | phase (byte), roundNumber, scoreA, scoreB, playerAId, playerBId (OwnerId), spawnAIsLeft (bool), phaseEndsAtTick (uint), lastRoundWinnerId, matchWinnerId, loadoutA/B (byte[]), readyA/B, bothHaveMod (bool), statusText (string) |
| `ArenaBroadcast` | host to all | build (bool), returnIslandIndex (byte) |

Handshake: host records `HelloBroadcast` per connection. `Start` is refused unless
`PlayerManager.Players.Count == 2` and both connections said hello with the same
modVersion. The `Lobby` state carries `bothHaveMod` and `statusText` so the panel can explain.

Clients build/destroy the arena on `ArenaBroadcast`; the host builds it itself
after `IslandManager.UnloadIslands()`. Layout is deterministic code, so all
peers produce identical geometry with no extra data.

## 6. Match state machine (Core.MatchMachine)

Phases: `Inactive`, `Lobby`, `Countdown`, `Live`, `RoundEnd`, `MatchEnd`.

Inputs (events): `Open`, `SetLoadout(id, itemIds, ready)`, `Start`, `Tick(now)`, `Kill(victimId)`,
`PlayerLeft(id)`, `Rematch`, `Quit`.

Outputs (effects, applied by HostMatchController): `BuildArena`, `DestroyArena(returnIsland)`,
`ResetPlayers(spawnAssignments)`, `GiveLoadouts`, `Broadcast(state)`.

Rules:
- `Open` from `Inactive` goes to `Lobby` (no arena yet).
- `Start` allowed only in `Lobby` with 2 players, both ready, both have the mod
  (or `SoloDebug` with 1 player). Effects: `BuildArena` (first time), round 1,
  scores 0-0, then `ResetPlayers` + `GiveLoadouts`, then `Countdown`.
- `Countdown` lasts `rules.CountdownSeconds` (3). Inputs frozen. Ends in `Live`.
- `Live`: first `Kill(victim)` makes the other player the winner, score++, then `RoundEnd`.
  A second `Kill` in the same round is ignored. Kills during
  `Countdown`/`RoundEnd`/`MatchEnd` are ignored for scoring.
- `RoundEnd` lasts `rules.RoundEndSeconds` (2). Then, if a score reached
  `rules.RoundsToWin` (6), `MatchEnd`; else swap spawn sides, round++, `ResetPlayers`, `GiveLoadouts`, `Countdown`.
- `MatchEnd` lasts `rules.MatchEndSeconds` (5), then `Lobby` (scores reset, ready flags cleared, arena stays, players remain on their pads).
- `Rematch` in `Lobby` behaves as `Start` (players must re-ready; loadouts persist).
- `PlayerLeft` in `Countdown`/`Live`/`RoundEnd`/`MatchEnd` goes to `Lobby` with statusText "Player left".
- `Quit` (host only) from any phase: `DestroyArena(returnIsland)`, `Inactive`.
  Non-host `Quit` only closes their panel.

Time is passed in as seconds (double) so tests don't depend on Unity.

## 7. Round reset procedure (host, per player)

1. If the player has a `DeadPlayer` ragdoll: `deadPlayer.DestroyItem(7)`.
2. Destroy every item the player holds or has in inventory, plus any loadout
   items the service spawned earlier that are now loose (tracked by NetworkObject).
3. Assign spawn: player A gets the left pad when `spawnAIsLeft`, else right; B the other.
   Clients know the assignment from `MatchStateBroadcast`; `DeathPatches.ResurrectEffect_Prefix`
   substitutes the assigned spawn for `SpawnManager.PlayerSpawnPos/Rot` when the
   mode is active, so the game's own respawn teleport lands on the right pad.
4. `player.Vitals.ServerResetVitals()` (health/fullness 100, poison 0).
5. `Server.Instance.TeleportPlayer(player, spawnPos, spawnYaw)` one frame later
   (covers the alive-player case where no resurrect happens).
6. Give loadout: spawn each chosen weapon server-side, the first into hands,
   the second into inventory slot 0. On each client, `LoadoutService` sets
   `Weapon.Ammo = AmmoPerMag` on the local player's weapons when `Live` begins.

`Server.RpcLogic___RespawnPlayer___2210451296` is never invoked by the mod, so
`ServerDropAll` never runs. The "give up" input while dead is blocked
during the mode (prefix on `PlayerDying.LocalRespawn` returning false).

## 8. Arena (Core.ArenaLayout + Arena.ArenaBuilder)

Origin: `(0, WaterManager.WaterHeight + 4, 0)` at build time; layout coordinates
are relative to the origin. Dimensions in meters. X is the long axis (spawn to spawn),
Z the short axis. Y=0 is the floor top.

- Floor: 40 x 28, thickness 1.
- Spawn pads: 6 x 6 at x = +/-17, z = 0, raised 0.2, with a low back wall.
- Central tower: 8 x 8 footprint at origin; ground level open on all four sides
  (four 1 x 1 corner pillars), second floor slab at y = 3 (thickness 0.3) with
  waist-high parapets (height 1) on all sides, reached by two 1.5-wide ramps
  on the +/-Z sides (rotated boxes).
- Containers (2.4 wide x 2.6 tall x 6 long, long axis along Z): at (+/-9, 0, +/-7). Mirror-symmetric.
- Crates (1.5 cube): at (+/-4, 0, +/-10), (+/-13, 0, 0), (0, 0, +/-12).
- Side walkways: 3-wide strips along z = +/-12.5 at y = 2, from x = -12 to 12,
  supported by pillars, with waist-high parapet on the inner edge; stairs (rotated
  boxes) at both ends.
- Perimeter: invisible walls (no renderer) 6 high around the floor edge, plus an
  invisible ceiling at y = 12 to keep everything in bounds.
- Spawns: A at (-17, 0.4, 0) facing +X (yaw 90), B at (+17, 0.4, 0) facing -X (yaw 270).

Every box: `GameObject.CreatePrimitive(Cube)`, `BoxCollider`, layer = first
layer set in `GameInfo.LevelLayer`, parented under an `Arena` root. Root
has an `Island` component (`_islandSize` set to 30 via reflection). Materials:
concrete grey (floor, tower, walkways), rust orange (containers), dark steel
(crates, parapets), shader `Universal Render Pipeline/Lit` via `Shader.Find`,
falling back to `Standard` if not found.

`ArenaLayout` is pure data; tests check X-mirror symmetry of cover, spawn
separation, all boxes inside the perimeter, and nothing below the floor.

## 9. Harmony patch inventory

| Purpose | Target | Kind |
|---|---|---|
| Force PvP on | `ServerSettings.UseFriendlyFire` getter | Postfix returns true while active |
| Full damage | `PlayerVitals.LocalHit` | Prefix: `damage = round(damage * cfg.DamageMultiplier / 0.25)` while active (default 1.0 = full weapon damage) |
| Kill detection | `PlayerDying.ServerDie` | Postfix calls `HostMatchController.OnKill(player)` |
| Spawn override | `PlayerDying.ResurrectEffect` | Prefix swaps `SpawnManager.PlayerSpawnPos/Rot` for the assigned spawn; Postfix restores |
| No give-up respawn | `PlayerDying.LocalRespawn` | Prefix returns false while active |
| Freeze inputs | `Player.BlockInputs` getter | Postfix returns true during Countdown, RoundEnd, MatchEnd, or while LobbyPanel is open |
| No fish | `CreatureManager.TickUpdate` | Prefix false |
| No birds | `BirdManager.AddFlyingBird`, `BirdManager.ServerTickUpdate` | Prefix false |
| No albatross | `AlbatrossSpawner.TickUpdate` | Prefix false |
| No boss | `BossManager.InitializeBossFight` | Prefix false |
| No NPC | `NPCManager.AddNpc` | Prefix false |
| No loot | `ItemSpawner.Start` | Prefix false |
| No island hop | `IslandSpawner.OnTriggerEnter` | Prefix false |
| No tutorial | `TutorialManager.AddTutorial` | Prefix false |
| No hunger | `PlayerVitals.LowerFullnessTick` (private, called from its `TickUpdate`) | Prefix false |
| No autosave | `AutoSaver.Start` | Prefix false |
| No save writes | `SaveSystem.SaveServer`, `SaveSystem.SaveLocal`, `SaveSystem.DeleteServer` | Prefix false while active |
| No saved clutter | `SaveManager.LoadWorldItems` | Prefix false while active |

"While active" means `MatchPhase != Inactive` on that peer. All patches are gated
this way so normal play with the mod installed is unchanged.

## 10. UI

- **LobbyPanel** (IMGUI, `OnGUI`): toggled with the hotkey. Shows both players,
  mod status, a weapon list with checkboxes (max 2), a Ready toggle; the host also sees
  Start / Rematch / Quit. Unlocks the cursor while open; inputs are blocked.
- **Hud**: two `TextMeshProUGUI` instances from `PlayerUI.CanvasTextPrefab` under
  `PlayerUI.FXCanvasTrans`: a top-center scoreboard `YOU 3 - 2 NAME` and a center banner
  for `3 / 2 / 1 / FIGHT`, `NAME wins the round`, `NAME wins the match`. Hidden
  when `Inactive`.

## 11. Configuration (BepInEx config file)

`PanelKey` (F5), `RoundsToWin` (6), `CountdownSeconds` (3), `DamageMultiplier` (1.0),
`MaxLoadoutGuns` (2), `SoloDebug` (false: allow Start with one player for testing).

## 12. Project layout and build

```
how to fish 1v1 mod/
├── HowToFish1v1.sln
├── src/HowToFish1v1/HowToFish1v1.csproj   netstandard2.1, refs BepInEx + game DLLs via GameDir property
├── tests/HowToFish1v1.Tests/              xunit, refs Core only
├── docs/superpowers/{specs,notes,plans}/
└── README.md                              install steps for both players
```

`GameDir` defaults to `C:\Program Files (x86)\Steam\steamapps\common\How to Fish\How to Fish`.
A post-build step copies the DLL to `$(GameDir)\BepInEx\plugins\`. BepInEx
5.4.23.5 (zip already in Downloads) is extracted into `GameDir` as a setup step.
BepInEx assemblies are referenced from the extracted `BepInEx\core` folder.

## 13. Testing

- Unit (xunit): `MatchMachine` transitions, scoring, side swap, match end,
  ignored kills, player-left handling, rematch; `ArenaLayout` symmetry and bounds.
- In-game solo (SoloDebug): plugin loads without errors in the BepInEx log, panel
  opens, weapon list populated, Start unloads the island and builds the arena, HUD
  visible, countdown freezes input, loadout appears in hands with full ammo,
  Quit restores the island.
- Two-player (user + friend, checklist in README): handshake, both loadouts,
  kill leads to round win and reset on the correct pads with side swap, match to 6, rematch,
  disconnect handling, real save untouched afterwards.

## 14. Implementation notes (added after in-game verification)

- **Maps.** The user asked for spawn cover and several maps mid-implementation. `ArenaLayout.Create(mapIndex)` now
  offers Rust, Nuketown, Shipment, and Killhouse. Every map has a shield wall or building in front of each pad, and a
  unit test proves no straight line exists between any two points on the two pads. The host picks the map in the panel;
  the index travels in `ArenaBroadcast` and `MatchStateBroadcast`, and changing the map after a match rebuilds the arena.
- **Arena height.** The game's drowning check uses the wave-adjusted water height, so the floor sits at
  mean water + crest amplitude (read from the water material) + 4 m.
- **Vitals reset.** `ServerResetVitals` restores the prefab's serialized 50 hp / 25 fullness, so the mod writes
  100 to both SyncVars afterwards and clears fire and poison.
- **Teleports.** `Player.RPCTeleport` is patched to the instant path while the mode is active and during the island
  return; the deferred `MovePosition` path lost the race against island unloads. The arena is built before the island
  unloads and destroyed only after the island has reloaded.
- **Spawn clearance.** Players are teleported 1.6 m above the pad and guns are instantiated above the head; overlapping
  colliders had been flinging players across the map.
- **Countdown deaths** now end the round, so nobody enters the next round dead.
- **Scripted verification.** Debug config flags `AutoHostOffline` and `AutoSoloMatch` run a full solo match from the
  main menu and log every step, which is how all four maps were verified without a second player.

## 15. Out of scope

Attachment upgrades on loadout guns, more than two players, more than one map,
ranked stats, spectating, custom models/textures, Steam Workshop packaging.
