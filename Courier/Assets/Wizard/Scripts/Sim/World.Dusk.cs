using System;
using System.Collections.Generic;

namespace Wizard.Sim
{
    // Dusk: when the day's work is done, every construct stops its routine, walks home along the
    // roads (one cell per tick, shortest route), empties its cargo into the tower and lines up at its
    // start position facing its start direction. The day ends once everyone is home.
    public sealed partial class World
    {
        readonly Dictionary<Cell, int[,]> _homeDistance = new Dictionary<Cell, int[,]>();

        /// <summary>The working day ended and constructs are heading home.</summary>
        public event Action DuskStarted;

        /// <summary>A construct emptied its cargo into the tower at the end of the day.</summary>
        public event Action<Construct, Resource, int> CargoBanked;

        void BeginDusk()
        {
            Phase = DayPhase.Dusk;
            _homeDistance.Clear(); // the map may have grown since last time
            foreach (var c in _constructs)
            {
                c.ResetProgram(); // clears LastPath: no routine line is running at dusk
                c.PreviousPosition = c.Position;
                c.PreviousFacing = c.Facing;
                if (c.Position == c.Home) SettleAtHome(c);
            }
            DuskStarted?.Invoke();
        }

        void DuskStep()
        {
            foreach (var c in _constructs)
            {
                c.PreviousPosition = c.Position;
                c.PreviousFacing = c.Facing;
                if (c.Position == c.Home) continue;

                var dist = HomeDistance(c.Home);
                int d = Grid.InBounds(c.Position) ? dist[c.Position.X, c.Position.Y] : -1;
                if (d < 0)
                {
                    c.Position = c.Home; // stranded off the road network: magic it home
                }
                else
                {
                    for (int f = 0; f < 4; f++)
                    {
                        var next = c.Position.Step((Facing)f);
                        if (Grid.InBounds(next) && dist[next.X, next.Y] == d - 1)
                        {
                            c.Facing = (Facing)f;
                            c.Position = next;
                            break;
                        }
                    }
                }

                if (c.Position == c.Home) SettleAtHome(c);
            }
        }

        void SettleAtHome(Construct c)
        {
            if (c.IsCarrying)
            {
                var r = c.Cargo;
                int n = c.CargoCount;
                c.RemoveCargo(n);
                if (Balance.BankCargoAtDusk)
                {
                    Receive(Tower, r, n); // counts as a tower delivery (stores, collect jobs)
                    c.DeliveredToday += n;
                }
                CargoBanked?.Invoke(c, r, n);
            }
            c.Facing = c.HomeFacing;
        }

        bool AllHome()
        {
            foreach (var c in _constructs)
                if (c.Position != c.Home || c.IsCarrying || c.Facing != c.HomeFacing) return false;
            return true;
        }

        /// <summary>Road steps from every cell to <paramref name="home"/> over drivable roads (-1 = unreachable).</summary>
        int[,] HomeDistance(Cell home)
        {
            if (_homeDistance.TryGetValue(home, out var cached)) return cached;

            var dist = new int[Grid.Width, Grid.Height];
            for (int x = 0; x < Grid.Width; x++)
            for (int y = 0; y < Grid.Height; y++)
                dist[x, y] = -1;

            if (Grid.InBounds(home))
            {
                var queue = new Queue<Cell>();
                dist[home.X, home.Y] = 0;
                queue.Enqueue(home);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    for (int f = 0; f < 4; f++)
                    {
                        var n = c.Step((Facing)f);
                        if (!Grid.IsDrivable(n) || dist[n.X, n.Y] >= 0) continue;
                        dist[n.X, n.Y] = dist[c.X, c.Y] + 1;
                        queue.Enqueue(n);
                    }
                }
            }

            _homeDistance[home] = dist;
            return dist;
        }
    }
}
