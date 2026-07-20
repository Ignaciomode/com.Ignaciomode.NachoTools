---
name: unity-game-architecture
description: >-
  Reusable architecture patterns for building a Unity game on the NachoTools
  toolkit — ScriptableObject-driven data, systems decoupled through
  ScriptableVariable reference assets instead of singletons, a single-file JSON
  save system with an Export/Import + OnLoad convention, an EventFSM state
  machine for the player and other entities, object pooling for frequently
  spawned objects, and event-driven interactables/inventory/puzzles. Use this
  when starting or structuring a new Unity game, or when adding any of those
  systems to one: a save/load system, a player or enemy state machine,
  ScriptableObject content plus a registry, an interaction system, an inventory,
  or a puzzle/trigger. Trigger even when the user doesn't name the pattern —
  e.g. "set up saving for my game", "add a crouch state to the player", "make
  this chest openable", "give the player an inventory", "how should I structure
  this new game's systems". Do NOT use it for Unity engine internals, shader or
  rendering work, physics tuning, or debugging third-party packages — it is a
  guide to a specific set of game-architecture conventions, not general Unity
  support.
---

# Unity game architecture (NachoTools patterns)

A reusable way to structure a Unity game: **data lives in ScriptableObjects**,
**systems find each other through ScriptableVariable assets** (not singletons),
there is **one save file**, and **entities are driven by state machines**. The
runtime primitives (`EventFSM`, `ScriptableVariable<T>`, `ObjectPool<T>`) come
from the **NachoTools** package, imported into each game as a `file:` package in
`Packages/manifest.json`.

Follow the conventions below and new systems drop in cleanly and stay decoupled;
ignore them and you end up fighting load order, singleton lifetimes, and the
serializer. Each rule states the *why*, not just the rule.

## Core conventions

### 1. Author data as ScriptableObjects
Content (items, entity configs, tuning tables, shared references) is authored as
SO assets via `[CreateAssetMenu(menuName = "...", fileName = "...")]`, not
hard-coded. Code defines the *shape and behavior*; designers create *instances*
in the editor. Any new data type you add should carry a `CreateAssetMenu` so it
can be authored and reused across scenes and prefabs.

### 2. Reference shared systems through `ScriptableVariable<T>`, not singletons
Instead of `FindObjectOfType` or a static `Instance`, one SO asset of type
`ScriptableVariable<T>` holds a live reference. The scene object that owns the
reference writes itself in on `Awake` (`myVariable.value = this;`); every
consumer has that same asset dragged into a serialized field and reads
`myVariable.value`. No scene lookups, no load-order coupling, no static state
leaking between play sessions — everyone just points at the same asset.

**Add one:** a one-line subclass
`public class FooVariable : ScriptableVariable<Foo> {}` with a
`[CreateAssetMenu(menuName = "Scriptable Objects/ScriptableVariable/Foo")]`,
create the asset, have the owner write `.value` on `Awake`, drag the asset into
each consumer. Prefer reusing an existing variable (e.g. a `Player` one) to reach
things it already owns, rather than making a new variable per system.

### 3. Persist stable string Ids, never ScriptableObject references
ScriptableObjects can't be serialized into JSON. Anything persistent that points
at an SO must store a **stable string Id** and resolve it back at load time.
Give each persistable SO a GUID Id (auto-generate it in `OnValidate` so it's
never hand-edited), keep a **registry/database SO** that maps Id → asset, and
have save data store only Ids. When you add saveable content that references an
SO, store its Id and resolve through the registry — do not store the reference.

### 4. One save file, owned by a `SaveManager`
Serialize a single `[Serializable]` save class to
`Application.persistentDataPath + "/{slot}SaveData.json"` with `JsonUtility`.

- **Save:** the manager reads current world state (positions, settings, inventory
  contents, …) and, for systems whose state is **runtime-only**, calls their
  `ExportX()` method, then writes JSON.
- **Load:** read JSON into the current save object, then raise an `OnLoad` event.
  Each system subscribes in `Start()` and restores itself (its `ImportX()`, or a
  `LoadX()` that reads the freshly loaded save).

This keeps every system's persistence in one place and one file, and lets
runtime-only systems round-trip their state without baking it onto an asset.

**Add a persisted field:**
1. Add a `JsonUtility`-friendly field to the save class (primitives, `Vector3`,
   `[Serializable]` classes, `List<>` — *not* SO references; store an Id/enum per
   rule 3).
2. In the save method, set it before the write — or, if it lives in a runtime-only
   system, give that system `ExportX()`/`ImportX()` and call `ExportX()` here.
3. In `Start`, add an `OnLoad += …` subscription that restores it.
4. A top-level `List`/array can't be serialized by `JsonUtility` on its own — wrap
   it in a `[Serializable]` container class.

### 5. Drive entities with `EventFSM<TState>`
The player (and enemies, doors, cameras, anything stateful) is a finite state
machine. Build it in `Awake`: create a `StateE<TState>` per state, wire allowed
transitions with `StateConfigurer.Create(state).SetTransition(TState.X, other)…
.Done()`, then attach `OnEnter/OnUpdate/OnFixedUpdate/OnExit` handlers. Pump it
from `Update`/`FixedUpdate`. Change state only via the FSM's input
(`SendInput(TState.X)`), never by setting fields directly — only declared
transitions succeed, which keeps illegal states unreachable.

**Add a state:**
1. Create the `StateE` in `Awake` and add the case to your state enum.
2. Add it to the relevant `StateConfigurer` chains — both *into* it and *out of* it.
3. Add its `OnEnter/OnExit/…` handlers (set current state, lock camera/cursor or
   open UI as needed — copy an existing UI-style state as a template).
4. Trigger it from your input/controller via the FSM's `SendInput`.

### 6. Pool frequently spawned objects with `ObjectPool<T>`
For anything spawned repeatedly at runtime (projectiles, enemies, pickups, VFX),
reuse instances through `ObjectPool<T>` instead of Instantiate/Destroy per spawn,
to avoid GC churn. Build the pool lazily with factory + turn-on/turn-off
callbacks; spawned objects take a `returnToPool` callback and return themselves
when done (on hit, on expiry) rather than being destroyed.

### 7. Input via the Input System → controller → FSM
Handle input in a controller MonoBehaviour whose `On<Action>` methods are invoked
by a `PlayerInput` component (Unity Input System). The controller translates input
into FSM inputs (`SendInput`) and system calls — it doesn't implement behavior
itself. Add an action in the Input System asset, then a matching `On<Action>`
method on the controller.

## Misc systems

**Interactables.** Define `interface IInteractable { void Interact(); }`. A
controller casts in front of the camera each frame (e.g. `Physics.BoxCastAll`),
collects `IInteractable`s, and calls `Interact()` on the interact input. Reach
shared systems through the relevant `ScriptableVariable`.
*Make something interactable:* implement `IInteractable` on a MonoBehaviour with a
collider in the cast path, do the work in `Interact()`.

**Inventory & items.** Items are SO assets (an id/enum, quantity, an optional
prefab). The inventory itself can be an SO holding the item collection with
`Add`/`Remove`/`Has` and change events, rebuilt from the save on load.
*Add an item type:* extend the item id/enum and author a new item asset.

**Event-driven puzzles / triggers.** Keep puzzle logic in small MonoBehaviours
that raise C# events or `UnityEvent`s, with visuals/audio/quest hooks wired in the
inspector rather than hard-coded — designers assemble behavior without new code.
Route any persistent puzzle state through the save file (rules 3–4).

## NachoTools

The toolkit that provides `EventFSM`, `StateE`, `StateConfigurer`,
`ObjectPool<T>`, and `ScriptableVariable<T>`. It's a standalone package referenced
from each game's `Packages/manifest.json` (a `file:` local package or a git URL),
so its source lives outside any single game's `Assets/` — treat these as stable
library types shared across your projects.
