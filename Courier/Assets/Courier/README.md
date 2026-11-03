# Courier: prototype scaffold

A town builder where you program courier bots with drag-and-drop commands. Each bot runs a daily
schedule: it picks up outputs, feeds building inputs, and delivers goods to the Town Hall, which
spends them on new buildings, more bots and more land.

## Run it

1. Let Unity finish compiling.
2. Choose **Courier > Create Town Scene** from the menu bar. This saves `Assets/Courier/Scenes/Town.unity`.
3. Press **Play**, then **Start Day** (or press Space).

Bot 1 starts with a working route (woodcutter → sawmill → town hall). Watch it run for a day, then
improve it, buy a second courier, and build a bakery.

| Input | Action |
|---|---|
| Left click | Select a courier or building, or place a building in build mode |
| Right-drag / middle-drag / WASD | Pan |
| Scroll | Zoom |
| Space | Start the day, or pause/resume |
| F | Cycle speed (1x, 2x, 4x, 8x) |
| Tab | Select the next courier |
| Esc / right click | Cancel build mode or deselect |

**Schedule editor** (right panel)

- Drag a block from the palette into the list, or click it to append it.
- Drag rows to reorder them.
- Drag a row out of the list (or right-click it) to delete it.
- Click a Pick Up or Drop Off row to change which side it uses: `auto`, `ahead`, `left` or `right`.
  `auto` tries ahead, then left, then right, then behind, and uses the first building that can do the job.
- You can edit while planning or while the day is paused.
- The route preview on the map shows where the bot will drive:
  - green and orange markers show pick-ups and drop-offs
  - red markers show moves that hit a dead end
  - a yellow marker means the loop doesn't return to its start

## Rules (current defaults, all tunable)

- **Ticks:** each command takes exactly one tick. A day is 96 ticks (`Balance.TicksPerDay`); at 0.25 s per tick that's 24 s at 1x speed.
- **Schedules:** they loop until the day ends unless **Loop** is turned off for that bot.
- **Start and end of day:** at the start of each day, bots begin at the depot facing north. Overnight they return to the depot and keep their cargo.
- **Production:** a building uses up its inputs when a cycle starts and adds its outputs when the cycle ends. Production stops when the output buffer is full.
- **Collisions:** bots can share a tile; they don't block each other yet.
- **Town Hall:** accepts any good. Its input buffer *is* the town stockpile.
- **Layout:** the map is 8×8 city blocks of 2×2 lots, so every lot touches a road. Play starts with the middle 2×2 blocks unlocked, and each **Expand** unlocks a ring of blocks.

## Code layout

```
Scripts/Sim/    Courier.Sim: pure C#, no UnityEngine (asmdef enforces it). Deterministic, unit-tested.
  Town.cs            day loop, command execution, build/buy/expand
  TownGrid.cs        city-block grid, unlocked area
  Building.cs        BuildingType definitions + Building instances (buffers, production)
  CourierBot.cs      bot state
  Command.cs         CommandType, InteractSide, CommandResult, Schedule (editable list + Changed event)
  ScheduleTracer.cs  dry-runs movement for the route preview and loop check
  Content.cs         Catalogue (buildings), Balance (costs/tuning), StarterTown (opening layout + tutorial route)
Scripts/Game/   Courier.Game: Unity side. Reads the sim, never owns rules.
  GameBootstrap.cs   builds everything at runtime (no prefabs yet)
  GameController.cs  real-time ticking, speed, selection, build mode, toasts
  CameraRig.cs, WorldInput.cs   Input System only (project has legacy input disabled)
  Visuals/           placeholder primitives: TownView, BuildingView, BotView, RoutePreview
  UI/                uGUI built in code: GameUI (HUD), ScheduleEditor + CommandBlockView (drag & drop)
Editor/         Courier menu
Tests/EditMode/ NUnit tests for the sim (Window > General > Test Runner)
```

## Extending

- **New good:** add it to `Resource`, then give it a colour in `Palette.For(Resource)`.
- **New building:** add a `BuildingType` in `Catalogue` and list it in `Catalogue.All`, then give it a colour in `Palette.For(BuildingType)`. It appears in the Build menu automatically.
- **New command:**
  1. Add it to `CommandType` and `CommandInfo` (name and palette order).
  2. Handle it in `Town.Execute`. If it moves the bot, handle it in `ScheduleTracer` as well.
  3. Give it a colour in `Palette.For(CommandType)`.

  If the command needs a parameter like `Side`, extend `Command`.
- **Real art:** swap `Shapes.Box` calls in `Visuals/` for prefabs. Nothing outside `Visuals/` depends on the meshes.

## Known gaps and ideas

- No save/load yet. `Town` is plain data, so JSON serialisation is easy.
- Content is defined in C#. Move it to ScriptableObjects once it settles.
- For builds, add `Universal Render Pipeline/Unlit` to *Always Included Shaders*. Without it the route line falls back to the lit material.
- Design hooks worth trying:
  - "wait until full/empty" and "repeat N" blocks
  - conditional blocks (if carrying X)
  - bot traffic and collisions
  - per-bot depots
  - houses that consume bread
  - a schedule that runs only once per day, so length becomes the constraint
