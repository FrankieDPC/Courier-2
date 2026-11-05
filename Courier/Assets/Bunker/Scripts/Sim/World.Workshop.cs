using System.Collections.Generic;

namespace Bunker.Sim
{
    /// <summary>Things you can make in the bunker workshop from what's in the stores.</summary>
    public enum Recipe
    {
        Farmhand,

        /// <summary>Unlocks once you've built a farmhand.</summary>
        WaterBot,

        /// <summary>Every bot (and you) can carry more.</summary>
        BiggerTanks,

        // Turn one stored crop into seeds, so a harvest can be replanted.
        SaveRadishSeeds,
        SavePotatoSeeds,
        SaveSquashSeeds,
    }

    // The workshop: build bots, upgrade carrying, save seeds. Everything is paid for from the stores.
    public sealed partial class World
    {
        public static readonly Recipe[] AllRecipes =
        {
            Recipe.Farmhand, Recipe.WaterBot, Recipe.BiggerTanks,
            Recipe.SaveRadishSeeds, Recipe.SavePotatoSeeds, Recipe.SaveSquashSeeds,
        };

        /// <summary>Raised after something is made in the workshop.</summary>
        public event System.Action<Recipe> Built;

        public static CropType CropFor(Recipe r)
        {
            switch (r)
            {
                case Recipe.SaveRadishSeeds: return Crops.Radish;
                case Recipe.SavePotatoSeeds: return Crops.Potato;
                case Recipe.SaveSquashSeeds: return Crops.Squash;
                default: return null;
            }
        }

        /// <summary>What a recipe costs right now (bots and upgrades get pricier).</summary>
        public List<ItemAmount> CostOf(Recipe r)
        {
            var list = new List<ItemAmount>();
            int extra = Balance.ExtraScrapPerBot * BotsBuilt;
            switch (r)
            {
                case Recipe.WaterBot:
                case Recipe.Farmhand:
                {
                    // The kind's base price, plus extra scrap for every bot already built.
                    var stats = BotKinds.Stats(r == Recipe.WaterBot ? BotKind.WaterBot : BotKind.Farmhand);
                    bool hasScrap = false;
                    foreach (var a in stats.Cost)
                    {
                        if (a.Amount <= 0) continue;
                        bool scrap = a.Item == Item.Scrap;
                        hasScrap |= scrap;
                        list.Add(new ItemAmount(a.Item, a.Amount + (scrap ? extra : 0)));
                    }
                    if (!hasScrap && extra > 0) list.Insert(0, new ItemAmount(Item.Scrap, extra));
                    break;
                }
                case Recipe.BiggerTanks:
                    list.Add(new ItemAmount(Item.Scrap, Balance.TankScrap * (CapacityLevel + 1)));
                    list.Add(new ItemAmount(Item.Wood, Balance.TankWood * (CapacityLevel + 1)));
                    break;
                default:
                    var crop = CropFor(r);
                    if (crop != null) list.Add(new ItemAmount(crop.Produce, 1));
                    break;
            }
            return list;
        }

        /// <summary>Why a recipe can't be made yet regardless of cost, or null if it's unlocked.</summary>
        public string LockedReason(Recipe r)
        {
            if (r == Recipe.WaterBot && !HasBot(BotKind.Farmhand)) return "Build a farmhand first";
            return null;
        }

        public bool IsUnlocked(Recipe r) => LockedReason(r) == null;

        bool HasBot(BotKind kind)
        {
            foreach (var b in _bots)
                if (b.Kind == kind) return true;
            return false;
        }

        /// <summary>Unlocked, and the stores can pay for it.</summary>
        public bool CanAfford(Recipe r)
        {
            if (IsGameOver || !IsUnlocked(r)) return false;
            foreach (var a in CostOf(r))
                if (Stores.Get(a.Item) < a.Amount) return false;
            return true;
        }

        /// <summary>
        /// Makes the recipe if the stores can pay for it. New bots appear at the hatch with an empty
        /// routine (<paramref name="newBot"/>); they start looping it as soon as time runs.
        /// </summary>
        public bool TryBuild(Recipe r, out Bot newBot)
        {
            newBot = null;
            if (!CanAfford(r)) return false;
            foreach (var a in CostOf(r)) Stores.Remove(a.Item, a.Amount);

            switch (r)
            {
                case Recipe.WaterBot:
                case Recipe.Farmhand:
                {
                    var kind = r == Recipe.WaterBot ? BotKind.WaterBot : BotKind.Farmhand;
                    int ofKind = 0;
                    foreach (var b in _bots)
                        if (b.Kind == kind) ofKind++;
                    BotsBuilt++;
                    newBot = AddBot(Balance.BotName(kind, ofKind), kind);
                    break;
                }
                case Recipe.BiggerTanks:
                    CapacityLevel++;
                    RefreshStats();
                    break;
                default:
                    var crop = CropFor(r);
                    if (crop != null) Stores.Add(crop.Seed, crop.SeedsSaved);
                    break;
            }
            Built?.Invoke(r);
            return true;
        }

        public bool TryBuild(Recipe r) => TryBuild(r, out _);
    }
}
