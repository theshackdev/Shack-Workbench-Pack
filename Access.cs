using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheShack.WorkbenchPack
{
    // The game's private members this mod uses, resolved once at startup (Init) so a game update that renames one
    // fails loudly in the log instead of halfway through setting a bench down.
    internal static class Access
    {
        public delegate bool PieceRayTestFn(Player player, out Vector3 point, out Vector3 normal, out Piece piece,
            out Heightmap heightmap, out Collider waterSurface, bool water);

        public static AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> UtilityItem;
        public static AccessTools.FieldRef<Player, CraftingStation> CurrentStation;
        public static AccessTools.FieldRef<CraftingStation, List<StationExtension>> AttachedExtensions;
        public static AccessTools.FieldRef<CraftingStation, float> BuildRange;
        public static AccessTools.FieldRef<CraftingStation, CircleProjector> AreaMarkerCircle;
        public static AccessTools.FieldRef<VisEquipment, List<GameObject>> UtilityItemInstances;
        public static PieceRayTestFn PieceRayTest;
        public static Func<Player, bool> TakeInput;
        public static Action<InventoryGui, bool> UpdateCraftingPanel;
        public static AccessTools.FieldRef<InventoryGui, Recipe> CraftRecipe;
        public static AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> CraftUpgradeItem;
        public static Action<Inventory> UpdateTotalWeight;
        public static AccessTools.FieldRef<Character, ZSyncAnimation> Zanim;

        public static void Init()
        {
            UtilityItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_utilityItem");
            CurrentStation = AccessTools.FieldRefAccess<Player, CraftingStation>("m_currentStation");
            AttachedExtensions = AccessTools.FieldRefAccess<CraftingStation, List<StationExtension>>("m_attachedExtensions");
            BuildRange = AccessTools.FieldRefAccess<CraftingStation, float>("m_buildRange");
            AreaMarkerCircle = AccessTools.FieldRefAccess<CraftingStation, CircleProjector>("m_areaMarkerCircle");
            UtilityItemInstances = AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_utilityItemInstances");
            PieceRayTest = Method<PieceRayTestFn>(typeof(Player), "PieceRayTest", typeof(Vector3).MakeByRefType(),
                typeof(Vector3).MakeByRefType(), typeof(Piece).MakeByRefType(), typeof(Heightmap).MakeByRefType(),
                typeof(Collider).MakeByRefType(), typeof(bool));
            TakeInput = Method<Func<Player, bool>>(typeof(Player), "TakeInput");
            UpdateCraftingPanel = Method<Action<InventoryGui, bool>>(typeof(InventoryGui), "UpdateCraftingPanel", typeof(bool));
            CraftRecipe = AccessTools.FieldRefAccess<InventoryGui, Recipe>("m_craftRecipe");
            CraftUpgradeItem = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_craftUpgradeItem");
            UpdateTotalWeight = Method<Action<Inventory>>(typeof(Inventory), "UpdateTotalWeight");
            Zanim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        }

        private static T Method<T>(Type type, string name, params Type[] parameters) where T : Delegate =>
            AccessTools.MethodDelegate<T>(AccessTools.Method(type, name, parameters)
                ?? throw new MissingMethodException(type.Name, name));
    }
}
