using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace TheShack.WorkbenchPack
{
    // The backpacks' recipes. The workbench backpack's level 1 costs the "Craft cost" setting, and upgrading it to
    // level 2-5 costs exactly what the matching workbench upgrade costs to build. The forge backpack's level 1 costs what
    // building a forge costs (or its own setting), and level 2-7 what each forge upgrade costs. All read from those
    // pieces, so it follows the game.
    //
    // The game gives each material one amount plus an amount per level, so instead the recipe lists every material
    // once and Requirement.GetAmount is answered from a table per level (0 = not needed at that level; the crafting
    // panel hides those rows). Upgrading to level N already needs a level N station (Recipe.GetRequiredStationLevel).
    internal static class Recipes
    {
        private static readonly Dictionary<Piece.Requirement, int[]> amounts = new Dictionary<Piece.Requirement, int[]>();

        public static void Rebuild()
        {
            try
            {
                if (!ObjectDB.instance)
                {
                    return;
                }
                amounts.Clear();
                RebuildPack(Content.ItemName, ParseCost(Settings.CraftCost.Value), Content.Upgrades, Content.MaxLevel, "Workbench");
                if (Content.ForgeBuildPiece)
                {
                    List<KeyValuePair<string, int>> forgeCost = ParseCost(Settings.ForgeCraftCost.Value).ToList();
                    if (forgeCost.Count == 0)
                    {
                        forgeCost = Content.ForgeBuildPiece.m_resources.Where(r => r.m_resItem && r.m_amount > 0)
                            .Select(r => new KeyValuePair<string, int>(r.m_resItem.name, r.m_amount)).ToList();
                    }
                    RebuildPack(Content.ForgeItemName, forgeCost, Content.ForgeUpgrades, Content.ForgeMaxLevel, "Forge");
                }
                RebuildCutter();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not set up the backpack's recipe: " + e);
            }
        }

        private static void RebuildPack(string itemName, IEnumerable<KeyValuePair<string, int>> baseCost, string[] upgrades,
            int maxLevel, string what)
        {
            Recipe recipe = ObjectDB.instance.m_recipes.FirstOrDefault(r => r && r.m_item && r.m_item.name == itemName);
            if (!recipe)
            {
                return;
            }

            // Material -> amount at each level (index 1-max), in the order they first appear.
            List<ItemDrop> order = new List<ItemDrop>();
            Dictionary<ItemDrop, int[]> table = new Dictionary<ItemDrop, int[]>();
            void Add(ItemDrop material, int level, int amount)
            {
                if (!material || amount <= 0)
                {
                    return;
                }
                if (!table.TryGetValue(material, out int[] perLevel))
                {
                    table[material] = perLevel = new int[maxLevel + 1];
                    order.Add(material);
                }
                perLevel[level] += amount;
            }

            foreach (KeyValuePair<string, int> cost in baseCost)
            {
                ItemDrop material = ObjectDB.instance.GetItemPrefab(cost.Key)?.GetComponent<ItemDrop>();
                if (!material)
                {
                    Plugin.Log.LogWarning($"{what} backpack craft cost: there is no item called '{cost.Key}'.");
                }
                Add(material, 1, cost.Value);
            }
            if (order.Count == 0)
            {
                Add(ObjectDB.instance.GetItemPrefab("Wood")?.GetComponent<ItemDrop>(), 1, 10);
            }

            for (int i = 0; i < upgrades.Length && i + 2 <= maxLevel; i++)
            {
                GameObject prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(upgrades[i]) : null;
                prefab = prefab ? prefab : PrefabManager.Instance.GetPrefab(upgrades[i]);
                Piece upgrade = prefab ? prefab.GetComponent<Piece>() : null;
                if (!upgrade)
                {
                    Plugin.Log.LogWarning($"{what} upgrade {upgrades[i]} is missing; level {i + 2} costs nothing.");
                    continue;
                }
                foreach (Piece.Requirement req in upgrade.m_resources)
                {
                    Add(req.m_resItem, i + 2, req.m_amount);
                }
            }

            recipe.m_resources = order.Select(material =>
            {
                int[] perLevel = table[material];
                Piece.Requirement req = new Piece.Requirement
                {
                    m_resItem = material,
                    m_amount = perLevel[1], // what discovering the recipe looks at
                    m_amountPerLevel = 0,
                    m_recover = true
                };
                amounts[req] = perLevel;
                return req;
            }).ToArray();
            recipe.m_minStationLevel = 1;
        }

        // The stonecutter add-on at a real stonecutter: the backpack (kept, it only has to be there) plus what building a
        // stonecutter costs, read from the piece so it follows the game.
        private static void RebuildCutter()
        {
            Recipe recipe = ObjectDB.instance.m_recipes.FirstOrDefault(r => r && r.m_item && r.m_item.name == Content.CutterItemName);
            ItemDrop backpack = Content.ItemPrefab ? Content.ItemPrefab.GetComponent<ItemDrop>() : null;
            if (!recipe || !backpack || !Content.StonecutterPiece)
            {
                return;
            }
            List<Piece.Requirement> list = new List<Piece.Requirement>
            {
                new Piece.Requirement { m_resItem = backpack, m_amount = 1, m_amountPerLevel = 0, m_recover = false }
            };
            list.AddRange(Content.StonecutterPiece.m_resources.Where(r => r.m_resItem && r.m_amount > 0).Select(r =>
                new Piece.Requirement { m_resItem = r.m_resItem, m_amount = r.m_amount, m_amountPerLevel = 0, m_recover = true }));
            recipe.m_resources = list.ToArray();
            recipe.m_amount = 1;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = Content.Stonecutter;
            if (!loggedCutter)
            {
                loggedCutter = true;
                string[] others = ObjectDB.instance.m_recipes.Where(r => r && r != recipe && r.m_item && r.m_craftingStation &&
                    r.m_craftingStation.m_name == Content.Stonecutter.m_name).Select(r => r.m_item.name).ToArray();
                Plugin.Log.LogInfo($"Backpack stonecutter recipe ready; the stonecutter's other recipes: " +
                    $"{(others.Length > 0 ? string.Join(", ", others) : "none")}.");
            }
        }

        private static bool loggedCutter;

        public static IEnumerable<KeyValuePair<string, int>> ParseCost(string text)
        {
            foreach (string part in (text ?? "").Split(','))
            {
                string[] pair = part.Split(':');
                if (pair.Length == 2 && int.TryParse(pair[1].Trim(), out int amount) && amount > 0 && pair[0].Trim().Length > 0)
                {
                    yield return new KeyValuePair<string, int>(pair[0].Trim(), amount);
                }
            }
        }

        [HarmonyPatch(typeof(Piece.Requirement), nameof(Piece.Requirement.GetAmount))]
        private static class Requirement_GetAmount
        {
            private static bool Prefix(Piece.Requirement __instance, int qualityLevel, ref int __result)
            {
                if (amounts.Count == 0 || !amounts.TryGetValue(__instance, out int[] perLevel))
                {
                    return true;
                }
                __result = qualityLevel >= 1 && qualityLevel < perLevel.Length ? perLevel[qualityLevel] : 0;
                return false;
            }
        }
    }

    // Weight comes from the setting. Every backpack on the ground has its own copy of the item's shared data, so it
    // is set where the weight is read.
    [HarmonyPatch(typeof(ItemDrop.ItemData))]
    internal static class ItemData_Weight
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ItemDrop.ItemData.GetWeight))]
        private static void GetWeight(ItemDrop.ItemData __instance) => Apply(__instance);

        [HarmonyPrefix]
        [HarmonyPatch(nameof(ItemDrop.ItemData.GetNonStackedWeight))]
        private static void GetNonStackedWeight(ItemDrop.ItemData __instance) => Apply(__instance);

        // The game reads the maximum here for the durability bar, the tooltip and repairs.
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ItemDrop.ItemData.GetMaxDurability), typeof(int))]
        private static void GetMaxDurability(ItemDrop.ItemData __instance) => Apply(__instance);

        private static void Apply(ItemDrop.ItemData item)
        {
            bool forge = Content.IsForge(item);
            if (!forge && !Content.IsOurs(item))
            {
                return;
            }
            Content.ApplyShared(item.m_shared, forge);
            item.m_shared.m_weight = Content.Weight(item);
            float max = Content.MaxDurability(item.m_quality, forge);
            // Backpacks from before durability (1.0.x) start full once. Durability above the maximum (an admin lowered
            // it) is left alone here, so raising it back loses nothing; repairs and packing up set it within range.
            if (item.m_customData != null && !item.m_customData.ContainsKey(Content.SeenData))
            {
                item.m_customData[Content.SeenData] = "1";
                item.m_durability = max;
            }
        }
    }

    // Upgrading gives a new backpack one level up; the stonecutter packed in the old one goes into it.
    [HarmonyPatch]
    internal static class Upgrade_KeepsStonecutter
    {
        private static bool carry;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static void DoCrafting(InventoryGui __instance)
        {
            ItemDrop.ItemData upgrading = Access.CraftUpgradeItem(__instance);
            carry = Content.IsOurs(upgrading) && Content.HasCutter(upgrading);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static void DoCraftingDone() => carry = false;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(string), typeof(int), typeof(int), typeof(int),
            typeof(long), typeof(string), typeof(Vector2i), typeof(bool), typeof(bool), typeof(bool))]
        private static void AddItem(Inventory __instance, ItemDrop.ItemData __result)
        {
            if (carry && Content.IsOurs(__result))
            {
                Content.SetCutter(__result, true);
                Access.UpdateTotalWeight(__instance);
                carry = false;
            }
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(ItemDrop.ItemData), typeof(int),
        typeof(bool), typeof(float), typeof(int), typeof(bool))]
    internal static class ItemData_GetTooltip
    {
        private static void Postfix(ItemDrop.ItemData item, int qualityLevel, ref string __result)
        {
            if (Content.IsOurs(item))
            {
                __result += Content.Tooltip(Mathf.Clamp(qualityLevel, 1, Content.MaxLevel), Content.HasCutter(item));
            }
            else if (Content.IsForge(item))
            {
                __result += Content.ForgeTooltip(Mathf.Clamp(qualityLevel, 1, Content.ForgeMaxLevel));
            }
        }
    }
}
