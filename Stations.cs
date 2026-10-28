using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheShack.WorkbenchPack
{
    // A backpack bench: the set-down one (PlacedBench) or the one on a wearer's back (BackBench).
    internal interface IBench
    {
        int Level { get; }
        bool Worn { get; }
        bool Broken { get; }
        bool HasCutter { get; }

        // A craft, upgrade or repair was done at it.
        void Wear(float amount);
    }

    // What a station of ours is: a backpack's workbench, the stonecutter packed into one, or a set-down forge backpack.
    internal enum Role
    {
        Workbench,
        Cutter,
        Forge
    }

    // Our benches are workbenches (same station name, so workbench recipes and the hammer accept them), but their
    // level is the backpack's level and their build radius comes from the settings. Real workbench upgrades standing
    // nearby don't add to them. A backpack with a stonecutter also has a second station, a stonecutter, which only
    // gives a build radius for stone pieces (it can't be opened). A set-down forge backpack is a forge in the same way:
    // its level is the pack's, and real forge upgrades nearby don't add to it.
    internal static class Stations
    {
        private struct Entry
        {
            public IBench Bench;
            public Role Role;
        }

        private static readonly Dictionary<CraftingStation, Entry> benches = new Dictionary<CraftingStation, Entry>();

        public static void Register(CraftingStation station, IBench bench, Role role = Role.Workbench) =>
            benches[station] = new Entry { Bench = bench, Role = role };

        public static void Unregister(CraftingStation station)
        {
            if (station != null)
            {
                benches.Remove(station);
            }
        }

        // One of our workbenches or forges (not a backpack's stonecutter).
        public static bool TryGet(CraftingStation station, out IBench bench)
        {
            bench = null;
            if (station == null || !benches.TryGetValue(station, out Entry entry) || entry.Role == Role.Cutter)
            {
                return false;
            }
            bench = entry.Bench;
            return true;
        }

        private static bool TryGetAny(CraftingStation station, out Entry entry)
        {
            entry = default;
            return station != null && benches.TryGetValue(station, out entry);
        }

        public static float Radius(int level) =>
            Settings.BuildRadius.Value + (Mathf.Clamp(level, 1, Content.MaxLevel) - 1) * Settings.BuildRadiusPerLevel.Value;

        // A set-down forge builds forge pieces as far as a real forge of its level does.
        private static float ForgeRadius(int level) => Content.Forge
            ? Content.Forge.m_rangeBuild + (Mathf.Clamp(level, 1, Content.ForgeMaxLevel) - 1) * Content.Forge.m_extraRangePerLevel
            : 20f;

        // The station on the local player that crafts or repairs right now, if it's one of ours.
        public static IBench Current(Player player) =>
            player && TryGet(player.GetCurrentCraftingStation(), out IBench bench) ? bench : null;

        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetLevel))]
        private static class CraftingStation_GetLevel
        {
            private static bool Prefix(CraftingStation __instance, ref int __result)
            {
                if (!TryGetAny(__instance, out Entry entry))
                {
                    return true;
                }
                __result = entry.Role == Role.Cutter ? 1 : entry.Bench.Level;
                return false;
            }
        }

        // Vanilla looks for upgrades nearby here and sizes the build radius, its marker and the base area from them.
        [HarmonyPatch(typeof(CraftingStation), "GetExtensions")]
        private static class CraftingStation_GetExtensions
        {
            private static bool Prefix(CraftingStation __instance, ref List<StationExtension> __result)
            {
                if (!TryGetAny(__instance, out Entry entry))
                {
                    return true;
                }
                IBench bench = entry.Bench;
                List<StationExtension> none = Access.AttachedExtensions(__instance);
                none.Clear();
                float range = bench.Broken || (bench.Worn && !Settings.BuildWhileWorn.Value) ? 0f
                    : entry.Role == Role.Cutter ? (bench.HasCutter ? Settings.CutterRadius.Value : 0f)
                    : entry.Role == Role.Forge ? ForgeRadius(bench.Level)
                    : Radius(bench.Level);
                Access.BuildRange(__instance) = range;
                CircleProjector marker = Access.AreaMarkerCircle(__instance);
                if (marker)
                {
                    marker.m_radius = range;
                }
                switch (__instance.m_effectAreaCollider)
                {
                    case SphereCollider sphere:
                        sphere.radius = range;
                        break;
                    case CapsuleCollider capsule:
                        capsule.radius = range;
                        break;
                }
                __result = none;
                return false;
            }
        }

        // The roof setting, and a broken bench does nothing.
        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.CheckUsable))]
        private static class CraftingStation_CheckUsable
        {
            private static bool Prefix(CraftingStation __instance, Player player, bool showMessage, ref bool __result)
            {
                if (!TryGetAny(__instance, out Entry entry))
                {
                    return true;
                }
                __instance.m_craftRequireRoof = Settings.NeedsRoof.Value;
                if (!entry.Bench.Broken)
                {
                    return true;
                }
                if (showMessage && player)
                {
                    player.Message(MessageHud.MessageType.Center, entry.Bench.Worn ? "$shack_benchpack_broken"
                        : entry.Role == Role.Forge ? "$shack_forgepack_brokenplaced" : "$shack_benchpack_brokenplaced");
                }
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetHoverName))]
        private static class CraftingStation_GetHoverName
        {
            private static bool Prefix(CraftingStation __instance, ref string __result)
            {
                if (!TryGet(__instance, out IBench bench) || bench.Worn || !(bench is PlacedBench placed))
                {
                    return true;
                }
                __result = Localization.instance.Localize(placed.Token);
                return false;
            }
        }

        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetHoverText))]
        private static class CraftingStation_GetHoverText
        {
            private static bool Prefix(CraftingStation __instance, ref string __result)
            {
                if (!TryGet(__instance, out IBench bench) || bench.Worn || !(bench is PlacedBench placed) ||
                    !Player.m_localPlayer || !__instance.InUseDistance(Player.m_localPlayer))
                {
                    return true;
                }
                string cutter = bench.HasCutter && Content.StonecutterPiece ? ", " + Content.StonecutterPiece.m_name : "";
                string state = bench.Broken
                    ? "<color=red>$shack_benchpack_brokenword</color>"
                    : $"{placed.Durability:0}/{Content.MaxDurability(bench.Level, placed.IsForge):0}";
                string level = placed.IsForge ? "$shack_forgepack_level" : "$shack_benchpack_level";
                __result = Localization.instance.Localize(
                    $"{placed.Token} ({level} {bench.Level}{cutter})" +
                    $"\n$item_durability: {state}" +
                    "\n[<color=yellow><b>$KEY_Use</b></color>] $piece_use" +
                    "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $shack_benchpack_packup");
                return false;
            }
        }

        // Shift+E packs the bench up.
        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]
        private static class CraftingStation_Interact
        {
            private static bool Prefix(CraftingStation __instance, Humanoid user, bool repeat, bool alt, ref bool __result)
            {
                if (!alt || !TryGet(__instance, out IBench bench) || !(bench is PlacedBench placed))
                {
                    return true;
                }
                if (!repeat && user == Player.m_localPlayer)
                {
                    PackingUp.Start(Player.m_localPlayer, placed);
                }
                __result = false;
                return false;
            }
        }
    }
}
