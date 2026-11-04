# Bunker Bots: prototype

A thousand years after the bombs, a little robot named Pip is sent up through the bunker hatch. The
surface is completely overgrown, with old tech rusting in the weeds. Clear the ground, farm it, and
salvage enough parts to build bots that take over your chores. Time always runs, Stardew-style:
each day lasts 15 minutes, from 6 am to 2 am. Every third night the people inside need a food ration.
Keep them fed through day 30 and the doors open.

## Run it

1. Let Unity finish compiling.
2. Choose **Bunker > Create Bunker Scene** from the menu bar. This saves `Assets/Bunker/Scenes/Bunker.unity`.
3. Press **Play**. The first day starts straight away.

## Controls

| Input | Action |
|---|---|
| WASD / arrows | Tap to turn and face that way; tap again or hold to walk. One tile per tick. |
| E | Do the obvious job on the tile in front of you (see below) |
| Q | Choose which seed E plants or takes: any, radish, potato or squash |
| Space | Pause / resume (or close a bot's interface) |
| Left click a bot | Open its programming interface (time stops while it's open) |
| Tab | Open the next bot's interface |
| P | Preview the rest of today (in a bot's interface, or while paused) |
| F | Cycle speed (1x, 2x, 4x, 8x) |
| C | Recentre the camera on you |
| Right-drag / middle-drag | Look around (the camera drifts back when you walk) |
| Scroll | Zoom |
| Left click a tile | Select it, or choose it when picking a Go to target |
| Esc / right click | Cancel picking, close the preview, close the interface, or deselect |

**What E does, by what's in front of you:**

- **Overgrowth or old tech:** clear it.
- **Clear soil:** till it.
- **Tilled soil:** plant the seed you chose with Q.
- **A crop:** water it, or harvest it once it's ripe.
- **The stream:** fill up with water.
- **The hatch:** deliver your load, or take seeds if you have nothing to deliver.

The bar at the bottom of the screen always shows what E will do.

## The surface

| Thing | Clear time (ticks) | You get |
|---|---|---|
| Weeds | 4 | 1 fibre; every other patch also hides a radish seed |
| Thicket | 10 | 2 fibre; every third hides a potato seed |
| Sapling | 10 | 2 wood |
| Old tree | 24 | 3 wood |
| Rubble | 14 | 2 scrap |
| Rusted car | 20 | 3 scrap, 1 circuit |
| Dead terminal | 16 | 1 scrap, 2 circuits |
| Seed cache | 8 | 4 squash seeds, 1 scrap |

A tick is a quarter of a second at 1x speed.

- **Carrying:** what comes off goes into your load, which holds 10 weight. Fibre, water, radishes and circuits weigh 1; wood, scrap and potatoes 2; squashes 3; seeds nothing. If what's ahead won't fit, you can't clear it until you deliver.
- **Using what you clear:** cleared soil can be tilled and farmed. Concrete never can. Crops and soil are walkable, so you and the bots can work along a row.
- **The stream:** runs down the east side. There's an old bridge across it in the south.

## Day, night and rations

- **The day:** runs on its own from 6 am to 2 am, 3600 ticks or 15 minutes at 1x. There's no start button. Space pauses, and F speeds things up.
- **Light:** it warms in the morning, turns orange in the evening and goes dim blue late at night. At midnight you get a reminder.
- **Night (2 am):** the screen fades to black and everything resets.
  - You and every bot are back outside the hatch, facing it.
  - Whatever anyone was carrying is handed in. Food and materials go into the stores, seeds go back on the shelf, and water is poured out.
  - Jobs in progress are dropped, and every routine starts again from line 1 in the morning.
  - Watered crops grow a day and the soil dries.
  - Then the next morning begins by itself.
- **Ration nights (days 3, 6, 9 ... 30):** the bunker eats from the stored food, smallest first.
  - Rations needed: 4, 6, 9, 12, 16, 20, 25, 30, 36 and 42 food.
  - A radish is worth 1 food, a potato 3, a squash 6.
  - The starting stores cover the first ration.
  - If there isn't enough food, that's a strike. Three strikes and the bunker seals its doors.

## The workshop

Everything is paid for from the stores. Deliver what you salvage before you can spend it.

| Recipe | Cost |
|---|---|
| Water bot | 6 scrap, 1 circuit, 2 fibre |
| Farmhand | 8 scrap, 2 circuits, 2 wood, 4 fibre |
| Bigger tanks | 3 scrap and 3 wood × the next level. Everyone (you included) carries 5 more. |
| Save seeds | 1 stored crop → 2 of its seeds |

Each bot you build adds 2 scrap to the next one's cost.

**Your first bot:** the dead terminal and rubble to the west of the hatch, plus the rusted car and weeds to the east, give exactly enough for a water bot on day 1. Building a bot opens its programming interface straight away.

## Bots and their commands

**Programming a bot:**

- **Opening the interface:** click a bot (on the map or in the Bots list), or press Tab. Time stops while it's open.
- **Building the routine:** drag blocks in. They are the same blocks as before, plus Clear, Face and Deliver. **Every job works on the tile in front of the bot.**
- **Closing:** press Done, Esc or Space. Time runs again unless you'd paused it yourself.
- **Looping:** routines always loop, over and over until 2 am.
- **After an edit:** a bot whose routine changed, or that runs a custom command you changed, starts again from line 1 where it stands. The preview and the route line already assume that.

| Command | Option (click the row) | Ticks | Does |
|---|---|---|---|
| Go to | stream / hatch / tile (pick) | 1 per tile walked | Stream and hatch: walks beside it and turns to face it. Tile: stands on that tile. |
| Step | north / east / south / west | 1 | Turns and moves one tile. |
| Face | north / east / south / west | 1 | Turns on the spot. |
| Clear | | by what's there | Clears the tile ahead into the load. |
| Till soil | | 4 | |
| Plant | any seed / a specific seed | 2 | Needs a seed in the load. |
| Water | | 2 | Uses 1 water. |
| Harvest | | 3 | |
| Grab | water or a seed type; amount with − / + | 2 | Water from the stream, seeds from the hatch. |
| Deliver | | 2 | Hands food and materials in at the hatch. |
| Wait | | 1 | |
| Run (custom) | | free | Runs a custom command, then carries on. |

**Bot kinds:**

- **Farmhands** do every job.
- **Water bots** only move, grab water and water. In a water bot's routine, blocks it can't do are faded.

**Custom commands** live under MY COMMANDS. You can keep up to 32, and a routine holds up to 300 lines per loop.

- **Water 3 East** comes ready-made. Stand just west of a row, Face east, then Run it.
- **Save as command** copies the routine on screen into a new custom command.

**The preview:** P, in a bot's interface, runs the rest of today on a copy of the world, assuming you stand still. It shows what the bot gets done, what it hands in at 2 am, what reaches the stores, and whether tonight's ration is covered.

## Code layout

```
Scripts/Sim/      Bunker.Sim: pure C#, no UnityEngine (asmdef enforces it). Deterministic, unit-tested.
  SurfaceMap.cs       ground, debris, the bunker and hatch, ASCII layout parser, pathfinding (BFS)
  Items.cs            items, weights, food values, inventories, crops
  Command.cs          command set, options, durations, Routine
  CommandLibrary.cs   custom commands (Macro), cycle checks
  Bot.cs / BotKind.cs bot state and call stack; which kind can do what (you are BotKind.Player)
  World.cs            the clock, your input, nightfall, rations and strikes
  World.Night.cs      the 2 am reset: everyone home, loads handed in, routines back to line 1
  World.Commands.cs   what each command does (all on the tile ahead), SuggestUse for E
  World.Workshop.cs   recipes: bots, tanks, saving seeds
  World.Clone.cs      deep copy (for the preview)
  DayPreview.cs       runs a day on a copy and records frames, jobs, problems, the night
  RoutineTracer.cs    quick dry-run of movement and timing (route line, footer)
  Content.cs          Balance (all tuning) and StarterSurface (ASCII layout, starting stores)
Scripts/Game/     Bunker.Game: the Unity side. It reads the sim and never owns the rules.
  GameBootstrap, GameController (always-running time, night fade, the bot interface, edits, workshop),
  PlayerController (your keys), CameraRig (follows you), WorldInput (mouse and hotkeys), PreviewController
  Visuals/            placeholder boxes: SurfaceView (ground, overgrowth, tech, bunker, crops), BotView, RoutePreview, PreviewGhost, DayLight
  UI/                 uGUI built in code: GameUI (HUD, workshop), RoutineEditor + CommandBlockView, PreviewPanel
Editor/           Bunker menu
Tests/EditMode/   NUnit tests for the sim (Window > General > Test Runner)
```

## Tuning

- **The surface:** edit `StarterSurface.Layout` in `Content.cs`. It's ASCII, one character per tile, with the first row as the north edge. The legend is in `SurfaceMap.Parse`:

  | Character | Means |
  |---|---|
  | `.` | clear soil |
  | `=` | concrete |
  | `~` | stream |
  | `t` | tilled |
  | `p` | radish planted |
  | `,` | weeds |
  | `"` | thicket |
  | `i` | sapling |
  | `T` | old tree |
  | `r` | rubble |
  | `C` | car wreck |
  | `X` | terminal |
  | `V` | seed cache |
  | `B` | bunker |
  | `H` | hatch |

- **Numbers:** `Balance` holds:
  - day length
  - ration schedule, final day and strikes
  - starting stores
  - job and clear times
  - yields
  - carry weight
  - recipe costs
  - editor limits

  Crop stats are in `Crops` in `Items.cs`.
- **Totals on this map:** clearing all of it would take one worker about 8 days. It holds 69 scrap and 20 circuits, enough for 6 or 7 bots with some tank upgrades.

## Known gaps and ideas

- **Not yet tuned:**
  - With 15-minute days you can do a lot by hand. Clearing the whole map is about 24 minutes of pure clearing work, plus walking and hauling, so roughly 3 days. Job times were raised to compensate, but bots may matter less than they should.
  - A Stardew-style battery for you (bots don't need one) is the obvious lever if that's how it plays.
  - The late rations haven't been playtested.
- No save/load.
- Overgrowth doesn't grow back yet.
- Bots don't block each other.
- **Ideas:**
  - weeds creeping back onto unused soil overnight
  - a battery for you that bots don't need
  - more bot kinds (a clearer, a hauler)
  - restoring ancient machines (a pump, a solar panel) as milestones
  - night-time dangers
