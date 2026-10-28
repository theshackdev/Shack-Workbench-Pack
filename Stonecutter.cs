using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace TheShack.WorkbenchPack
{
    // A stonecutter packed into the backpack. It goes in through a recipe at a real stonecutter (the backpack has to
    // be in your inventory, plus what building a stonecutter costs), is kept in the item's custom data, and adds a
    // stonecutter station to the bench, worn or set down, so stone pieces can be built around it.
    internal static class Stonecutter
    {
        // The second station on a backpack bench. It only gives the stone build radius (Stations.GetExtensions); you
        // can't open it, since E and Tab reach the workbench, which comes first on the object.
        public static CraftingStation AddStation(GameObject host, IBench bench)
        {
            CraftingStation real = Content.Stonecutter;
            if (!real)
            {
                return null;
            }
            CraftingStation station = host.AddComponent<CraftingStation>();
            station.m_name = real.m_name;
            station.m_icon = real.m_icon;
            station.m_discoverRange = real.m_discoverRange;
            station.m_rangeBuild = Settings.CutterRadius.Value;
            station.m_extraRangePerLevel = 0f;
            station.m_craftRequireRoof = false;
            station.m_craftRequireFire = false;
            station.m_roofCheckPoint = host.transform;
            station.m_connectionPoint = host.transform;
            station.m_showBasicRecipies = false;
            station.m_useDistance = 0f;
            station.m_useAnimation = 0;
            station.m_craftingSkill = real.m_craftingSkill;
            station.m_hasCraftTab = real.m_hasCraftTab;
            station.m_canRepair = false;
            station.m_upgrader = false;
            station.m_craftItemEffects = real.m_craftItemEffects;
            station.m_craftItemDoneEffects = real.m_craftItemDoneEffects;
            station.m_repairItemDoneEffects = real.m_repairItemDoneEffects;
            station.m_craftItemDoneFailEffects = real.m_craftItemDoneFailEffects;
            Stations.Register(station, bench, Role.Cutter);
            return station;
        }

        // Crafting the add-on packs the stonecutter into a backpack (the one you wear first) instead of making an item.
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static class InventoryGui_DoCrafting
        {
            private static bool Prefix(InventoryGui __instance, Player player)
            {
                Recipe recipe = Access.CraftRecipe(__instance);
                if (!recipe || !recipe.m_item || recipe.m_item.name != Content.CutterItemName)
                {
                    return true;
                }
                Install(player, recipe);
                return false;
            }
        }

        // Pressing Craft checks the inventory has room for what the recipe makes (InventoryGui.OnCraftPressed), but the
        // add-on never goes into the inventory: crafting it packs the stonecutter into the backpack. So it always fits.
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(GameObject), typeof(int))]
        private static class Inventory_CanAddItem
        {
            private static void Postfix(GameObject prefab, ref bool __result)
            {
                if (!__result && prefab && prefab.name == Content.CutterItemName)
                {
                    __result = true;
                }
            }
        }

        private static void Install(Player player, Recipe recipe)
        {
            if (!player || player != Player.m_localPlayer)
            {
                return;
            }
            Inventory inventory = player.GetInventory();
            ItemDrop.ItemData worn = Worn.Item(player);
            ItemDrop.ItemData target = worn != null && !Content.HasCutter(worn) ? worn
                : inventory.GetAllItems().FirstOrDefault(i => Content.IsOurs(i) && !Content.HasCutter(i));
            if (target == null)
            {
                player.Message(MessageHud.MessageType.Center, "$shack_benchpack_cutterneed");
                return;
            }
            bool free = player.NoCostCheat() || (ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));
            if (!free)
            {
                // The crafting panel checked this when crafting started; the inventory may have changed since.
                if (!player.HaveRequirements(recipe, false, 1))
                {
                    player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
                    return;
                }
                // Everything but the backpack itself, which stays (it only has to be there).
                Piece.Requirement[] cost = recipe.m_resources.Where(r => r.m_resItem && !Content.IsOurs(r.m_resItem.m_itemData)).ToArray();
                player.ConsumeResources(cost, 1);
            }
            Content.SetCutter(target, true);
            // The inventory keeps its total weight and only adds it up again when items come or go.
            Access.UpdateTotalWeight(inventory);
            CraftingStation station = player.GetCurrentCraftingStation();
            if (station)
            {
                station.m_craftItemDoneEffects.Create(player.transform.position, Quaternion.identity);
            }
            player.Message(MessageHud.MessageType.Center, "$shack_benchpack_cutteradded");
            if (InventoryGui.instance && InventoryGui.IsVisible())
            {
                Access.UpdateCraftingPanel(InventoryGui.instance, false);
            }
        }
    }
}
