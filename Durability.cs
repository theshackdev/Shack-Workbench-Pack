using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheShack.WorkbenchPack
{
    // Each craft, upgrade or repair done at a backpack bench (worn or set down) or a set-down forge backpack wears it.
    // Repairing a backpack needs another station of its kind of at least its level, never itself.
    internal static class Durability
    {
        // The bench a craft runs at, from the start of DoCrafting until its cost is paid.
        private static IBench crafting;

        // Only recipes that need this station (a workbench's, or a forge's): the basic ones the inventory crafts on its
        // own cost nothing.
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static class InventoryGui_DoCrafting
        {
            private static void Prefix(InventoryGui __instance)
            {
                Recipe recipe = Access.CraftRecipe(__instance);
                Player player = Player.m_localPlayer;
                CraftingStation station = player ? player.GetCurrentCraftingStation() : null;
                crafting = recipe && recipe.m_craftingStation && station &&
                    recipe.m_craftingStation.m_name == station.m_name ? Stations.Current(player) : null;
            }

            private static void Finalizer() => crafting = null;
        }

        // A craft or upgrade is done once its cost is paid (not with no-cost cheats or the no craft cost world
        // setting). Multi-crafting pays once for several, so it wears that many times.
        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
        private static class Player_ConsumeResources
        {
            private static void Postfix(int multiplier)
            {
                if (crafting == null)
                {
                    return;
                }
                IBench bench = crafting;
                crafting = null;
                bench.Wear(Settings.WearPerUse.Value * Mathf.Max(1, multiplier));
            }
        }

        // RepairOneItem repairs at most one item: if one fewer is worn afterwards, the bench did a repair.
        private static IBench repairing;
        private static int wornBefore;
        private static readonly List<ItemDrop.ItemData> worn = new List<ItemDrop.ItemData>();

        private static int CountWorn(Player player)
        {
            worn.Clear();
            player.GetInventory().GetWornItems(worn);
            return worn.Count;
        }

        [HarmonyPatch(typeof(InventoryGui), "RepairOneItem")]
        private static class InventoryGui_RepairOneItem
        {
            private static void Prefix()
            {
                Player player = Player.m_localPlayer;
                repairing = Stations.Current(player);
                wornBefore = repairing != null ? CountWorn(player) : 0;
            }

            private static void Postfix()
            {
                IBench bench = repairing;
                repairing = null;
                if (bench != null && Player.m_localPlayer && CountWorn(Player.m_localPlayer) < wornBefore)
                {
                    bench.Wear(Settings.WearPerUse.Value);
                }
            }
        }

        [HarmonyPatch(typeof(InventoryGui), "CanRepair")]
        private static class InventoryGui_CanRepair
        {
            private static void Postfix(ItemDrop.ItemData item, ref bool __result)
            {
                Player player = Player.m_localPlayer;
                if (!__result || !(Content.IsOurs(item) || Content.IsForge(item)) || !player || player.NoCostCheat())
                {
                    return;
                }
                CraftingStation station = player.GetCurrentCraftingStation();
                if (!station)
                {
                    return;
                }
                if (Stations.TryGet(station, out IBench bench) && bench is BackBench back && back.IsFor(item))
                {
                    __result = false;
                }
                else if (Settings.RepairNeedsLevel.Value && station.GetLevel() < item.m_quality)
                {
                    __result = false;
                }
            }
        }
    }
}
