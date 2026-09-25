# Cleanup Phase 1 (Behavior-Preserving Restructure) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the GMPFramework/FactoryGame codebase for maintainability without changing runtime behavior.

**Architecture:** Add an end-to-end headless game smoke test first, then delete dead code, move files into the target layout consolidate the duplicated GMPO sync logic into shared helpers, and split `GameWorld` into partial files with game-specific bootstrap moved into `game/`. Every task must leave the build green and all headless tests passing.

**Tech Stack:** Godot 4.7.2 mono (C#, .NET 8), Nerdbank.MessagePack + PolyType, Box3D GDExtension (reached via `Call`/`Set`/`Get`), Steamworks.NET, PowerShell test harness.

**Spec:** `docs/superpowers/specs/2026-09-24-codebase-cleanup-design.md`

## Global Constraints

- **Behavior-preserving.** Phase 1 must not change gameplay or network behavior. If you find a bug, do NOT fix it — add it to the "Phase 2 findings" list in your report.
- Public fields/properties stay **camelCase**. Types/methods PascalCase. Do not mass-rename existing members.
- Godot constraints: GMPO classes derive from a Godot node type (`Node3D`/`Node`); `GMPObject` is a plain C# interface that Godot never sees (`[Export]` on it does nothing; Godot never calls interface default methods). Box3D types are GDExtension-only — wrappers derive `Node3D` and use `Call`/`Set`/`Get`. A Godot C# script's class name must equal its file name.
- Never override `_Ready()` in a GMPO class and chain to a `GMPObject` interface method named `_Ready` (infinite recursion). Per-node setup goes in `AfterInit()`.
- Move files with `git mv`, always together with their `.uid` / `.import` sidecars.
- Match surrounding code style and comment density. No new features.
- Project root: `C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework`. Branch: `refactor/cleanup`.
- Godot console binary: `C:\Users\steph\OneDrive\Documents\godot\Godot_v4.7.2-stable_mono_win64_console.exe`.
- Commit message trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Verification Suite (run at the end of every task)

```bash
cd "/c/Users/steph/OneDrive/Documents/godot/projects/gmp-framework"
dotnet build 2>&1 | tail -3          # expect: 0 Error(s); warning count must not increase vs. previous task
```
```powershell
& ".\tests\check_scenes.ps1"         # (created in Task 0) expect: ALL SCENES LOADED
& ".\tests\test_lan_lobby.ps1"       # expect: ALL TESTS PASSED
& ".\tests\test_lan_lobby.ps1" -Game # (created in Task 0) expect: ALL TESTS PASSED
```
Baseline before Task 0: build 0 errors / 6 warnings; lobby test passes 3/3.

---

### Task 0: Headless game smoke test + scene-load check

**Files:**
- Modify: `gmp/core/GameWorld.cs` (add inbound tick counter)
- Modify: `gmp/ui/LobbyDebug.cs` (add `--test-game` mode)
- Modify: `tests/test_lan_lobby.ps1` (add `-Game` switch, exception scan)
- Create: `tests/check_scenes.gd`, `tests/check_scenes.ps1`

**Interfaces:**
- Produces: `GameWorld.inboundTickCount` (`public static int`), CLI flags `--test-game` and `--test-game-seconds <n>`, `tests/test_lan_lobby.ps1 -Game`, `tests/check_scenes.ps1`.

- [ ] **Step 1: Add the inbound tick counter to GameWorld.** In `GameWorld.cs`, add next to `tickNum`:

```csharp
    /// <summary>Count of GAME_State tick messages received from remote peers (used by the headless smoke test).</summary>
    public static int inboundTickCount = 0;
```
and in `OnMessageReceived`, directly after the `if (ch!=Channel.GAME_State) { return; }` block:
```csharp
        inboundTickCount++;
```

- [ ] **Step 2: Add `--test-game` mode to LobbyDebug.** Add fields beside the other `--test mode fields`:

```csharp
    private bool _testGame;               // --test-game: after the lobby test passes, host starts the game
    private double _testGameSeconds = 8.0; // how long to run in-game before evaluating
    private bool _gameStartSent;
    private bool _gameLoaded;
    private double _gameLoadedAt;
```
In `HandleCmdline`'s switch add:
```csharp
                case "--test-game": _testMode = true; _testGame = true; auto = true; break;
                case "--test-game-seconds":
                    string gs = Next(args, ref i);
                    if (gs != null) double.TryParse(gs, out _testGameSeconds);
                    break;
```
Change `DoneLoading()` to:
```csharp
    private void DoneLoading()
    {
        Hide();
        if (_testGame && !_gameLoaded)
        {
            _gameLoaded = true;
            _gameLoadedAt = _testElapsed;
            Log("TEST game loaded — running in-game checks");
        }
    }
```
Replace the whole `_Process` method with:
```csharp
    public override void _Process(double delta)
    {
        if (!_testMode || _testDone)
            return;
        _testElapsed += delta;

        if (Lobby.MemberCount > _peakMembers)
            _peakMembers = Lobby.MemberCount;

        bool peersOk = _peakMembers >= _expectPeers;
        bool chatOk = _chatReceivedFrom.Count >= _expectPeers - 1;
        bool sentChat = _autoTick >= 1;

        if (peersOk && chatOk && sentChat)
        {
            if (!_testPassed)
            {
                _testPassed = true;
                _testPassedAt = _testElapsed;
                Log($"TEST conditions met — holding {TestGracePeriod}s for peers to converge");
            }
            if (_testElapsed - _testPassedAt >= TestGracePeriod)
            {
                if (!_testGame)
                {
                    _testDone = true;
                    Log($"TEST PASS — peak {_peakMembers} peers, chat from {_chatReceivedFrom.Count} remote peer(s), sent {_autoTick} msg(s)");
                    GD.Print($"TEST_RESULT:PASS peers={_peakMembers} chat_from={_chatReceivedFrom.Count} sent={_autoTick}");
                    GetTree().Quit(0);
                    return;
                }
                if (Lobby.isHost && !_gameStartSent)
                {
                    _gameStartSent = true;
                    Log("TEST host starting game");
                    Lobby.SendStartGame();
                }
            }
        }

        // --test-game: evaluate once the world has been running for _testGameSeconds.
        if (_testGame && _gameLoaded && _testElapsed - _gameLoadedAt >= _testGameSeconds)
        {
            _testDone = true;
            int players = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().Count();
            bool ok = players == _expectPeers && GameWorld.inboundTickCount > 0;
            string summary = $"players={players}/{_expectPeers} synced={GameWorld.syncedObjs.Count} inbound_ticks={GameWorld.inboundTickCount}";
            Log($"TEST GAME {(ok ? "PASS" : "FAIL")} — {summary}");
            GD.Print($"TEST_RESULT:{(ok ? "PASS" : "FAIL")} {summary}");
            GetTree().Quit(ok ? 0 : 1);
            return;
        }

        if (_testElapsed >= _testTimeout)
        {
            _testDone = true;
            Log($"TEST FAIL — timeout after {_testTimeout}s (peak_peers={_peakMembers}/{_expectPeers}, chat_from={_chatReceivedFrom.Count}/{_expectPeers - 1}, sent={_autoTick}, game_loaded={_gameLoaded})");
            GD.Print($"TEST_RESULT:FAIL peak_peers={_peakMembers}/{_expectPeers} chat_from={_chatReceivedFrom.Count}/{_expectPeers - 1} sent={_autoTick} game_loaded={_gameLoaded} elapsed={_testElapsed:F1}s");
            GetTree().Quit(1);
        }
    }
```
Add `using System.Linq;` if missing. Update the `// Test:` comment above `HandleCmdline` to document `--test-game [--test-game-seconds 8]`.

- [ ] **Step 3: Add `-Game` to the PowerShell harness.** In `tests/test_lan_lobby.ps1`:
  - Add param `[switch]$Game`. When `$Game`, if the caller did not pass them, set `$TestTimeout = 60` and `$ScriptTimeout = 80` (use `$PSBoundParameters.ContainsKey`).
  - Build a `$testFlag` array: `@("--test-game")` when `$Game`, else `@("--test")`; use it in place of `"--test"` in both host and joiner arg lists.
  - Print `Mode: game` / `Mode: lobby` in the header.
  - In result collection, also read the `_err.log` for each instance and treat the instance as FAIL if the stdout or stderr log matches `Unhandled exception|System\.[A-Za-z.]*Exception` ; print the first 5 matching lines under the instance's result.

- [ ] **Step 4: Create the scene-load checker.** `tests/check_scenes.gd`:

```gdscript
# Headless check that every .tscn/.tres under res://game and res://gmp loads.
# Run: Godot_console.exe --headless --path <project> --script res://tests/check_scenes.gd
extends SceneTree

var failures := 0
var checked := 0

func _init() -> void:
	for root in ["res://game", "res://gmp"]:
		_walk(root)
	print("CHECK_SCENES checked=%d failures=%d" % [checked, failures])
	print("CHECK_SCENES_RESULT:%s" % ("PASS" if failures == 0 else "FAIL"))
	quit(0 if failures == 0 else 1)

func _walk(path: String) -> void:
	var dir := DirAccess.open(path)
	if dir == null:
		return
	for sub in dir.get_directories():
		_walk(path.path_join(sub))
	for f in dir.get_files():
		if f.ends_with(".tscn") or f.ends_with(".tres"):
			var p := path.path_join(f)
			checked += 1
			var res = ResourceLoader.load(p)
			if res == null:
				failures += 1
				print("CHECK_SCENES_FAIL ", p)
			elif res is PackedScene and not res.can_instantiate():
				failures += 1
				print("CHECK_SCENES_FAIL (cannot instantiate) ", p)
```
`tests/check_scenes.ps1`:
```powershell
# Loads every scene/resource headlessly; fails on load errors or missing dependencies.
param([string]$GodotPath = "C:\Users\steph\OneDrive\Documents\godot\Godot_v4.7.2-stable_mono_win64_console.exe")
$projectDir = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $PSScriptRoot "logs"
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Force $logDir | Out-Null }
$log = Join-Path $logDir "check_scenes.log"
& $GodotPath --headless --path $projectDir --script res://tests/check_scenes.gd *> $log
$content = Get-Content $log
$bad = $content | Where-Object { $_ -match "CHECK_SCENES_FAIL|Cannot open file|Failed loading resource|Unable to load|missing|Parse Error" }
$content | Where-Object { $_ -match "^CHECK_SCENES " } | Write-Host
if ($bad) { $bad | Select-Object -First 30 | Write-Host -ForegroundColor Red }
if (($content -match "CHECK_SCENES_RESULT:PASS") -and -not $bad) { Write-Host "ALL SCENES LOADED" -ForegroundColor Green; exit 0 }
Write-Host "SCENE CHECK FAILED - see tests/logs/check_scenes.log" -ForegroundColor Red; exit 1
```

- [ ] **Step 5: Run the full verification suite on the unmodified-behavior code.** Expected: build OK; `check_scenes.ps1` — record the result. If it reports failures that pre-date this work (e.g. `SyncedBox3DLevel.tscn` references the missing `GMPOBox3DWorld.cs`), list them in the report; they are fixed in Task 1, so temporarily note them as the known baseline. Lobby test PASS 3/3. `-Game` test: must PASS 3/3 (players=3/3, inbound_ticks>0). If the game test fails for environmental reasons (e.g. headless rendering of a level), investigate the logs in `tests/logs/` and report — do not change game code to make it pass.

- [ ] **Step 6: Commit**

```bash
git add gmp/core/GameWorld.cs gmp/ui/LobbyDebug.cs tests/test_lan_lobby.ps1 tests/check_scenes.gd tests/check_scenes.ps1
git commit -m "test: headless in-game smoke test and scene-load checker"
```

---

### Task 1: Delete dead code and stale files

**Files:** deletions and small edits listed below. Before deleting any file, `grep -rn "<FileName-or-ClassName>" --include=*.cs --include=*.tscn --include=*.tres --include=*.godot .` (excluding `addons/`, `.godot/`) and confirm the only references are other files also being deleted. If something unexpected references it, stop and report.

- [ ] **Step 1: Delete unused GMPO bases and the examples that depend on them.**
  - `gmp/core/gmpo/GMPOStaticBody3D.cs`, `GMPORigidBody3D.cs`, `GMPOCharacterBody3D.cs`, `GMPOFPSPlayer.cs` (+ `.uid`s).
  - `gmp/examples/GmpoSphere.cs`, `gmp/examples/GMPOSphere.tscn`, `gmp/examples/SyncedLevel.tscn`, `SyncedLevelTiny.tscn`, `SyncedLevelColors.tscn` — only if they reference only deleted types (check).
  - `gmp/examples/SyncedBox3DLevel.tscn` — references non-existent `GMPOBox3DWorld.cs`; delete.
  - Keep `GmpoB3DSphere.cs` + `GMPBox3DSphere.tscn` and the `ExampleGMPOSingleton*` trio (they use surviving bases); they move to `game/dev/examples/` in Task 2.
- [ ] **Step 2: Delete stale files.** `gmp/ui/lobby_lan_debug.tscn`, `gmp/ui/lobby_steam_debug.tscn`, `game/items/tools/Tool.cs`, `game/items/FactoryMachine.cs`, `game/items/factoryMachines/new_resource.tres`, `GMPFramework.csproj.old`, and the whole `game/items/inventoryItems/` folder (only referenced by the unused `GameResources.Items`).
- [ ] **Step 3: Remove dead members.**
  - `GameResources.Items` dictionary (unused).
  - `GameInfo`: `bulk1`, `bulk2`, `bulk1size`, `bulk2size`, `bulk1flag`, `bulk2flag` (confirm unused).
  - `GameWorld`: `gridMesh`, `GridSize`, `CellSize`, `GridColor` fields, and the two `GameWorld.gridMesh.Show()/Hide()` calls in `game/items/machines/InHandBuilding.cs` (the mesh is never in the tree, so these are no-ops); `waitTicks`/`waitTickCount` and the branch using them; `mostRecentInboundTick`; the unreachable `Logging.Warn` after `break`.
  - `RPCManager.OnMessageReceived`: collapse to `if (ch != Channel.RPC_Main) return;` + handling.
  - `GMPOBox3DBody._Ready` override that only calls `base._Ready()` — delete the override.
  - Unused `using ENet;` in `Lobby.cs` and any other now-unused `using`s in touched files.
- [ ] **Step 4: Remove commented-out code** in `gmp/` and `game/` `.cs` files: blocks of commented *code* (not explanatory comments). Keep `// TODO`-style notes and prose. Exception: the commented-out all-members gate in `Lobby.OnDoneLoading` — keep it and add above it `// PHASE 2: this gate is disabled; LobbyDoneLoadingEvent currently fires on the first DoneLoading message.`
- [ ] **Step 5: Fix typos.** Class `ExmapleGMPOSingletonReferencer` → `ExampleGMPOSingletonReferencer`, `ExmapleGMPOSingletonSpawner` → `ExampleGMPOSingletonSpawner` (update any `.tscn` referencing them — scripts are referenced by path/uid, so just the class names).
- [ ] **Step 6: Remove the `LiteNetLib` PackageReference** from `GMPFramework.csproj`. Leave `ENet-CSharp` only if something uses the `ENet` namespace (grep `using ENet` / `ENet\.`); if nothing does, remove it too.
- [ ] **Step 7: Run the verification suite.** `check_scenes` must now PASS with zero failures. All tests pass.
- [ ] **Step 8: Commit** — `git add -A && git commit -m "chore: remove dead code, stale scenes, and unused packages"`

---

### Task 2: Move files into the target layout

**Files:** all moves below. Use this bash helper (run from project root) which moves a file plus its sidecars and rewrites `res://` references in every text asset and C# file:

```bash
mvres() {  # mvres <old-relative-path> <new-relative-path>
  local old="$1" new="$2"
  mkdir -p "$(dirname "$new")"
  git mv "$old" "$new"
  for ext in uid import; do [ -f "$old.$ext" ] && git mv "$old.$ext" "$new.$ext"; done
  grep -rl --include=*.tscn --include=*.tres --include=*.cs --include=*.godot --include=*.gdshader \
       --include=*.gd --include=*.cfg --exclude-dir=.godot --exclude-dir=addons --exclude-dir=bin \
       -F "res://$old" . | xargs -r sed -i "s#res://$old#res://$new#g"
}
```

- [ ] **Step 1: gmp/ moves**

| Old | New |
|---|---|
| `gmp/core/utils/ColorDict.cs` | `gmp/core/ColorDict.cs` |
| `gmp/core/GameWorld.cs` | `gmp/sync/GameWorld.cs` |
| `gmp/core/gmpo/GMPObject.cs` | `gmp/sync/GMPObject.cs` |
| `gmp/core/gmpo/GMPONode3D.cs` | `gmp/objects/GMPONode3D.cs` |
| `gmp/core/gmpo/GMPOBox3DBody.cs` | `gmp/objects/GMPOBox3DBody.cs` |
| `gmp/core/gmpo/GMPOBox3DCharacter.cs` | `gmp/objects/GMPOBox3DCharacter.cs` |
| `gmp/core/gmpo/GMPOSingleton.cs` | `gmp/objects/GMPOSingleton.cs` |
| `gmp/core/net/*.cs` (each file) | `gmp/net/<same name>` |
| `gmp/core/Grid.cs` | `game/core/Grid.cs` |
| `gmp/examples/*` (each surviving file) | `game/dev/examples/<same name>` |

- [ ] **Step 2: game/ moves**

| Old | New |
|---|---|
| `game/GameResources.cs` | `game/core/GameResources.cs` |
| `game/FluidSim.cs` | `game/dev/FluidSim.cs` |
| `test.glsl` | `game/dev/fluid_sim.glsl` |
| `game/items/PhysicalFactoryItem.cs` | `game/items/world/PhysicalFactoryItem.cs` |
| `game/items/factoryItems/DefaultDroppedBox.cs` | `game/items/world/DefaultDroppedBox.cs` |
| `game/items/factoryItems/defaultDroppedBox.tscn` | `game/items/world/DefaultDroppedBox.tscn` |
| `game/items/factoryItems/DefaultHeldBox.cs` | `game/items/held/DefaultHeldBox.cs` |
| `game/items/factoryItems/defaultHeldBox.tscn` | `game/items/held/DefaultHeldBox.tscn` |
| `game/items/factoryItems/FactoryItem.cs` | `game/items/FactoryItem.cs` |
| `game/items/factoryItems/*.tres` (each) | `game/items/definitions/<same name>` |
| `game/items/factoryMachines/ConveyorBelt.tscn` | `game/machines/ConveyorBelt.tscn` |
| `game/items/factoryMachines/ConveyorBelt(UpSlope).tscn` | `game/machines/ConveyorBeltUpSlope.tscn` |
| `game/items/factoryMachines/test_placeable_1x1x1_*.tscn` (each) | `game/dev/items/<same name>` |
| `game/items/machines/InHandBuilding.cs` | `game/machines/InHandBuilding.cs` |
| `game/items/examples/**` (each file, flatten `hand/`) | `game/dev/items/<file name>` |
| `game/scripts/BasicButton.cs` | `game/props/BasicButton.cs` |
| `game/scripts/ObjectSpawner.cs` | `game/props/ObjectSpawner.cs` |
| `game/shaders/ConveyorBelt.gdshader` | `game/assets/shaders/ConveyorBelt.gdshader` |

The parenthesized filename must be quoted in the shell. `sed` on `(`/`)`: the `#`-delimited pattern treats them literally in basic regex — verify with grep afterwards.

- [ ] **Step 3: Fix path strings the helper can't catch.** `FactoryItem.Fetch` builds `"res://game/items/factoryItems/" + itemID + ".tres"` → change to `"res://game/items/definitions/"`. Grep all `.cs` for `res://` and confirm every literal points at an existing file (`test -f`). Also `grep -rn "factoryItems\|factoryMachines\|items/examples\|gmp/core/gmpo\|gmp/core/net\|gmp/examples\|game/scripts\|game/shaders\|res://test.glsl"` must return nothing outside `docs/` and `.superpowers/`.
- [ ] **Step 4: Remove now-empty directories** (`gmp/core/gmpo`, `gmp/core/net`, `gmp/core/utils`, `gmp/examples`, `game/items/factoryItems`, `game/items/factoryMachines`, `game/items/machines`, `game/items/tools`, `game/items/examples`, `game/scripts`, `game/shaders`).
- [ ] **Step 5: Delete the `.godot/` cache's stale script/uid cache** so Godot rebuilds it: `rm -rf .godot/editor .godot/uid_cache.bin .godot/global_script_class_cache.cfg`, then run a headless import: `"<godot console>" --headless --path . --import` (expect exit 0). Then run the verification suite.
- [ ] **Step 6: Commit** — `git add -A && git commit -m "refactor: reorganize folders into gmp/{core,net,sync,objects,ui} and game/{core,player,items,machines,props,dev}"`

---

### Task 3: (removed)

Namespaces were dropped at the user's request (2026-09-24). All types stay in the global namespace.

---

### Task 4: Consolidate GMPO sync logic

**Files:**
- Create: `gmp/sync/SyncHelpers.cs`
- Modify: `gmp/sync/GMPObject.cs`, `gmp/objects/GMPONode3D.cs`, `gmp/objects/GMPOBox3DBody.cs`, `gmp/objects/GMPOBox3DCharacter.cs`, `gmp/objects/GMPOSingleton.cs`, plus any user of `BasicSyncMessage`.

**Interfaces:**
- Produces:
  - `TransformSyncState` — `[GenerateShape] public partial record struct` with `public Vector3 pos; public Vector3 rot;` (replaces `BasicSyncMessage`; same field names so the wire format is unchanged).
  - `static class SyncHelpers`:
    - `byte[] WriteTransform(Node3D node)`
    - `bool TryReadTransform(byte[] state, out TransformSyncState s)` — false for null/empty.
    - `void LerpTransform(Node3D node, TransformSyncState target, float weight)` — `weight >= 1` snaps.
  - `GMPObject.ApplyProcessMode()` — replaces the interface's `_Ready()` default method.

- [ ] **Step 1: Create `gmp/sync/SyncHelpers.cs`:**

```csharp
using Godot;
using PolyType;

/// <summary>Position + Euler rotation snapshot sent by the authority for transform-synced objects.</summary>
[GenerateShape]
public partial record struct TransformSyncState
{
    public Vector3 pos;
    public Vector3 rot;
}

/// <summary>
/// Shared StateUpdate helpers for GMPO node types. Godot nodes can't share a base class across
/// Node3D/Box3D wrappers, so common sync logic lives here instead.
/// </summary>
public static class SyncHelpers
{
    /// <summary>Serializes the node's local position and rotation.</summary>
    public static byte[] WriteTransform(Node3D node)
    {
        return GMPObject.serializer.Serialize(new TransformSyncState { pos = node.Position, rot = node.Rotation });
    }

    /// <summary>Deserializes a transform state; returns false if there is no state yet.</summary>
    public static bool TryReadTransform(byte[] state, out TransformSyncState s)
    {
        if (state == null || state.Length == 0)
        {
            s = default;
            return false;
        }
        s = GMPObject.serializer.Deserialize<TransformSyncState>(state);
        return true;
    }

    /// <summary>Moves the node toward <paramref name="target"/>. A weight of 1 or more snaps.</summary>
    public static void LerpTransform(Node3D node, TransformSyncState target, float weight)
    {
        if (weight >= 1f)
        {
            node.Position = target.pos;
            node.Rotation = target.rot;
            return;
        }
        node.Position = node.Position.Lerp(target.pos, weight);
        node.Rotation = node.Rotation.Lerp(target.rot, weight);
    }
}
```
- [ ] **Step 2: Rework `GMPObject.cs`.** Remove all `[ExportGroup]`/`[Export]` attributes from the interface properties (Godot ignores them). Rename the default method `_Ready()` → `ApplyProcessMode()` with an XML doc comment: "Sets ProcessMode from `pauseable`. Named so it can never collide with Godot's `_Ready`." Add XML doc comments on the interface and every member (id, authority, owner, priority, priorityAccumulator, pauseable, desiredState, GenerateStateUpdate, ApplyStateUpdate, Init, AfterInit) describing the contract: authority's `GenerateStateUpdate` is sent to all other peers; non-authority peers get `ApplyStateUpdate`; `priority` accumulates each tick and higher accumulators send first within the byte budget; `priority <= -1` never sends; `Init` runs on every peer after spawn, applies `initState`, then calls `AfterInit`.
- [ ] **Step 3: Rewrite the four bases to use the helpers, preserving behavior exactly:**
  - `GMPONode3D`: `GenerateStateUpdate` → `SyncHelpers.WriteTransform(this)`; `_PhysicsProcess` non-authority → `if (SyncHelpers.TryReadTransform(desiredState, out var s)) SyncHelpers.LerpTransform(this, s, 1f);` (snap — current behavior).
  - `GMPOBox3DBody`: same, with weight `(float)(10 * delta)`; `AfterInit` authority branch → `if (SyncHelpers.TryReadTransform(desiredState, out var s)) Teleport(s.pos, s.rot);`. Add the same `[ExportGroup]/[Export]` block the other bases have (it was missing; defaults are unchanged so scenes are unaffected). Leave the `Enable()` method exactly as-is (it sets `enabled` to false — a Phase 2 finding; add `// PHASE 2: Enable() sets enabled=false` above it).
  - `GMPOBox3DCharacter`: same as Box3DBody for `GenerateStateUpdate`/`_PhysicsProcess`/`AfterInit` (it teleports via `Call("teleport", ...)` — keep a `Teleport(Vector3 pos, Vector3 rot)` private helper or call inline). `_Ready()` body becomes `((GMPObject)this).ApplyProcessMode();` — this is the only class whose `_Ready` applies process mode today.
  - `GMPOSingleton`: remove nothing behavioral; ensure its export block matches the others.
  - Every base keeps an identical, documented export block. Put a one-line comment above it: `// Godot only reads [Export] on the node class, so each GMPO base repeats this block.`
  - Order members identically in all four files: export block, Init hooks (`AfterInit`), sync (`GenerateStateUpdate`, `ApplyStateUpdate`, `_PhysicsProcess`), then type-specific helpers.
- [ ] **Step 4: Replace remaining `BasicSyncMessage` usages** anywhere (grep) with `TransformSyncState`/helpers, then delete the `BasicSyncMessage` type.
- [ ] **Step 5: Run the verification suite** (the `-Game` test exercises FactoryPlayer = GMPOBox3DCharacter and state ticks). Commit — `git commit -am "refactor: share GMPO transform sync via SyncHelpers; rename interface _Ready to ApplyProcessMode"`

---

### Task 5: Split GameWorld; move game bootstrap and grid into game/

**Files:**
- Split: `gmp/sync/GameWorld.cs` → `GameWorld.cs`, `GameWorld.Spawning.cs`, `GameWorld.StateSync.cs`, `GameWorld.Debug.cs` (all `public partial class GameWorld`)
- Create: `game/core/GameBootstrap.cs`
- Rename: `game/core/Grid.cs` → `game/core/BuildGrid.cs` (class `Grid` → `BuildGrid`)

**Interfaces:**
- Produces: `static class GameBootstrap` with `static void Preload(GameInfo gameInfo)` (host: spawn level) and `static void Init()` (spawn local FactoryPlayer and seed inventory). `GameWorld.Preload/Init` keep their framework parts and call these.

- [ ] **Step 1: Split GameWorld by responsibility.** Pure cut/paste, no logic edits:
  - `GameWorld.cs`: class doc comment, static fields that are about lifecycle (`instance`, `b3droot`, `started`, `tickNum`), `_Ready`, Lobby event handlers, `Raycast`, `OverlapSphere`, `Preload`, `Init`, `Claim`.
  - `GameWorld.Spawning.cs`: `SpawnScene` overloads, `_SpawnScene`, `_SpawnPackedScene`, `_SpawnInternal`, `DespawnObject`, `_DespawnObject`, `initDataNormalizer`, `childGMPORegisterGenerator`, `GenRandomID`, `syncedObjs`.
  - `GameWorld.StateSync.cs`: `WorldTickMessage` record, `Witness` shape classes, `pendingOutgoingTick`, `pendingIncomingTicks`, `mostRecentUpdates`, `pack`, `maxTickSize`, `inboundTickCount`, `OnMessageReceived`, `_PhysicsProcess`.
  - `GameWorld.Debug.cs`: `displaySyncedObjectDebugInfo`, `_Process` ImGui overlay.
  - Add a short XML summary to each file describing what that part owns, and XML docs on `SpawnScene`, `DespawnObject`, `Raycast`, `OverlapSphere` (note: `SpawnScene` is an RPC to all peers including self; the self copy is delivered synchronously, so `syncedObjs[id]` is valid immediately after it returns — document this as the current behavior).
  - Check whether the Godot C# source generator is happy with a partial node class split across files (it is supported); verify the `GameWorld` autoload still resolves (its uid points to `GameWorld.cs`, which must keep the class's primary declaration).
- [ ] **Step 2: Extract GameBootstrap.** Create `game/core/GameBootstrap.cs`:

```csharp
using Godot;
using System;

/// <summary>
/// FactoryGame-specific session startup, called by GameWorld during the lobby → game transition.
/// Preload runs on every peer when the host starts the game; Init runs on every peer once all
/// peers have finished preloading.
/// </summary>
public static class GameBootstrap
{
    public const string PlayerScene = "res://game/player/FactoryPlayer.tscn";

    /// <summary>Host spawns the selected level for everyone.</summary>
    public static void Preload(GameInfo gameInfo)
    {
        if (Lobby.isHost)
        {
            string levelPath = GameResources.LevelsList[gameInfo.levelIdx].levelPath;
            GameWorld.SpawnScene(levelPath);
        }
    }

    /// <summary>Each peer spawns its own player and seeds its starting inventory.</summary>
    public static void Init()
    {
        PlayerSync init = new PlayerSync();
        init.controllingPeerID = Lobby.selfPeerID;
        init.isHuman = true;
        Vector3 spawnPos = new Vector3(Random.Shared.Next(5), Random.Shared.Next(2, 5), Random.Shared.Next(5));
        ulong pid = GameWorld.SpawnScene(PlayerScene, spawnPos, default, default, GMPObject.serializer.Serialize(init));
        FactoryPlayer player = GameWorld.syncedObjs[pid] as FactoryPlayer;
        player.inventory.AddItem("test_1x1x1cubePLACEABLE", 1);
        player.UpdateEquippedItem();
    }
}
```
Then `GameWorld.Preload` becomes: keep the `if (Lobby.isHost) maxTickSize *= 4;` line in GameWorld (framework budget), then `GameBootstrap.Preload(gameInfo);`. `GameWorld.Init` becomes `GameBootstrap.Init();` followed by the existing `Lobby.SendToAllAndSelf(Channel.LOBBY_Control, [(byte)LobbyControlCode.DoneLoading]);`. Preserve call ordering exactly (maxTickSize multiply happened before SpawnScene in the original — keep it first). Copy the exact expressions from the current code if they differ from the snippet above.
- [ ] **Step 3: Rename Grid → BuildGrid.** `git mv game/core/Grid.cs game/core/BuildGrid.cs` (+ `.uid`), rename the class, update `Grid.` references (FactoryPlayer, Console, any others via grep `\bGrid\.`) and any `.tscn` `res://` path. Add a class XML summary: "Build-placement grid: debug line mesh, looked-at cell highlight, and a local cell-occupancy map. Not networked (Phase 2+)."
- [ ] **Step 4: Run the verification suite.** Commit — `git add -A && git commit -m "refactor: split GameWorld into partials; move game bootstrap and BuildGrid into game/core"`

---

### Task 6: String hygiene — nameof() for RPCs, cached node paths

**Files:** any `.cs` calling `RPCManager.RPC(...)`, `GMPOSingleton.RPC(...)`, or with repeated `GetNode<T>("...")` string lookups in per-frame code.

- [ ] **Step 1:** Replace every string-literal RPC method name with `nameof(...)`, e.g. `RPCManager.RPC(instance, "_SpawnScene", ...)` → `RPCManager.RPC(instance, nameof(_SpawnScene), ...)`. Grep: `grep -rn 'RPC(.*"_\?[A-Za-z]*"' --include=*.cs gmp game`. Every hit must be converted.
- [ ] **Step 2:** In `game/player/FactoryPlayer.cs` and `game/player/InventoryUI.cs`, any `GetNode`/`hud.GetNode<...>("%...")` executed inside `_Process`/`_PhysicsProcess`/per-frame helpers becomes a private field assigned once in `AfterInit()` (FactoryPlayer — do NOT add a `_Ready` that chains to an interface method) or `_Ready()` (InventoryUI, a plain Control). Paths stay identical.
- [ ] **Step 3:** Run the verification suite. Commit — `git commit -am "refactor: nameof() for RPC method names; cache per-frame node lookups"`

---

### Task 7: FactoryPlayer component split — DESIGN CHECKPOINT

Not implemented by this plan. After Task 6, the coordinator presents a component-split design for `FactoryPlayer.cs` to the user; once approved it gets its own short plan appended here.

---

## Phase 2 findings log
Implementers append bugs discovered (but not fixed) here, with file:line.

- `GMPOBox3DBody.Enable()` sets `enabled` to `false` (same as `Disable()`).
