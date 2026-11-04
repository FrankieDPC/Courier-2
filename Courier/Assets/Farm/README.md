# Farm Bots: prototype

A cosy Stardew-style farm worked by little programmable bots. You build each bot's daily routine
from drag-and-drop commands and reusable custom commands. The bots till, plant, water and harvest;
shipped produce sells overnight. Earn the season goal before spring ends.

## Run it

1. Let Unity finish compiling.
2. Choose **Farm > Create Farm Scene** from the menu bar. This saves `Assets/Farm/Scenes/Farm.unity`.
3. Press **Play**, then **Start Day** (or press Space).

You start with two bots:

- **Drizzle** is a **water bot** (the one with a tank on its head). It waters the 3x3 parsnip patch: its routine goes to the well, grabs 9 water, goes to the patch and runs the **Water 3 East** custom command three times.
- **Sprocket** is a **farmhand** and can do any job. Its routine starts empty. The parsnips are ripe on day 4, and there are two rows of tilled soil waiting for seeds, so your first job is giving Sprocket a plant, harvest and ship routine.

| Input | Action |
|---|---|
| Left click | Select a bot or tile, or choose a tile while picking a Go to target |
| Right-drag / middle-drag / WASD | Pan |
| Scroll | Zoom |
| Space | Start the day, or pause/resume |
| P | Preview the selected bot's day |
| F | Cycle speed (1x, 2x, 4x, 8x) |
| Tab | Select the next bot |
| Esc / right click | Cancel picking, close the preview, or deselect |

## Bot commands

Tasks act on the **tile the bot is standing on**. Grab and Ship need the bot to be **next to** the well or pond, the seed shed, or the shipping bin.

| Command | Option (click the row) | Ticks | Does |
|---|---|---|---|
| Go to | well / tile (pick) / bin / shed / home | 1 per tile walked | Shortest walking route. "Well" means the nearest tile beside the well or pond. |
| Step | north / east / south / west | 1 | One tile. Handy inside custom commands because it works anywhere. |
| Grab | water or a seed type; amount with − / + | 1 | Takes up to the amount you set, limited by weight and by the shed's stock. |
| Till soil | | 3 | Turns farmland into tilled soil. |
| Plant | any seed / a specific seed | 1 | Needs tilled, empty soil and a seed in the load. |
| Water | | 1 | Uses 1 water. Watering an already watered tile costs no water. |
| Harvest | | 2 | Ripe crop goes into the load; the soil stays tilled for replanting. |
| Ship | | 1 | Puts all produce from the load into the bin, to be sold overnight. |
| Wait | | 1 | Does nothing for a tick. |
| Run (custom) | | free | Runs a custom command, then carries on. |

**Bot kinds:**

- **Farmhands** do every job.
- **Water bots** only do water jobs: Go to, Step, Wait and Run, plus Grab (water only) and Water.
  - In a water bot's routine, blocks it can't do are faded and won't drop in. Clicking a Grab row skips the seed options.
  - A custom command with any job the bot can't do is faded too.
  - If a job it can't do still ends up in the routine (say, by editing a custom command it runs), the bot refuses it in one tick and carries on. You get a "!", a footer warning and a line in the preview's problems.

The shop sells farmhands. Starting bots don't raise the price: the first one you buy costs 150g.

**Weight:** each bot carries up to 10 weight, and the **Bigger baskets** upgrade adds 5 more for every bot. Water and parsnips weigh 1, potatoes 2, cauliflowers 3, and seeds nothing.

**Custom commands** live under **MY COMMANDS** in the routine panel:

- Click **+ New command** to make one, or **Save as command** to copy whatever routine is on screen.
- Drag one into any routine to use it. Click a Run row, or its **edit** button, to open it.
- Editing a custom command changes every routine that uses it.
- Custom commands can run other custom commands, but never themselves.
- You can keep up to 32. The list shows four rows, then scrolls; the caption shows how many you have.

**Limits:** a routine or custom command holds up to 150 lines, one per tick of the working day. Every command except Run takes at least a tick, so a longer routine could never finish. A single Grab can ask for up to 99; the bot still only takes what its weight limit allows.

**Loop:** routines run once by default, which suits daily chores. Turn **Loop** on and the routine repeats until dusk.

## A day on the farm

1. **Planning:** edit routines, buy seeds, bots and upgrades. Press **P** to preview a bot's day.
2. **Working day (150 ticks):** bots run their routines. A wrong step shows a "!" over the bot and red on the routine row.
3. **Dusk:** bots stop, walk home by the shortest route, and unload. Produce goes in the shipping bin, unused seeds go back to the shed, and water is poured out. Dusk plays at 3× speed.
4. **Overnight:** the bin sells. Every **watered** crop grows a day, unwatered ones don't, and all soil dries.

The season lasts 14 days. Earn 2,000g from sales by the end to meet the goal.

In a test run, Sprocket harvested and replanted the patch plus the two tilled rows (15 parsnips) while Drizzle watered them in two tank trips. That earned 2,100g, just over the goal, without buying a bot or a better crop. If that's too easy, raise `SeasonGoal`.

## The preview

Press **P** (or **Preview day**) to run the day on a throwaway copy of the farm:

- **Ghost:** a pale ghost of the selected bot acts the day out, on a timeline you can scrub.
- **Line highlight:** the routine panel highlights the line being previewed.
- **Summary:** what the bot tills, plants, waters, harvests and ships, and what it unloads at dusk.
- **Overnight:** what the farm sells tonight and how many crops go thirsty.
- **Problems:** grouped by line. Click one to jump to it.

The simulation is deterministic, so the preview is exact. Editing with the preview open re-runs it instantly.

## Code layout

```
Scripts/Sim/      Farm.Sim: pure C#, no UnityEngine (asmdef enforces it). Deterministic, unit-tested.
  FarmMap.cs          tiles, structures, ASCII layout parser, pathfinding (BFS)
  Items.cs            items, weights, inventories, crop types
  Command.cs          command set, options, durations, Routine
  CommandLibrary.cs   custom commands (Macro), cycle checks
  Bot.cs              bot state, load, call stack, multi-tick work
  BotKind.cs          bot kinds (farmhand, water bot) and which commands each can do
  World.cs            day loop, shop, overnight selling and growth, season goal
  World.Commands.cs   what each command does
  World.Dusk.cs       end of day: walk home and unload
  World.Clone.cs      deep copy (for the preview)
  DayPreview.cs       runs a day on a copy and records frames, tasks, problems, overnight results
  RoutineTracer.cs    quick dry-run of movement and timing (route line, footer)
  Content.cs          Balance (all tuning and editor limits) and StarterFarm (ASCII layout, starting bots)
Scripts/Game/     Farm.Game: the Unity side. It reads the sim and never owns the rules.
  GameBootstrap.cs, GameController.cs (ticking, selection, tile picking, shop), PreviewController.cs
  Visuals/            placeholder boxes: FarmView (tiles, crops, buildings), BotView, RoutePreview, PreviewGhost
  UI/                 uGUI built in code: GameUI (HUD, shop), RoutineEditor + CommandBlockView, PreviewPanel
Editor/           Farm menu
Tests/EditMode/   NUnit tests for the sim (Window > General > Test Runner)
```

## Tuning and editing

- **The farm:** edit `StarterFarm.Layout` in `Content.cs`. It's ASCII, one character per tile, with the first row as the north edge. The legend is in `FarmMap.Parse`: `g` grass, `.` farmland, `=` path, `~` pond, `t` tilled, `p` tilled with a planted parsnip, `H` house, `D` door (where bots live), `W` well, `B` bin, `S` seed shed, `#` fence, `T` tree, `R` rock.
- **Numbers:** `Balance` holds day length, season, goal, task durations, carry weight, prices, editor limits (`MaxRoutineLength`, `MaxCustomCommands`, `MaxGrab`) and whether produce is shipped at dusk. Crop stats are in `Crops` in `Items.cs`.
- **A new crop:** add its seed and produce to `Item`, give them weights and names in `ItemInfo`, add a `CropType` to `Crops`, then give it colours in `Palette`.
- **A new command:** add it to `CommandType`, then handle it in `World.Execute`, `CommandInfo` (name and duration) and `Palette`. If water bots should be able to do it, include it in `BotKinds.IsFluidJob`.
- **Another fluid:** water bots can grab any item that `ItemInfo.IsFluid` returns true for, such as a future liquid fertiliser.
- **A new bot kind:** add it to `BotKind` and give it a rule in `BotKinds.CanDo`. Starting bots are set up in `StarterFarm.Create`.

## Known gaps and ideas

- No save/load yet.
- No debris to clear, no seasons beyond one, no crop death. Unwatered crops just don't grow.
- Bots don't block each other.
- Ideas:
  - "repeat N" and "if ripe" blocks
  - parameters on custom commands, such as "water N in a row"
  - smarter Go to targets, such as nearest dry crop or nearest ripe crop
  - fertiliser (a fluid job for water bots)
  - water bots with bigger tanks than farmhands, or a water bot in the shop
  - regrowing crops
  - scarecrows and crows as a pressure system
