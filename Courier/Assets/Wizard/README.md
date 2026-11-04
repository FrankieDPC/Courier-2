# Wizard's Tower: prototype

You're a wizard in a tower. A Doom is coming in 10 days. Until then you send out magic constructs
to do the village's odd jobs: each construct follows a daily routine you assemble from drag-and-drop
commands and reusable spells. The gold you earn pays for wards, fortifications and workshops.
On day 10 the Doom comes up the roads and your preparations either hold or they don't.

## Run it

1. Let Unity finish compiling.
2. Choose **Wizard > Create Tower Scene** from the menu bar. This saves `Assets/Wizard/Scenes/Tower.unity`.
3. Press **Play**, then **Start Day** (or Space).

**Bramble**, your first construct, starts with a working routine:

- It carries grain from the farm to the bakery and bread to Mabel's cottage.
- The routine uses the **Two Steps** spell.
- When Mabel's job is done, Bramble ends up holding bread nobody wants. Rewriting its routine is your job.

| Input | Action |
|---|---|
| Left click | Select a construct or building, or place a building in build mode |
| Right-drag / middle-drag / WASD | Pan |
| Scroll | Zoom |
| Space | Start the day, or pause/resume |
| F | Cycle speed (1x, 2x, 4x, 8x) |
| P | Preview the selected construct's day |
| Tab | Select the next construct |
| Esc / right click | Cancel build mode or deselect |

## The loop

- **Days 1–9 (preparing):**
  - Villagers pin **odd jobs** to the board, at most 3 open at once. A job is either "bring N goods to my cottage" or "take my parcels to the tower".
  - Each job has a deadline and a gold reward. Missed jobs simply expire.
  - Spend gold and stone on:
    - workshops
    - more constructs
    - more land
    - **Wards** (defensive pylons)
    - **Fortify** (extra tower HP)
- **Day 10 (the Doom):**
  - Monsters leave the **Doom Gates** at the map edge, which are marked from day 1. They follow the roads through the wilds to the tower.
  - Wards (range 2) and the tower itself (range 3) shoot them, spending **Charges**. Wards fire from their own stock and the tower fires from the stores.
  - **Potions** in the tower heal it.
  - Your constructs keep running their routines, so a construct that keeps the wards topped up is worth a lot.
  - The tower survives: you win. It falls: you lose. The game-over card has **Play again**.
- **Production chains:**
  - Farm → **Bakery** makes Bread
  - Herb Garden → **Alchemist** makes Potions
  - Crystal Mine → **Arcane Forge** makes 2 Charges per Crystal
  - **Quarry** makes Stone

## Routines and spells

- **Commands:** Forward, Turn Left/Right, U-Turn, Wait, Pick Up, Drop Off, and **Cast**. Each command takes one tick, except Cast, which is free.
- **Spells** are named chunks of commands in a shared **spellbook**.
  - Drag a spell into a routine to cast it.
  - Click a Cast row, or a spell's **edit** button, to open the spell.
  - Editing a spell changes every construct and spell that casts it.
  - Spells can cast other spells, but never themselves, even indirectly; the editor refuses.
  - Deleting a spell removes every cast of it.
  - **Save as spell** (under the routine) copies the routine on screen into a new spell and opens it so you can name it. Use it on a construct's routine to bottle a route you're happy with, or on a spell to duplicate it.
- **Forward distance:** click a Forward row to switch between `1 step`, `till stop` and `till intersection`.
  - `till stop` keeps driving until the road ahead ends or is blocked.
  - `till intersection` keeps driving until the construct reaches a junction (a road branching left or right), or the road ends. It never counts the junction it starts on.
  - Every cell moved still takes one tick, and the routine waits on that line until the construct arrives.
- **Pick Up and Drop Off sides:** click a row to switch between `auto`, `ahead`, `left` and `right`. `auto` tries ahead, then left, then right, then behind, and uses the first building that can do the job.
- **Looping:** routines loop until the day ends unless **Loop** is off.
- **Dusk:** when the working day ends (tick 96), every construct stops its routine and walks home along the shortest road route, one cell per tick.
  - At home it empties its cargo into the tower, which counts toward "take it to the tower" jobs. Set `Balance.BankCargoAtDusk` to false to make leftover cargo vanish instead.
  - It then faces its start direction. The day ends once everyone is home, so each day starts clean.
  - Dusk plays three times faster. There's no dusk on the doom day.
- **Route preview:** the selected construct's route is drawn on the map with every spell expanded:
  - green and orange markers show pick-ups and drop-offs
  - red markers show moves that hit a dead end
  - a yellow marker means the loop doesn't end where it started
- **Running highlight:** while the day runs, the current line is highlighted, inside spells too.
- **Editing:** you can edit while planning or while paused. Edits made while paused take effect immediately, so a construct may skip or repeat a line mid-route.

## Previewing a day

Press **P**, or **Preview day** under the routine. This runs the day on a throwaway copy of the
world, so nothing real changes. It works while planning (the whole next day) and while paused
(the rest of today).

- **Ghost:** a pale, floating copy of the selected construct acts the day out. You can play, pause or drag the timeline.
- **Line highlight:** the routine panel highlights the line being previewed, inside spells too.
- **Summary:** the card at the bottom lists the construct's drop-offs, which jobs get finished, and what it's carrying at the end of the day.
- **Problems:** problems are grouped by line, such as "line 3 Pick Up: Nothing ready to collect x7". Click one to jump to its first occurrence and open the routine or spell it's in.
- **Live editing:** edit routines, spells or buildings with the preview open and it re-runs immediately.
- **Accuracy:** the simulation is deterministic, so the preview is exact unless you change something before the day runs.
- **Doom day:** the preview simulates the siege too, because wards spending Charges affects what constructs can drop off. If the tower falls early, the preview's timeline ends early.

## Code layout

```
Scripts/Sim/      Wizard.Sim: pure C#, no UnityEngine (asmdef enforces it). Deterministic, unit-tested.
  World.cs            day loop, building/buying/expanding/fortifying
  World.Commands.cs   command execution and goods hand-off
  World.Jobs.cs       job board: posting, crediting, rewards, expiry (seeded RNG)
  World.Siege.cs      the Doom: spawning, road pathing (BFS), wards/tower firing, potions, outcome
  World.Dusk.cs       end of day: walk home, empty cargo, line up
  Construct.cs        construct state + call stack for spells
  Spellbook.cs        shared spells, cycle checks
  Command.cs          CommandType/Command/Routine
  Building.cs, Job.cs, Doom.cs, TownGrid.cs, Cell.cs, Inventory.cs
  RoutineTracer.cs    dry-runs movement (spells expanded) for the route line and loop check
  World.Clone.cs      deep copy of the whole sim, mid-day state included
  DayPreview.cs       runs a day on a clone and records frames, drop-offs, jobs and problems
  Content.cs          Catalogue (buildings), Monsters, Balance (all tuning), StarterWorld (opening + Doom plan)
Scripts/Game/     Wizard.Game: the Unity side. It reads the sim and never owns the rules.
  GameBootstrap.cs    builds everything at runtime and rebuilds it for "Play again"
  GameController.cs   real-time ticking, speed, selection, build mode, toasts
  PreviewController.cs  "preview day": recompute on edits, playback position
  Visuals/            placeholder boxes: WorldView, BuildingView, ConstructView, MonsterView, Beam, RoutePreview, PreviewGhost
  UI/                 uGUI built in code: GameUI (HUD, job board, game over), RoutineEditor + CommandBlockView, PreviewPanel
Editor/           Wizard menu
Tests/EditMode/   NUnit tests for the sim (Window > General > Test Runner)
```

## Tuning

Everything lives in `Content.cs`:

- `Balance`: day length, doom day, tower HP, fortify, potion heal, construct, expansion and fortify costs, job rewards
- `Catalogue`: buildings and costs, plus ward range, damage and ammo
- `Monsters`: monster HP, damage and speed
- `StarterWorld.CreateDoom()`: the doom's waves

Here's how the current numbers play out, simulated:

| Preparation | Result |
|---|---|
| None | Lose |
| 30 tower Charges only | Lose |
| 3 wards with 10 Charges each, plus 20 tower Charges | Win with 4/20 HP |
| 4 stocked wards, 30 tower Charges, 1 Fortify and 4 Potions | Win comfortably |

Resupplying during the siege is how you'd get past the bare minimum.

## Known gaps and ideas

- No save/load yet. The world is plain data, so JSON is easy.
- Content is defined in C#. Move it to ScriptableObjects once it settles.
- For player builds, add `Universal Render Pipeline/Unlit` to *Always Included Shaders*. It's used for beams and the route line.
- Constructs don't block each other, and monsters ignore them.
- Design hooks to try:
  - "wait until full/empty" and "repeat N" blocks
  - conditional blocks
  - spell parameters, such as which side to use
  - villager reputation that unlocks better jobs
  - a doom that reveals more waves as days pass
  - a construct that the wizard can cast directly during the siege
