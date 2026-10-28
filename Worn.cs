using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace TheShack.WorkbenchPack
{
    // Wearing the backpack (utility slot): the slow, the bench on your back, and Tab opening the workbench menu.
    internal static class Worn
    {
        private static int frame = -1;
        private static ItemDrop.ItemData cached;

        // The backpack this player is wearing, or null. Equipment & Quick Slots can add utility slots that aren't the
        // game's own, so equipped items in the inventory count too.
        public static ItemDrop.ItemData Item(Player player)
        {
            if (!player)
            {
                return null;
            }
            if (player != Player.m_localPlayer)
            {
                return Find(player);
            }
            if (Time.frameCount != frame)
            {
                frame = Time.frameCount;
                cached = Find(player);
            }
            return cached;
        }

        private static ItemDrop.ItemData Find(Player player)
        {
            ItemDrop.ItemData utility = Access.UtilityItem(player);
            if (Content.IsOurs(utility))
            {
                return utility;
            }
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (item.m_equipped && Content.IsOurs(item))
                {
                    return item;
                }
            }
            return null;
        }

        private static int carriedFrame = -1;
        private static bool carried;
        private static bool carriedForge;

        // Whether the local player has a backpack (or a forge backpack) anywhere in their inventory, worn or not
        // (checked once a frame).
        public static bool Carrying(Player player)
        {
            Check(player);
            return carried;
        }

        public static bool CarryingForge(Player player)
        {
            Check(player);
            return carriedForge;
        }

        private static void Check(Player player)
        {
            if (!player)
            {
                carried = carriedForge = false;
                return;
            }
            if (Time.frameCount != carriedFrame)
            {
                carriedFrame = Time.frameCount;
                carried = carriedForge = false;
                foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
                {
                    carried |= Content.IsOurs(item);
                    carriedForge |= Content.IsForge(item);
                }
            }
        }

        // Walking, running, crouching and swimming all multiply by this factor, so the slow is the same everywhere.
        // It applies while a pack is anywhere in your inventory, worn or in the bag. Each kind counts once, and the
        // two add up (15% + 15% = 30% by default).
        [HarmonyPatch(typeof(Humanoid), "GetAttackSpeedFactorMovement")]
        private static class Humanoid_GetAttackSpeedFactorMovement
        {
            private static void Postfix(Humanoid __instance, ref float __result)
            {
                Player player = Player.m_localPlayer;
                if (__instance != player || !player)
                {
                    return;
                }
                int slow = (Carrying(player) ? Settings.SlowPercent.Value : 0) + (CarryingForge(player) ? Settings.ForgeSlowPercent.Value : 0);
                if (slow > 0)
                {
                    __result *= Mathf.Max(0.05f, 1f - slow / 100f);
                }
            }
        }

        // Every client shows the utility items of every player (vanilla's slot, and Equipment & Quick Slots' extra
        // ones, which call AttachArmor directly). Each copy of the backpack is sized, and on a player it gets a bench.
        private static readonly int ItemHash = Content.ItemName.GetStableHashCode();

        [HarmonyPatch(typeof(VisEquipment), "AttachArmor")]
        private static class VisEquipment_AttachArmor
        {
            private static void Postfix(int itemHash, List<GameObject> __result)
            {
                if (itemHash != ItemHash || __result == null)
                {
                    return;
                }
                foreach (GameObject instance in __result)
                {
                    if (instance && instance.name.StartsWith("attach_"))
                    {
                        BackBench.Fit(instance.transform.Find("bench"));
                        BackBench.Attach(instance);
                    }
                }
            }
        }

        // Crafting reads the station's ZNetView to see if it was built with cheats; the bench on your back has none
        // (it's part of you, not a world object), so that one read is swapped for a null-safe one. If the game changed
        // and the read isn't found, Tab crafting stays off rather than risking the crafted item.
        public static bool TabCrafting;

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        private static class InventoryGui_DoCrafting
        {
            // Valheim 1.0.20 made the cheated flag a number read by the internal CheatUtils.IsCheated(zdo); before
            // that it was GetBool(ZDOVars.s_cheated). Null on games without CheatUtils.
            private static Func<ZDO, bool> isCheated;

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> code = instructions.ToList();
                MethodInfo getZdo = AccessTools.Method(typeof(ZNetView), nameof(ZNetView.GetZDO));
                MethodInfo getBool = AccessTools.Method(typeof(ZDO), nameof(ZDO.GetBool), new[] { typeof(int), typeof(bool) });
                Type cheatUtils = typeof(ZDO).Assembly.GetType("CheatUtils");
                MethodInfo cheatCheck = cheatUtils == null ? null : AccessTools.Method(cheatUtils, "IsCheated", new[] { typeof(ZDO) });
                MethodInfo safe = AccessTools.Method(typeof(InventoryGui_DoCrafting), nameof(StationCheated));
                isCheated = cheatCheck == null ? null : AccessTools.MethodDelegate<Func<ZDO, bool>>(cheatCheck);
                TabCrafting = false;
                for (int i = 0; i + 2 < code.Count; i++)
                {
                    if (!(code[i].operand is MethodInfo m && m.Name == "GetComponent" && m.IsGenericMethod &&
                          m.GetGenericArguments()[0] == typeof(ZNetView) && code[i + 1].Calls(getZdo)))
                    {
                        continue;
                    }
                    // GetComponent<ZNetView>, GetZDO, then IsCheated (1.0.20) or ldsfld, ldc, GetBool (1.0.17).
                    int length = cheatCheck != null && code[i + 2].Calls(cheatCheck) ? 3
                        : i + 4 < code.Count && code[i + 2].opcode == OpCodes.Ldsfld && code[i + 4].Calls(getBool) ? 5 : 0;
                    if (length == 0)
                    {
                        continue;
                    }
                    code[i] = new CodeInstruction(OpCodes.Call, safe).MoveLabelsFrom(code[i]);
                    for (int j = 1; j < length; j++)
                    {
                        code[i + j] = new CodeInstruction(OpCodes.Nop).MoveLabelsFrom(code[i + j]);
                    }
                    TabCrafting = true;
                    break;
                }
                if (!TabCrafting)
                {
                    Plugin.Log.LogWarning("This game version crafts differently; Tab crafting from the worn backpack is off " +
                        "(set the bench down to use it).");
                }
                return code;
            }

            private static bool StationCheated(Component station)
            {
                ZNetView view = station ? station.GetComponent<ZNetView>() : null;
                if (!view || !view.IsValid())
                {
                    return false;
                }
                return isCheated != null ? isCheated(view.GetZDO()) : view.GetZDO().GetBool(ZDOVars.s_cheated);
            }
        }

        // Tab with the backpack on: the inventory opens at the bench on your back. Pressing E at a real bench sets
        // that bench before the inventory opens, so it always wins.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static class InventoryGui_Show
        {
            private static void Prefix(Container container)
            {
                Player player = Player.m_localPlayer;
                if (!TabCrafting || container != null || !player || player.GetCurrentCraftingStation() != null || !BackBench.Local ||
                    !BackBench.Local.Station || Item(player) == null)
                {
                    return;
                }
                // Set directly, not with SetCraftingStation: that also puts your weapons away.
                Access.CurrentStation(player) = BackBench.Local.Station;
                player.AddKnownStation(BackBench.Local.Station);
            }
        }

        // Vanilla turns you to face the bench you work at and closes the menu if you walk away; the bench on your back
        // only lasts while the inventory is open.
        [HarmonyPatch(typeof(Player), "UpdateStations")]
        private static class Player_UpdateStations
        {
            private static bool Prefix(Player __instance)
            {
                CraftingStation station = Access.CurrentStation(__instance);
                if (!station || !Stations.TryGet(station, out IBench bench) || !bench.Worn)
                {
                    return true;
                }
                if (!InventoryGui.IsVisible() || !BackBench.Local || BackBench.Local.Station != station || Item(__instance) == null)
                {
                    Access.CurrentStation(__instance) = null;
                }
                return false;
            }
        }
    }

    // On the copy of the backpack shown on a player's back (every client): a workbench that goes where they go.
    internal class BackBench : MonoBehaviour, IBench
    {
        public static BackBench Local;

        public CraftingStation Station;
        private CraftingStation cutter;
        private Player player;
        private ZNetView playerView;
        private int sentLevel = -1;
        private int sentCutter = -1;
        private int sentBroken = -1;

        public bool Worn => true;

        private bool IsLocal => player && player == Player.m_localPlayer;

        // The wearer's own client knows the item; everyone else reads what the wearer put on their player ZDO.
        public bool Broken => IsLocal ? Content.Broken(Worn_Item()) : RemoteFlag(Content.BrokenKey);

        public bool HasCutter => IsLocal ? Content.HasCutter(Worn_Item()) : RemoteFlag(Content.CutterKey);

        public bool IsFor(ItemDrop.ItemData item) => IsLocal && item != null && Worn_Item() == item;

        private bool RemoteFlag(string key)
        {
            ZDO zdo = playerView && playerView.IsValid() ? playerView.GetZDO() : null;
            return zdo != null && zdo.GetBool(key);
        }

        // Crafting is local, so only the wearer's own client wears it down. At 0 the game takes it off (a broken
        // utility item can't stay worn) and it can't be worn again until it's repaired.
        public void Wear(float amount)
        {
            ItemDrop.ItemData item = IsLocal ? Worn_Item() : null;
            if (item != null && amount > 0f)
            {
                item.m_durability = Mathf.Max(0f, item.m_durability - amount);
            }
        }

        public int Level
        {
            get
            {
                if (player && player == Player.m_localPlayer)
                {
                    return Mathf.Clamp(Worn_Item()?.m_quality ?? 1, 1, Content.MaxLevel);
                }
                ZDO zdo = playerView && playerView.IsValid() ? playerView.GetZDO() : null;
                return zdo != null ? Mathf.Clamp(zdo.GetInt(Content.LevelKey, 1), 1, Content.MaxLevel) : 1;
            }
        }

        private ItemDrop.ItemData Worn_Item() => WorkbenchPack.Worn.Item(player);

        public static void Attach(GameObject instance)
        {
            Player player = instance.GetComponentInParent<Player>();
            if (!player || instance.GetComponent<BackBench>() || !Content.Workbench)
            {
                return;
            }
            BackBench bench = instance.AddComponent<BackBench>();
            bench.player = player;
            bench.playerView = player.GetComponent<ZNetView>();

            CraftingStation wb = Content.Workbench;
            CraftingStation station = instance.AddComponent<CraftingStation>();
            station.m_name = wb.m_name;
            station.m_icon = wb.m_icon;
            station.m_discoverRange = wb.m_discoverRange;
            station.m_rangeBuild = Settings.BuildRadius.Value;
            station.m_craftRequireRoof = Settings.NeedsRoof.Value;
            station.m_craftRequireFire = false;
            station.m_roofCheckPoint = instance.transform;
            station.m_connectionPoint = instance.transform;
            station.m_showBasicRecipies = wb.m_showBasicRecipies;
            station.m_useDistance = 5f;
            station.m_useAnimation = 0;
            station.m_craftingSkill = wb.m_craftingSkill;
            station.m_hasCraftTab = true;
            station.m_canRepair = wb.m_canRepair;
            station.m_upgrader = false;
            station.m_craftItemEffects = wb.m_craftItemEffects;
            station.m_craftItemDoneEffects = wb.m_craftItemDoneEffects;
            station.m_repairItemDoneEffects = wb.m_repairItemDoneEffects;
            station.m_craftItemDoneFailEffects = wb.m_craftItemDoneFailEffects;
            bench.Station = station;
            Stations.Register(station, bench);
            if (player == Player.m_localPlayer)
            {
                Local = bench;
            }
        }

        private void Update()
        {
            if (IsLocal)
            {
                Local = this;
                // Tell other players the level, the stonecutter and whether it's broken, for the build radius they
                // see around you.
                if (playerView && playerView.IsValid() && playerView.IsOwner())
                {
                    ZDO zdo = playerView.GetZDO();
                    int level = Level;
                    if (level != sentLevel)
                    {
                        zdo.Set(Content.LevelKey, level);
                        sentLevel = level;
                    }
                    int hasCutter = HasCutter ? 1 : 0;
                    if (hasCutter != sentCutter)
                    {
                        zdo.Set(Content.CutterKey, hasCutter == 1);
                        sentCutter = hasCutter;
                    }
                    int broken = Broken ? 1 : 0;
                    if (broken != sentBroken)
                    {
                        zdo.Set(Content.BrokenKey, broken == 1);
                        sentBroken = broken;
                    }
                }
            }
            // The stonecutter station comes and goes with the stonecutter (packed in while worn, or seen on another
            // player once their ZDO says so).
            bool wantCutter = HasCutter && Content.Stonecutter;
            if (wantCutter && !cutter)
            {
                cutter = Stonecutter.AddStation(gameObject, this);
            }
            else if (!wantCutter && cutter)
            {
                Stations.Unregister(cutter);
                Destroy(cutter);
                cutter = null;
            }
        }

        // Every copy on a back (also a ragdoll's or an armor stand's) is sized here; the bones are scaled.
        public static void Fit(Transform model)
        {
            if (model)
            {
                Content.FitModel(model, Content.BackWidth, Content.BackPosition, Quaternion.identity);
            }
        }

        private void OnDestroy()
        {
            Stations.Unregister(Station);
            Stations.Unregister(cutter);
            // Taken off: other players shouldn't keep seeing this one's stonecutter or broken state.
            if (IsLocal && playerView && playerView.IsValid() && playerView.IsOwner())
            {
                playerView.GetZDO().Set(Content.CutterKey, false);
                playerView.GetZDO().Set(Content.BrokenKey, false);
            }
            if (Local == this)
            {
                Local = null;
            }
            // Taken off while working at it: drop it as the current station, so the menu doesn't keep a dead bench.
            Player local = Player.m_localPlayer;
            if (local && (object)Access.CurrentStation(local) == Station)
            {
                Access.CurrentStation(local) = null;
                if (InventoryGui.instance && InventoryGui.IsVisible())
                {
                    Access.UpdateCraftingPanel(InventoryGui.instance, false);
                }
            }
        }
    }
}
