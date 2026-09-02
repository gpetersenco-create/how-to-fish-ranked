# Ranked Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Ranked main-menu entry with 1v1 / 2v2 / 3v3 / free-for-all modes and a local fishing-rank ladder.

**Architecture:** Generalize the pure `MatchMachine` to N players with teams and kills; extend the broadcast state to
carry every player; add a rank service that persists points per Steam id; clone a native menu button that opens a
hosting panel; teach the host controller team spawns and free-for-all respawns.

**Tech Stack:** as the 1v1 plan (BepInEx 5, HarmonyX, FishNet 4.6 broadcasts, xunit). Adds a reference to
`com.rlabrecque.steamworks.net.dll` for the local Steam id.

**Spec:** `docs/superpowers/specs/2026-09-02-ranked-mode-design.md`

## Global Constraints

Same as `2026-09-01-1v1-mode.md`. Plugin version becomes `0.2.0` (the handshake value; both players must match).

---

### Task 1: Core: modes, teams, kills, ranks (TDD)

**Files:** `src/HowToFish1v1.Core/{MatchMode,PlayerSlot,EffectKind,MatchRules,MatchState,MatchMachine,RankLadder,ArenaLayout}.cs`,
tests `MatchMachineTests.cs`, `RankLadderTests.cs`, `ArenaLayoutTests.cs` (FFA spawns).

- [x] Tests written for: team alternation and cap, CanStart per mode, mode lock after start, team wipe round end,
  pad slot spacing, FFA kill/respawn/suicide/kills-to-win/leave rules, rank ladder tiers and deltas, FFA spawn spread.
- [x] Implementation: `MatchMachine.Kill(victim, killer, now)`, `MoveTeam`, `SetMode`, `PlayerRespawned`,
  `Effect{Kind, PlayerId}`, `RankLadder`, `ArenaLayout.TeamSpawn/FfaSpawns/YawToCenter`.
- [ ] `dotnet test` green. Commit `feat(core): modes, teams, free-for-all, rank ladder`.

### Task 2: Plugin: state, network, host, client, UI

**Files:** `Net/Messages.cs`, `Net/ModSerializers.cs`, `Net/ModNet.cs`, `ModState.cs`, `ModConfig.cs`,
`Arena/ArenaBuilder.cs`, `Match/{HostMatchController,ClientMatchView,RankService}.cs`,
`Patches/{KillAttribution,SavePatches}.cs`, `UI/{LobbyPanel,Hud,RankedMenu}.cs`, `Plugin.cs`, `DebugAutoTest.cs`, csproj.

- [x] Written as described in the spec section 3.
- [ ] `dotnet build` clean.
- [ ] Scripted solo run, mode 0 (1v1) and mode 3 (FFA): log shows arena, spawns on pads, self-kill handled
  (round reset in 1v1, respawn at a far spawn in FFA), rank applied at match end is not expected in the solo run
  (no match end); Quit returns to island.
- [ ] Menu run: log shows `Ranked button added to the main menu`; no exceptions.
- [ ] Commit `feat: ranked menu, team and free-for-all modes, rank ladder`.

### Task 3: Docs

- [ ] README: Ranked section (menu, modes, ranks, config keys). Commit `docs: ranked mode`.
