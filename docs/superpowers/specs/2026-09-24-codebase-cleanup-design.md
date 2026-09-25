# Codebase Cleanup & Reorganization — Design

Date: 2026-09-24 · Branch: `refactor/cleanup` (from `FactoryGame`)

## Goal
Reduce tech debt before the proof-of-concept: organization, readability, documentation, and
multiplayer correctness (all gameplay state flows through RPCs or StateUpdates).

## Decisions
- `gmp/` no longer needs to be a generic framework. It may reference game types. `TPRTS` is left to diverge.
- **Late join is out of scope.** RPCs remain the tool for discrete events (spawn, inventory changes).
  Nothing should make a future snapshot mechanism harder, but none is built.
- **Two phases.** Phase 1 is a *behavior-preserving* restructure. Phase 2 fixes multiplayer bugs.
  Each phase is verified with multi-instance headless runs so regressions are attributable.
- **Conventions:** public fields/properties stay **camelCase**; types and methods PascalCase;
  private fields camelCase or `_camelCase` (match surrounding code). **No namespaces** — all
  types stay in the global namespace (user preference); folders provide the organization.
- **Godot constraints:** every GMPO class derives from its Godot node type (`Node3D`, `Node`).
  `GMPObject` is a C# interface Godot never sees — `[Export]` on it is meaningless, and Godot never
  invokes interface default methods like `_Ready`. Box3D types are GDExtension-only; wrappers derive
  `Node3D` and reach physics through `Call`/`Set`/`Get`. No shared base class is possible; shared
  logic goes in static helpers / interface methods.

## Target layout
```
gmp/core/     Global, GameInfo, Logging, Console, ColorDict
gmp/net/      Network, ENetNetwork, SteamNetwork, Lobby, RPCManager,
              PlayerInfo
gmp/sync/     GMPObject(+GMPOInitData), SyncHelpers, TransformSyncState,
              GameWorld.cs / .Spawning.cs / .StateSync.cs / .Debug.cs (partial class)
gmp/objects/  GMPONode3D, GMPOBox3DBody, GMPOBox3DCharacter, GMPOSingleton
gmp/ui/       MainMenu, LobbyDebug, OptionsMenu (+scenes, theme)
game/core/    GameBootstrap, GameResources, BuildGrid
game/player/  FactoryPlayer (+ .Interaction/.Grab/.Equipment partials), Inventory, InventoryUI, HUD
game/items/   FactoryItem + defs, ItemTags, world/, held/
game/machines/ ConveyorBelt, ConveyorBeltUpSlope, InHandBuilding, placeables
game/props/   BasicButton, ObjectSpawner
game/levels/  (unchanged)
game/dev/     test_* scenes, surviving examples, FluidSim experiment
game/assets/  (+ shaders/)
```
File moves carry their `.uid` sidecars; `res://` paths in `.tscn`/`.tres`/`project.godot`/C# string
literals are rewritten.

## Phase 1 — behavior-preserving restructure
1. **Game smoke test.** Extend the headless test harness with a `--test-game` mode: host starts the
   game once all peers joined; after load each peer runs N seconds, then asserts FactoryPlayer count ==
   peers, inbound state ticks > 0, and no C# exceptions; prints `TEST_RESULT:PASS|FAIL`.
2. **Dead code & stale files.** Delete: `GMPOStaticBody3D`, `GMPORigidBody3D`, `GMPOCharacterBody3D`,
   `GMPOFPSPlayer` and examples that only use them; `lobby_lan_debug.tscn`, `lobby_steam_debug.tscn`;
   `Tool.cs`, `FactoryMachine.cs`, `new_resource.tres`, `GMPFramework.csproj.old`; LiteNetLib package
   ref; `GameInfo.bulk*`; `GameWorld` duplicate grid fields, `waitTicks`, unreachable code, empty
   `else if(false)`; commented-out code blocks; broken `GameResources.Items` entries; fix `Exmaple*`
   typos; reconcile `inventoryItems/*.tres` vs `factoryItems/*.tres`.
3. **Folder moves** to the target layout.
4. ~~Namespaces~~ — dropped (user preference).
5. **GMPO consolidation.** Shared `TransformSyncState` + `SyncHelpers.Interpolate(...)`; each class passes
   its *current* rate (snap / 10·delta etc.) so behavior is unchanged. Strip `[Export]` from the
   interface. Rename interface `_Ready` → `ApplyProcessMode()`, called only where it effectively runs
   today (GMPOBox3DCharacter's manual copy).
6. **GameWorld split + seams.** Split into partial files. Move FactoryPlayer spawn/inventory seed and
   level selection from `GameWorld.Init/Preload` into `game/core/GameBootstrap`. Move `gmp/core/Grid.cs`
   → `game/core/BuildGrid.cs`.
7. **FactoryPlayer split** — done as partial-class files (`FactoryPlayer.Interaction/.Grab/.Equipment.cs`)
   with one explicit `_UnhandledInput` dispatcher; a child-node version was tried first and rejected
   (implicit input order, scroll rule split across files).
8. **String hygiene.** `nameof()` for RPC method names; cache hard-coded node paths as fields.

## Phase 2 — multiplayer correctness (separately planned after Phase 1)
- Inbound state: per-sender queue (or merge-by-object) instead of last-tick-wins.
- `Lobby.OnDoneLoading`: restore the all-members gate.
- Session reset: clear `GameWorld` static state on `LeaveLobby`.
- RPC hardening: `[RPC]` attribute allow-list, null-method guard, optional sender/authority check.
  Pass the transport-level sender (`from`, already known in `HandleRPC`) into handlers that want it,
  and add a targeted send, `RPCManager.RPCTo(ulong peer, ...)`, built on `Lobby.Send(to, ...)`.
- **Contested actions — authority-arbitrated requests** (decided 2026-09-24). Problem: RPCs go to
  all peers incl. self and the self copy is applied synchronously, so two peers acting at once each
  apply their own action first; other peers see the two RPCs in arbitrary order. Rule: any action
  where only one of several simultaneous attempts may succeed goes through the target object's
  `authority` (the host, for host-spawned world objects):
  1. the acting peer calls `RPCTo(target.authority, target, nameof(RequestX), args)`;
  2. the authority validates against its own state (first request wins, later ones are dropped),
     using the transport `from`, never a caller-supplied peer id;
  3. the authority broadcasts the outcome with `RPCManager.RPC(target, nameof(ApplyX), [winner, ...])`,
     which every peer (incl. authority and requester) applies.
  Provide one reusable helper for this request → validate → broadcast pattern so interactables don't
  re-implement it. Local, purely cosmetic feedback (press animation) may play immediately; gameplay
  effects only in `ApplyX`. Where a rule allows it, prefer order-independent effects instead (set vs.
  toggle; deterministic spawn ids so duplicate spawns collapse). Long-lived interactions (grab) use
  ownership transfer via `GameWorld.Claim`, arbitrated the same way. First conversion: `BasicButton`
  (currently calls `OnPressed()` locally with no RPC), which also fixes the `ObjectSpawner` on/off
  desync. Known limit: if an arbitrating authority disconnects, its objects have no arbiter (no host
  migration; consistent with no-late-join scope).
- GMPO: actually apply `pauseable` via `ApplyProcessMode()` from `Init`; unify interpolation to one
  exported, frame-rate-independent rate.
- Game: network inventory changes and equipped/held item (`FactoryPlayer.SyncInventory` exists but has
  no callers); implement `GameWorld.Claim` ownership for grabbed bodies (via the arbitration pattern above);
  `ObjectSpawner` on/off is fixed by the `BasicButton` conversion.
- Document the synchronous self-loopback guarantee that `SpawnScene`-then-read relies on.
- Findings logged during Phase 1 (not yet fixed): `GMPOBox3DBody.Enable()` sets `enabled=false`;
  aiming from an item to a button leaves the item's hover name label visible; the level root spawns
  as a non-GMPObject and logs a warning on every peer each game start.

## Documentation
Code comments only for now (user decision 2026-09-24): XML doc comments on public APIs touched in
Phase 2. No standalone Markdown docs (architecture / networked-objects guides are skipped).

## Verification (every task)
`dotnet build` with 0 errors and no new warnings; headless editor import (`--headless --editor --quit`)
with no missing-resource errors; `tests/test_lan_lobby.ps1` and the new game smoke test pass with 3 peers.
