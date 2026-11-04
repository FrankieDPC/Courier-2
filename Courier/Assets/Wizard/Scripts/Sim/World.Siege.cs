using System;
using System.Collections.Generic;

namespace Wizard.Sim
{
    // The doom: on the final day monsters leave the gates at the map edge, follow the roads (through
    // the unexplored wilds too) to the tower, and hit it. Wards and the tower shoot them using Charges.
    public sealed partial class World
    {
        struct Spawn
        {
            public int Tick;
            public Facing Gate;
            public MonsterType Type;
        }

        readonly List<Monster> _monsters = new List<Monster>();
        readonly List<Spawn> _spawnQueue = new List<Spawn>();
        int _nextSpawn;
        int _nextMonsterId = 1;
        int _healTimer;
        int[,] _distance;
        bool _distanceDirty = true;

        public IReadOnlyList<Monster> Monsters => _monsters;
        public int MonstersSlain { get; private set; }
        public int MonstersArrived { get; private set; }

        public float SiegeProgress01 =>
            Doom.TotalMonsters == 0 ? 1f : (MonstersSlain + MonstersArrived) / (float)Doom.TotalMonsters;

        public event Action<Monster> MonsterSpawned;

        /// <summary>The monster left the board: killed (true) or spent attacking the tower (false).</summary>
        public event Action<Monster, bool> MonsterRemoved;

        /// <summary>A ward (or the tower) fired at a monster.</summary>
        public event Action<Building, Monster> Shot;

        public event Action<int> TowerDamaged;
        public event Action<int> TowerHealed;

        /// <summary>Road cell at the map edge where a gate's monsters appear.</summary>
        public Cell GateCell(Facing gate)
        {
            int p = Grid.Pitch;
            int roadX = Math.Max(0, Math.Min(Grid.Width - 1, (int)Math.Round(Tower.Cell.X / (double)p) * p));
            int roadY = Math.Max(0, Math.Min(Grid.Height - 1, (int)Math.Round(Tower.Cell.Y / (double)p) * p));
            switch (gate)
            {
                case Facing.North: return new Cell(roadX, Grid.Height - 1);
                case Facing.South: return new Cell(roadX, 0);
                case Facing.East: return new Cell(Grid.Width - 1, roadY);
                default: return new Cell(0, roadY);
            }
        }

        /// <summary>Road steps from a cell to the tower's doorstep (0 = adjacent to the tower), or -1.</summary>
        public int DistanceToTower(Cell c)
        {
            EnsureDistanceField();
            return Grid.InBounds(c) ? _distance[c.X, c.Y] : -1;
        }

        /// <summary>The defence stats the tower itself fires with.</summary>
        public static DefenceStats TowerDefence => Balance.TowerDefence;

        public int AmmoOf(Building b) =>
            b.Type.IsTower ? Stockpile.Get(Balance.TowerDefence.Ammo)
            : b.Type.IsWard ? b.Input.Get(b.Type.Defence.Ammo)
            : 0;

        void BeginSiege()
        {
            _monsters.Clear();
            _spawnQueue.Clear();
            _nextSpawn = 0;
            _healTimer = 0;
            _distanceDirty = true;

            foreach (var w in Doom.Waves)
                for (int i = 0; i < w.Count; i++)
                    _spawnQueue.Add(new Spawn { Tick = w.Tick + i * w.Spacing, Gate = w.Gate, Type = w.Type });
            _spawnQueue.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        }

        void SiegeStep()
        {
            SpawnDue();
            MoveMonsters();
            FireDefences();
            RemoveDead();
            HealTower();

            if (TowerHp <= 0)
            {
                TowerHp = 0;
                Outcome = Outcome.Defeat;
            }
            else if ((_nextSpawn >= _spawnQueue.Count && _monsters.Count == 0) || Tick + 1 >= Balance.SiegeMaxTicks)
            {
                Outcome = Outcome.Victory;
            }
        }

        void SpawnDue()
        {
            while (_nextSpawn < _spawnQueue.Count && _spawnQueue[_nextSpawn].Tick <= Tick)
            {
                var s = _spawnQueue[_nextSpawn++];
                var m = new Monster(_nextMonsterId++, s.Type, GateCell(s.Gate));
                _monsters.Add(m);
                MonsterSpawned?.Invoke(m);
            }
        }

        void MoveMonsters()
        {
            EnsureDistanceField();
            for (int i = _monsters.Count - 1; i >= 0; i--)
            {
                var m = _monsters[i];
                m.PreviousCell = m.Cell;
                if (--m.MoveTimer > 0) continue;
                m.MoveTimer = m.Type.TicksPerMove;

                int d = _distance[m.Cell.X, m.Cell.Y];
                if (d == 0)
                {
                    // At the doorstep: smash into the tower and dissipate.
                    TowerHp -= m.Type.Damage;
                    MonstersArrived++;
                    _monsters.RemoveAt(i);
                    TowerDamaged?.Invoke(m.Type.Damage);
                    MonsterRemoved?.Invoke(m, false);
                    continue;
                }

                for (int f = 0; f < 4; f++)
                {
                    var next = m.Cell.Step((Facing)f);
                    if (Grid.InBounds(next) && _distance[next.X, next.Y] == d - 1)
                    {
                        m.Cell = next;
                        break;
                    }
                }
            }
        }

        void FireDefences()
        {
            foreach (var b in _buildings)
            {
                var stats = b.Type.IsTower ? Balance.TowerDefence : b.Type.Defence;
                if (stats == null || !(b.Type.IsWard || b.Type.IsTower)) continue;
                if (b.Cooldown > 0)
                {
                    b.Cooldown--;
                    continue;
                }

                var ammo = b.Type.IsTower ? Stockpile : b.Input;
                if (ammo.Get(stats.Ammo) <= 0) continue;

                var target = PickTarget(b.Cell, stats.Range);
                if (target == null) continue;

                ammo.Remove(stats.Ammo, 1);
                target.Hp -= stats.Damage;
                b.Cooldown = stats.Cooldown;
                Shot?.Invoke(b, target);
            }
        }

        Monster PickTarget(Cell from, int range)
        {
            Monster best = null;
            int bestDistance = int.MaxValue;
            foreach (var m in _monsters)
            {
                if (m.IsDead) continue;
                int dx = Math.Abs(m.Cell.X - from.X);
                int dy = Math.Abs(m.Cell.Y - from.Y);
                if (Math.Max(dx, dy) > range) continue;
                int d = _distance[m.Cell.X, m.Cell.Y];
                if (d < bestDistance || (d == bestDistance && best != null && m.Id < best.Id))
                {
                    best = m;
                    bestDistance = d;
                }
            }
            return best;
        }

        void RemoveDead()
        {
            for (int i = _monsters.Count - 1; i >= 0; i--)
            {
                var m = _monsters[i];
                if (!m.IsDead) continue;
                _monsters.RemoveAt(i);
                MonstersSlain++;
                MonsterRemoved?.Invoke(m, true);
            }
        }

        void HealTower()
        {
            if (_healTimer > 0)
            {
                _healTimer--;
                return;
            }
            if (TowerHp <= 0 || TowerHp >= TowerMaxHp || Stockpile.Get(Resource.Potion) <= 0) return;

            Stockpile.Remove(Resource.Potion, 1);
            int healed = Math.Min(Balance.PotionHeal, TowerMaxHp - TowerHp);
            TowerHp += healed;
            _healTimer = Balance.PotionCooldown;
            TowerHealed?.Invoke(healed);
        }

        /// <summary>BFS over every road cell (locked or not) out from the tower's doorstep.</summary>
        void EnsureDistanceField()
        {
            if (!_distanceDirty && _distance != null) return;
            _distanceDirty = false;

            _distance = new int[Grid.Width, Grid.Height];
            for (int x = 0; x < Grid.Width; x++)
            for (int y = 0; y < Grid.Height; y++)
                _distance[x, y] = -1;
            if (Tower == null) return;

            var queue = new Queue<Cell>();
            for (int f = 0; f < 4; f++)
            {
                var door = Tower.Cell.Step((Facing)f);
                if (!IsMonsterRoad(door)) continue;
                _distance[door.X, door.Y] = 0;
                queue.Enqueue(door);
            }

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                for (int f = 0; f < 4; f++)
                {
                    var n = c.Step((Facing)f);
                    if (!IsMonsterRoad(n) || _distance[n.X, n.Y] >= 0) continue;
                    _distance[n.X, n.Y] = _distance[c.X, c.Y] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        bool IsMonsterRoad(Cell c) => Grid.InBounds(c) && Grid.KindAt(c) == CellKind.Road;
    }
}
