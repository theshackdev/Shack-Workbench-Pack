using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace TheShack.WorkbenchPack
{
    // A workbench you can carry. It is crafted at the workbench and upgraded there like gear: each upgrade costs what
    // the matching real upgrade costs to build (Chopping block, Tanning rack, Adze, Tool shelf), and the game's own
    // rule means you can only add an upgrade once your real bench has it.
    //
    //   - Worn in the utility slot it shows on your back, slows you down, and Tab opens the workbench menu at its
    //     level. The hammer works around you like near a real bench.
    //   - The hotbar key (or a quick slot, or the "Set down" key while worn) shows a ghost of a small, half-width
    //     workbench: left-click sets it down, scroll turns it, right-click cancels. It works like a workbench of its
    //     level. Shift+E packs it back up, keeping its level; removing it with the hammer, or it being destroyed,
    //     drops the backpack instead of wood.
    //   - It has durability, more at each level. Every craft, upgrade or repair done at it wears it a little; broken,
    //     it does nothing until another workbench of at least its level repairs it.
    //   - A real stonecutter can pack a stonecutter into it (the stonecutter's own build cost), for stone building
    //     around it. That makes it heavier.
    //
    // The item and the placed bench are new prefabs, so every player and the server need this mod. The settings are
    // admin-only and come from the server (Jotunn config sync), except the "0 - This PC" ones.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    [SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "theshack.workbenchpack";
        public const string PluginName = "Shack Workbench Pack";
        public const string PluginVersion = "1.2.0";

        internal static ManualLogSource Log;
        private bool ready;

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);

            // A mod that throws in Awake takes the chainloader down with it. If the game changed under us, say so and
            // add nothing.
            Harmony harmony = new Harmony(PluginGuid);
            try
            {
                Access.Init();
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Content.Init();
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Log.LogError($"{PluginName} could not hook into this game version; the backpack is not added. {e}");
                return;
            }

            ready = true;
            Log.LogInfo($"{PluginName} {PluginVersion} ready (weight {Settings.Weight.Value}, " +
                $"{Settings.SlowPercent.Value}% slower while carried).");
        }

        private void Update()
        {
            if (ready)
            {
                Placing.Update();
                PackingUp.Update();
            }
        }
    }

    internal static class Settings
    {
        public static ConfigEntry<KeyboardShortcut> SetDownKey;
        public static ConfigEntry<bool> WearAfterPackUp;

        public static ConfigEntry<float> Weight;
        public static ConfigEntry<int> SlowPercent;
        public static ConfigEntry<bool> NeedsRoof;
        public static ConfigEntry<bool> OwnerOnly;
        public static ConfigEntry<string> CraftCost;

        public static ConfigEntry<bool> BuildWhileWorn;
        public static ConfigEntry<float> BuildRadius;
        public static ConfigEntry<float> BuildRadiusPerLevel;

        public static ConfigEntry<float> Durability;
        public static ConfigEntry<float> DurabilityPerLevel;
        public static ConfigEntry<float> WearPerUse;
        public static ConfigEntry<bool> RepairNeedsLevel;

        public static ConfigEntry<float> CutterWeight;
        public static ConfigEntry<float> CutterRadius;

        public static ConfigEntry<int> SettingsVersion;

        public static ConfigEntry<float> ForgeWeight;
        public static ConfigEntry<int> ForgeSlowPercent;
        public static ConfigEntry<string> ForgeCraftCost;

        // Bumped on every settings change (including a sync from the server) so cached values refresh.
        public static int Version;

        public static void Bind(ConfigFile config)
        {
            int order = 100;
            ConfigEntry<T> Bind<T>(string section, string key, T value, string description, AcceptableValueBase range = null) =>
                config.Bind(section, key, value, new ConfigDescription(description, range,
                    new ConfigurationManagerAttributes { IsAdminOnly = true, Order = order-- }));
            ConfigEntry<T> Personal<T>(string section, string key, T value, string description, AcceptableValueBase range = null) =>
                config.Bind(section, key, value, new ConfigDescription(description, range,
                    new ConfigurationManagerAttributes { Order = order-- }));

            // Each player's own choice; not synced from the server.
            SetDownKey = Personal("0 - This PC", "Set down key", new KeyboardShortcut(KeyCode.U),
                "Sets down the backpack you're wearing (worn gear isn't in the hotbar). The hotbar key does it when the " +
                "backpack is in your hotbar. Not while Alt is held (Alt+U toggles the compass), unless the key includes Alt.");
            // 1.0.0 used G, which is also vanilla's radial menu key: pressing it did both.
            if (SetDownKey.Value.MainKey == KeyCode.G && !SetDownKey.Value.Modifiers.Any())
            {
                SetDownKey.Value = new KeyboardShortcut(KeyCode.U);
                Plugin.Log.LogInfo("Set down key moved from G (the game's radial menu key) to U.");
            }
            WearAfterPackUp = Personal("0 - This PC", "Wear after packing up", true,
                "Packing the bench up puts the backpack straight on your back when your utility slot is free.");

            Weight = Bind("1 - General", "Weight", 30f, "Weight of the backpack, worn or in the bag.",
                new AcceptableValueRange<float>(0f, 300f));
            SlowPercent = Bind("1 - General", "Slower while carried", 15,
                "How much slower you walk, run and swim while the backpack is anywhere in your inventory (worn or in the " +
                "bag), in percent. In a cart or a chest it doesn't slow you. Adds to the forge backpack's when you carry both.",
                new AcceptableValueRange<int>(0, 90));
            // Until 1.2.0 the defaults were 50 and 25%; the user lowered them for both packs (30 each, 15% each). Moved
            // once, so an admin who sets 50 or 25% afterwards keeps it.
            SettingsVersion = config.Bind("9 - Internal", "Settings version", 0, new ConfigDescription(
                "Which setting changes this file has had. Don't edit.", null, new ConfigurationManagerAttributes { Browsable = false }));
            if (SettingsVersion.Value < 2)
            {
                if (Weight.Value == 50f)
                {
                    Weight.Value = 30f;
                    Plugin.Log.LogInfo("Weight moved from the old default 50 to 30.");
                }
                if (SlowPercent.Value == 25)
                {
                    SlowPercent.Value = 15;
                    Plugin.Log.LogInfo("Slower while carried moved from the old default 25% to 15%.");
                }
                SettingsVersion.Value = 2;
            }
            NeedsRoof = Bind("1 - General", "Needs a roof", false,
                "Crafting at the backpack needs a roof over it, like the real workbench. Off: it works anywhere.");
            OwnerOnly = Bind("1 - General", "Only the owner can pack up", true,
                "Only the player who set the bench down (or an admin) can pack it up or remove it with the hammer.");
            CraftCost = Bind("1 - General", "Craft cost", "Wood:10, DeerHide:2",
                "What crafting the level 1 backpack costs at the workbench: item prefab names with amounts, comma " +
                "separated. Upgrades always cost what the matching real workbench upgrade costs to build.");

            BuildWhileWorn = Bind("2 - Building", "Build while worn", true,
                "The hammer can build around you while you wear the backpack, like near a real workbench. " +
                "The set-down bench always lets you build around it.");
            BuildRadius = Bind("2 - Building", "Build radius", 20f,
                "Build radius at level 1, in meters. The real workbench has 20.", new AcceptableValueRange<float>(0f, 100f));
            BuildRadiusPerLevel = Bind("2 - Building", "Build radius per level", 4f,
                "Extra build radius for each level above 1, in meters. The real workbench gets 4 per upgrade.",
                new AcceptableValueRange<float>(0f, 20f));

            Durability = Bind("3 - Durability", "Durability", 100f,
                "Durability of a level 1 backpack. Upgrading it gives a new, fully repaired one.",
                new AcceptableValueRange<float>(1f, 10000f));
            DurabilityPerLevel = Bind("3 - Durability", "Durability per level", 50f,
                "Extra durability for each level above 1.", new AcceptableValueRange<float>(0f, 5000f));
            WearPerUse = Bind("3 - Durability", "Wear per use", 1f,
                "Durability lost for each craft, upgrade or repair done at the backpack, worn or set down. 0: it never " +
                "wears. Broken, it does nothing (no menu, no building, no setting down) until it is repaired.",
                new AcceptableValueRange<float>(0f, 1000f));
            RepairNeedsLevel = Bind("3 - Durability", "Repair needs its level", true,
                "Repairing the backpack needs a workbench of at least its level (a real one, or another backpack's). " +
                "Off: any workbench, like vanilla gear. It can never repair itself.");

            CutterWeight = Bind("4 - Stonecutter", "Stonecutter weight", 10f,
                "Extra weight of a backpack with a stonecutter packed in.", new AcceptableValueRange<float>(0f, 300f));
            CutterRadius = Bind("4 - Stonecutter", "Stonecutter build radius", 20f,
                "How far from the backpack (worn or set down) stone pieces can be built, in meters. The real " +
                "stonecutter has 20. \"Build while worn\" covers it too.", new AcceptableValueRange<float>(0f, 100f));

            ForgeWeight = Bind("5 - Forge", "Forge weight", 30f, "Weight of the forge backpack in the bag.",
                new AcceptableValueRange<float>(0f, 300f));
            ForgeSlowPercent = Bind("5 - Forge", "Forge slower while carried", 15,
                "How much slower you move while a forge backpack is in your inventory, in percent. Adds to the workbench " +
                "backpack's when you carry both.", new AcceptableValueRange<int>(0, 90));
            ForgeCraftCost = Bind("5 - Forge", "Forge craft cost", "",
                "What crafting the level 1 forge backpack costs at a forge: item prefab names with amounts, comma " +
                "separated. Empty: what building a real forge costs. Upgrades always cost what the matching real forge " +
                "upgrade costs to build. Durability and repairs follow \"3 - Durability\".");

            config.SettingChanged += (sender, args) =>
            {
                Version++;
                Content.ApplySettings();
            };
        }
    }
}
