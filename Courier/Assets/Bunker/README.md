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
| WASD / arrows | Walk freely, diagonals too. You face the way you're heading, and turning never stops you. |
| E | Do the obvious job on the tile in front of you (see below) |
| Q | Choose which seed E plants or takes: any, radish, potato or squash |
| X | Dump the water in your bag, to make room |
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

**Moving:** you glide freely between tiles at about 5.5 tiles a second, much faster than any bot, and slide along anything in the way. E works on the tile next to the one under you, in the direction you face; a yellow outline marks it. While a job is underway you stand still. Pressing a new direction walks away and drops the job.

**What E does, by what's in front of you:**

- **Overgrowth or old tech:** clear it.
- **Clear soil:** till it.
- **Tilled soil:** plant the seed you chose with Q.
- **A crop:** water it, or harvest it once it's ripe. Watering does nothing while the soil is still wet.
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

- **Carrying:** what comes off goes into your bag, which holds 10 weight. Fibre, water, radishes and circuits weigh 1; wood, scrap and potatoes 2; squashes 3. If what's ahead won't fit, you can't clear it until you deliver, or dump your water (X).
- **Seeds:** they go in a separate seed pouch and take up no room in the bag. The HUD lists them on their own line, and a bot carrying only seeds shows no crate.
- **Using what you clear:** cleared soil can be tilled and farmed. Concrete never can. Crops and soil are walkable, so you and the bots can work along a row.
- **Around the hatch:** it's overgrown right up to the door. Only a few tiles of the concrete pad are clear to stand on. The rest of the pad is weeds and rubble, and the soil south of it is thicket, saplings and weeds. There's no clear soil anywhere to start with.
- **The stream:** runs down the east side. There's an old bridge across it in the south.

## Water and growing

- **Watering:** one watering keeps a tile wet for 3 in-game hours (2¼ minutes at 1x). Then it dries out.
- **Growing:** crops grow only while their soil is wet, a tick at a time. A crop needs so many hours of wet soil, not so many days:
  - radish: 12 hours
  - potato: 30 hours
  - squash: 60 hours

  A radish kept wet all day ripens before night, and a crop left dry just waits.
- **Rewatering:** you can water a tile again once it's down to half its water, when the soil turns from dark (wet) to medium (damp). Watering earlier says "already done", so a looping water bot doesn't pour its tank away. Dry soil is light brown.
- **Tilled soil:** you can water it before planting.
- **Night:** soil and crops carry on as they were.

## Day, night and rations

- **The day:** runs on its own from 6 am to 2 am, 3600 ticks or 15 minutes at 1x. There's no start button. Space pauses, and F speeds things up.
- **Light:** it warms in the morning, turns orange in the evening and goes dim blue late at night. At midnight you get a reminder.
- **Night (2 am):** the screen fades to black and everything resets.
  - You and every bot are back outside the hatch, facing it.
  - Whatever anyone was carrying is handed in. Food and materials go into the stores, seeds go back on the shelf, and water is poured out.
  - Jobs in progress are dropped, and every routine starts again from line 1 in the morning.
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
| Farmhand | 6 scrap, 2 circuits, 2 wood, 4 fibre |
| Water bot (once you have a farmhand) | 6 scrap, 1 circuit, 2 fibre |
| Bigger tanks | 3 scrap and 3 wood × the next level. Everyone (you included) carries 5 more. |
| Save seeds | 1 stored crop → 2 of its seeds |

Bot prices and seeds saved are defaults; tune them in the game data asset (below).

Each bot you build adds 2 scrap to the next one's cost.

**Your first bot is a farmhand,** to help with the clearing. To pay for it, clear what's around the hatch:

- West along the pad: the dead terminal and two lots of rubble.
- East: the rubble at the end of the pad.
- A sapling just south of the pad, for wood.
- Weeds, for fibre.

Water bots unlock once a farmhand exists. The rusted car east of the pad and more rubble nearby go most of the way towards one. Building a bot opens its programming interface straight away.

## Bots and their commands

**Programming a bot:**

- **Opening the interface:** click a bot (on the map or in the Bots list), or press Tab. Time stops while it's open.
- **Building the routine:** drag blocks in. They are the same blocks as before, plus Clear, Face and Deliver. **Every job works on the tile in front of the bot.**
- **Closing:** press Done, Esc or Space. Time runs again unless you'd paused it yourself.
- **Looping:** routines always loop, over and over until 2 am.
- **After an edit:** a bot whose routine changed, or that runs a custom command you changed, starts again from line 1 where it stands. The preview and the route line already assume that.

| Command | Option (click the row) | Ticks | Does |
|---|---|---|---|
| Go to | stream / hatch / tile (pick) | 4 per tile walked | Stream and hatch: walks beside it and turns to face it. Tile: stands on that tile. |
| Step | north / east / south / west | 4 | Turns and moves one tile. |
| Face | north / east / south / west | 1 | Turns on the spot. |
| Clear | | by what's there | Clears the tile ahead into the load. |
| Till soil | | 4 | |
| Plant | any seed / a specific seed | 2 | Needs a seed in the load. |
| Water | | 2 | Uses 1 water. Wets tilled soil for 3 hours; "already done" while it's still more than half wet. |
| Harvest | | 3 | |
| Grab | water or a seed type; amount with − / + | 2 | Water from the stream, seeds from the hatch. Grabbing water with a full tank is "already done", not an error. |
| Deliver | | 2 | Hands food and materials in at the hatch. |
| Dump water | | 1 | Pours all the water out of the load, anywhere, to make room. Water bots can do it too. |
| Wait | | 1 | |
| Run (custom) | | free | Runs a custom command, then carries on. |

**Bot kinds:**

- **Farmhands** do every job.
- **Water bots** only move, grab water, water and dump water. In a water bot's routine, blocks it can't do are faded.

**Custom commands** live under MY COMMANDS. You can keep up to 32, and a routine holds up to 300 lines per loop.

- **Clear 3 East** comes ready-made, for your farmhand. It faces east, then clears and steps three times. Looping, it keeps clearing eastwards until its load is full (loads are handed in at night).
- **Water 3 East** also comes ready-made. Stand just west of a row, Face east, then Run it.
- **Save as command** copies the routine on screen into a new custom command.

**The preview:** P, in a bot's interface, runs the rest of today on a copy of the world, assuming you stand still. It shows what the bot gets done, what it hands in at 2 am, what reaches the stores, and whether tonight's ration is covered.

Bots are slow walkers: by default a tile takes them 4 ticks, or 1 second at 1x. Tune this per kind in the data asset.

## Game data asset (bot and crop stats)

`Assets/Bunker/Resources/BunkerData.asset` holds the stats you're most likely to tune. It's created for you the first time Unity compiles these scripts, with the built-in numbers. To find it, use **Bunker > Edit Game Data**.

| Section | Field | What it does |
|---|---|---|
| You | Player tiles per second | Your walking speed, in real time (game speed doesn't change it) |
| You | Player capacity | Your carry weight before upgrades |
| Water bot / Farmhand | Ticks per tile | Walking speed; 4 = one tile a second at 1x |
| Water bot / Farmhand | Capacity | Carry weight before upgrades |
| Water bot / Farmhand | Scrap, circuits, wood, fibre | Workshop price; each bot built still adds 2 scrap |
| Radish / Potato / Squash | Grow hours | Hours of wet soil before harvest (a day is 20 hours) |
| Radish / Potato / Squash | Food | Ration value of one crop |
| Radish / Potato / Squash | Weight | Carry weight of one crop |
| Radish / Potato / Squash | Seeds saved | Seeds from saving one crop in the workshop |
| Soil | Water lasts hours | How long one watering keeps the soil wet |

- **When edits apply:** at the start of play, and live while playing. Changes show up within a frame: carry limits, prices, the crop guide, the route line, the footer and the preview all update.
- **A different asset:** to use one other than the default, drag it onto the **Data** field of the Game object's GameBootstrap.
- **Tests:** the EditMode tests always use the built-in numbers, whatever the asset says.
- **Older assets:** an asset made by an earlier version is updated when Unity loads it:
  - crop grow times change from days to the new hour values;
  - the water setting is added;
  - the farmhand's scrap drops from 8 to 6, unless you'd changed it.

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
  GameBootstrap, GameController (always-running time, night fade, the bot interface, edits, workshop, live data),
  PlayerController (your free movement and keys), CameraRig (follows you), WorldInput (mouse and hotkeys),
  PreviewController, BunkerData (the stats asset)
  Visuals/            placeholder boxes: SurfaceView (ground, overgrowth, tech, bunker, crops), BotView, RoutePreview, PreviewGhost, DayLight
  UI/                 uGUI built in code: GameUI (HUD, workshop), RoutineEditor + CommandBlockView, PreviewPanel
Editor/           Bunker menu; BunkerDataSetup creates the data asset
Tests/EditMode/   NUnit tests for the sim (Window > General > Test Runner)
```

## Tuning

- **The surface:** edit `StarterSurface.Layout` in `Content.cs`. It's ASCII, one character per tile, with the first row as the north edge. The legend is in `SurfaceMap.Parse`:

  | Character | Means |
  |---|---|
  | `.` | clear soil |
  | `=` | concrete |
  | `;` | weeds on concrete |
  | `k` | rubble on concrete |
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

- **Bot and crop stats:** use the game data asset (above). Its starting values come from `BotKinds.DefaultStats` and `Crops` in code.
- **Everything else:** `Balance` holds:
  - day length
  - ration schedule, final day and strikes
  - starting stores
  - job and clear times
  - yields
  - the Bigger tanks upgrade
  - editor limits
- **Totals on this map:** it holds 73 scrap and 20 circuits, enough for 6 or 7 bots with some tank upgrades. Clearing all of it is about 26 minutes of pure clearing work, before walking and hauling.

## Known gaps and ideas

- **Not yet tuned:**
  - With 15-minute days you can do a lot by hand. Clearing the whole map is about 24 minutes of pure clearing work, plus walking and hauling, so roughly 3 days. Job times were raised to compensate, but bots may matter less than they should.
  - A Stardew-style battery for you (bots don't need one) is the obvious lever if that's how it plays.
  - The late rations haven't been playtested.
  - Hourly growth with drying water hasn't been playtested either. A radish kept wet all day yields about 1.7 a day, so a water bot looking after a few rows may make rations easy. Raise grow hours, or shorten how long water lasts, in the data asset if it does.
- No save/load.
- Overgrowth doesn't grow back yet.
- Bots don't block each other.
- **Ideas:**
  - weeds creeping back onto unused soil overnight
  - a battery for you that bots don't need
  - more bot kinds (a clearer, a hauler)
  - restoring ancient machines (a pump, a solar panel) as milestones
  - night-time dangers
