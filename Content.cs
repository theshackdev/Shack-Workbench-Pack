using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TheShack.WorkbenchPack
{
    // The backpack item and the set-down bench, both made from vanilla prefabs when the game has loaded them.
    internal static class Content
    {
        public const string ItemName = "ShackWorkbenchBackpack";
        public const string PieceName = "piece_shack_workbench_backpack";
        public const string Token = "$shack_benchpack";

        // The stonecutter add-on: a recipe at a real stonecutter. Its item is only there for the recipe list; crafting
        // it packs a stonecutter into a backpack instead (Stonecutter.cs).
        public const string CutterItemName = "ShackBenchStonecutter";
        public const string CutterToken = "$shack_benchpack_cutter";
        public const string StonecutterPrefab = "piece_stonecutter";
        public const int MaxLevel = 5;

        // The forge backpack (1.2.0): a second pack, carried in the bag. It goes down beside a workbench backpack when
        // that is set down and packs up with it; set down, it is a forge of its level. Upgraded at a real forge.
        public const string ForgeItemName = "ShackForgeBackpack";
        public const string ForgePieceName = "piece_shack_forge_backpack";
        public const string ForgeToken = "$shack_forgepack";
        public const string ForgePrefab = "forge";
        public const int ForgeMaxLevel = 7;

        // Each forge upgrade, in the order the levels add them (level 2 = forge_ext1 ... level 7 = forge_ext6).
        public static readonly string[] ForgeUpgrades =
            { "forge_ext1", "forge_ext2", "forge_ext3", "forge_ext4", "forge_ext5", "forge_ext6" };

        // Each workbench upgrade, in the order the levels add them (level 2 = Chopping block ... level 5 = Tool shelf).
        public static readonly string[] Upgrades =
            { "piece_workbench_ext1", "piece_workbench_ext2", "piece_workbench_ext3", "piece_workbench_ext4" };

        // ZDO keys: the level on a set-down bench and on the player wearing one, and who crafted and placed it.
        public const string LevelKey = "theshack.bench.level";
        public const string CrafterIdKey = "theshack.bench.crafterid";
        public const string CrafterNameKey = "theshack.bench.craftername";
        public const string PlacerNameKey = "theshack.bench.placer";
        // On a set-down bench: its durability (missing = full, from before 1.1.0) and whether it has a stonecutter.
        // On a wearer: whether theirs has a stonecutter and whether it's broken, for what other players see.
        public const string DurabilityKey = "theshack.bench.durability";
        public const string CutterKey = "theshack.bench.stonecutter";
        public const string BrokenKey = "theshack.bench.broken";
        // On a set-down bench and the forge set down with it: the same random number, so packing up either packs both.
        // (Not a ZDOID: the game gives every ZDO a new one when the world loads.)
        public const string PairKey = "theshack.bench.pair";

        // In the backpack item's custom data: a stonecutter is packed in ("1"), and the item has been seen by 1.1.0
        // (older backpacks get full durability once).
        public const string CutterData = "theshack.bench.stonecutter";
        public const string SeenData = "theshack.bench.v";

        // The set-down bench is the workbench at half its width and three quarters of its height and depth.
        public static readonly Vector3 PlacedScale = new Vector3(0.5f, 0.75f, 0.75f);
        private const float GroundWidth = 0.6f;

        // On the wearer's back: width in meters, and where it sits from the upper spine bone (x side, y up, z forward).
        public const float BackWidth = 0.7f;
        public static readonly Vector3 BackPosition = new Vector3(0f, 0.15f, -0.25f);

        // The stonecutter beside a set-down bench: its width in meters and the gap to the bench.
        private const float CutterWidth = 0.75f;
        private const float CutterGap = 0.1f;

        // The set-down forge is the forge at this size, to the left of the bench with this gap.
        public static readonly Vector3 ForgeScale = new Vector3(0.6f, 0.6f, 0.6f);
        private const float ForgeGap = 0.15f;

        public static GameObject ItemPrefab;
        public static GameObject PiecePrefab;
        public static Piece BenchPiece;
        public static CraftingStation Workbench;
        public static CraftingStation Stonecutter;
        public static Piece StonecutterPiece;
        public static string BackBone = "Spine2";

        // A copy of the workbench's meshes only (no colliders or scripts), full size. ModelBounds is its size once
        // shrunk to PlacedScale.
        public static GameObject BenchModel;
        public static Bounds ModelBounds;
        public static GameObject CutterModel;
        private static Bounds cutterBounds;
        private static GameObject holder;

        public static GameObject ForgeItemPrefab;
        public static GameObject ForgePiecePrefab;
        public static Piece ForgePiece;
        public static CraftingStation Forge;
        public static Piece ForgeBuildPiece;
        public static GameObject ForgeModel;
        private static Bounds forgeBounds;
        // Where the set-down forge stands, from the bench's own position and turn.
        public static Vector3 ForgeOffset;

        // The set-down forge's size, from its own position (for the checks before setting it down).
        public static Bounds ForgeBoundsPlaced => new Bounds(Vector3.Scale(forgeBounds.center, ForgeScale),
            Vector3.Scale(forgeBounds.size, ForgeScale));

        public static bool Ready => ItemPrefab != null && PiecePrefab != null;

        public static bool ForgeReady => ForgeItemPrefab != null && ForgePiecePrefab != null;

        public static void Init()
        {
            AddWords();
            PrefabManager.OnVanillaPrefabsAvailable += Build;
            ItemManager.OnItemsRegistered += Recipes.Rebuild;
        }

        public static bool IsOurs(ItemDrop.ItemData item) => item?.m_shared != null && item.m_shared.m_name == Token;

        public static bool IsOurs(ItemDrop.ItemData.SharedData shared) => shared != null && shared.m_name == Token;

        public static bool IsForge(ItemDrop.ItemData item) => item?.m_shared != null && item.m_shared.m_name == ForgeToken;

        public static int MaxLevelOf(bool forge) => forge ? ForgeMaxLevel : MaxLevel;

        public static bool HasCutter(ItemDrop.ItemData item) =>
            item?.m_customData != null && item.m_customData.TryGetValue(CutterData, out string value) && value == "1";

        public static void SetCutter(ItemDrop.ItemData item, bool cutter)
        {
            if (cutter)
            {
                item.m_customData[CutterData] = "1";
            }
            else
            {
                item.m_customData.Remove(CutterData);
            }
        }

        // Both packs follow the same durability settings; the forge goes up to level 7.
        public static float MaxDurability(int level, bool forge = false) =>
            Settings.Durability.Value + (Mathf.Clamp(level, 1, MaxLevelOf(forge)) - 1) * Settings.DurabilityPerLevel.Value;

        public static bool Broken(ItemDrop.ItemData item) => item != null && item.m_durability <= 0f;

        public static float Weight(ItemDrop.ItemData item) => IsForge(item) ? Settings.ForgeWeight.Value
            : Settings.Weight.Value + (HasCutter(item) ? Settings.CutterWeight.Value : 0f);

        // The backpack's shared data follows the settings. Every backpack on the ground has its own copy of it, so
        // this also runs where the game reads those values (Upgrades.cs).
        public static void ApplyShared(ItemDrop.ItemData.SharedData shared, bool forge = false)
        {
            shared.m_maxQuality = MaxLevelOf(forge);
            shared.m_useDurability = true;
            shared.m_maxDurability = Settings.Durability.Value;
            shared.m_durabilityPerLevel = Settings.DurabilityPerLevel.Value;
            shared.m_durabilityDrain = 0f;
            shared.m_canBeReparied = true;
            shared.m_destroyBroken = false;
        }

        // A setting changed (also when the server's settings arrive): the weight and the recipe follow at once.
        public static void ApplySettings()
        {
            if (ItemPrefab)
            {
                ItemDrop.ItemData.SharedData shared = ItemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared;
                shared.m_weight = Settings.Weight.Value;
                ApplyShared(shared);
            }
            if (ForgeItemPrefab)
            {
                ItemDrop.ItemData.SharedData shared = ForgeItemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared;
                shared.m_weight = Settings.ForgeWeight.Value;
                ApplyShared(shared, forge: true);
            }
            if (ObjectDB.instance)
            {
                Recipes.Rebuild();
            }
        }

        private static void AddWords()
        {
            LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
            {
                { "shack_benchpack", "Workbench backpack" },
                { "shack_benchpack_desc", "A workbench you can carry. Wear it and press Tab to work from your back, or " +
                    "set it down from the hotbar. Upgrade it at a workbench with what the workbench's own upgrades cost." },
                { "shack_benchpack_level", "Bench level" },
                { "shack_benchpack_packed", "Packed upgrades" },
                { "shack_benchpack_nothing", "none yet" },
                { "shack_benchpack_slow", "Movement while carried" },
                { "shack_benchpack_radius", "Build radius" },
                { "shack_benchpack_packup", "Pack up" },
                { "shack_benchpack_packing", "Packing up" },
                { "shack_benchpack_notyours", "Only {0} can pack this up" },
                { "shack_benchpack_full", "No room in your inventory: the backpack is at your feet" },
                { "shack_benchpack_placing", "Left-click: set down   Scroll: turn   Right-click: cancel" },
                { "shack_benchpack_badspot", "Can't set it down here" },
                { "shack_benchpack_steep", "Too steep to set it down" },
                { "shack_benchpack_broken", "The Workbench backpack is broken: repair it at a workbench" },
                { "shack_benchpack_brokenplaced", "This bench is broken: pack it up and repair it at a workbench" },
                { "shack_benchpack_brokenword", "broken" },
                { "shack_benchpack_repair", "Repairs at bench level" },
                { "shack_benchpack_orhigher", "or higher, not at itself" },
                { "shack_benchpack_cutter", "Backpack stonecutter" },
                { "shack_benchpack_cutter_desc", "Packs a stonecutter into a Workbench backpack in your inventory, so " +
                    "stone pieces can be built around it, worn or set down. It makes the backpack heavier." },
                { "shack_benchpack_cutteradded", "Stonecutter packed into the Workbench backpack" },
                { "shack_benchpack_cutterneed", "You need a Workbench backpack without a stonecutter in your inventory" },
                { "shack_forgepack", "Forge backpack" },
                { "shack_forgepack_desc", "A forge you can carry. It goes down beside your Workbench backpack when you set " +
                    "that down, and packs up with it. Upgrade it at a forge with what the forge's own upgrades cost." },
                { "shack_forgepack_level", "Forge level" },
                { "shack_forgepack_brokenplaced", "This forge is broken: pack it up and repair it at a forge" },
                { "shack_forgepack_brokenleft", "The Forge backpack is broken, so it stays in your bag: repair it at a forge" },
                { "shack_forgepack_needbench", "The Forge backpack goes down with a Workbench backpack: set that down" },
                { "shack_forgepack_noroom", "No room for the forge beside the bench" },
                { "shack_forgepack_repair", "Repairs at forge level" }
            });
        }

        private static void Build()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Build;
            try
            {
                GameObject workbench = PrefabManager.Instance.GetPrefab("piece_workbench");
                Workbench = workbench.GetComponent<CraftingStation>();
                holder = new GameObject("ShackWorkbenchBackpack_models");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);

                FindBackBone();
                BuildModel(workbench);
                BuildPiece(workbench);
                BuildItem(workbench.GetComponent<Piece>());
                Plugin.Log.LogInfo($"Workbench backpack added (back bone {BackBone}, model " +
                    $"{ModelBounds.size.x:0.00} x {ModelBounds.size.y:0.00} x {ModelBounds.size.z:0.00} m set down).");
                BuildCutter();
                BuildForge();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not make the workbench backpack: " + e);
            }
        }

        // The utility slot shows "attach_<bone>" children of the item on that bone of the wearer. Use the upper spine.
        private static void FindBackBone()
        {
            GameObject player = PrefabManager.Instance.GetPrefab("Player");
            Transform[] bones = player ? player.GetComponentsInChildren<Transform>(true) : new Transform[0];
            foreach (string name in new[] { "Spine2", "Spine1", "Spine" })
            {
                if (bones.Any(b => b.name == name))
                {
                    BackBone = name;
                    return;
                }
            }
            Plugin.Log.LogWarning("No spine bone found on the player; the backpack won't show on players' backs.");
        }

        private static void BuildModel(GameObject workbench)
        {
            BenchModel = CopyMeshes(workbench, "BenchModel");
            // This one is worn and dropped, so it moves.
            foreach (MeshRenderer renderer in BenchModel.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterials = MovingMaterials(renderer.sharedMaterials);
            }
            Bounds full = MeshBounds(BenchModel.transform);
            ModelBounds = new Bounds(Vector3.Scale(full.center, PlacedScale), Vector3.Scale(full.size, PlacedScale));
        }

        // Copy the meshes a piece shows when it is new (its first LOD), flattened under one object.
        private static GameObject CopyMeshes(GameObject prefab, string name)
        {
            GameObject model = new GameObject(name);
            model.transform.SetParent(holder.transform, false);
            Transform root = prefab.transform;
            // Several LOD groups (the model, its worn and broken looks, snow covers); keep only the closest level of
            // each, so high and low detail meshes aren't drawn on top of each other.
            HashSet<Renderer> lod0 = new HashSet<Renderer>();
            HashSet<Renderer> inAnyLod = new HashSet<Renderer>();
            foreach (LODGroup lod in prefab.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = lod.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                {
                    foreach (Renderer r in lods[i].renderers)
                    {
                        if (r)
                        {
                            inAnyLod.Add(r);
                            if (i == 0)
                            {
                                lod0.Add(r);
                            }
                        }
                    }
                }
            }
            HashSet<Transform> hidden = HiddenLooks(prefab);

            int kept = 0, skipped = 0;
            HashSet<string> shaders = new HashSet<string>();
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!ShownWhenNew(renderer.transform, root, hidden) || (inAnyLod.Contains(renderer) && !lod0.Contains(renderer)))
                {
                    skipped++;
                    continue;
                }
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh)
                {
                    continue;
                }
                Matrix4x4 m = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                GameObject part = new GameObject(renderer.name);
                part.transform.SetParent(model.transform, false);
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                kept++;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material && material.shader)
                    {
                        shaders.Add(material.shader.name);
                    }
                }
            }
            Plugin.Log.LogInfo($"{name}: {kept} meshes from {prefab.name} ({string.Join(", ", shaders)}), " +
                $"{skipped} left out (other levels of detail, worn, broken, snow, wet).");
            return model;
        }

        // The piece shader takes part of its look from world position (a value noise, triplanar textures, rain
        // ripples). On something that moves, that slides across it. The game makes copies for its placement ghosts
        // with those values set to follow the object instead; these are the same copies, made once per material.
        private static readonly Dictionary<Material, Material> movingMaterials = new Dictionary<Material, Material>();
        private static readonly int ValueNoise = Shader.PropertyToID("_ValueNoise");
        private static readonly int TriplanarLocalPos = Shader.PropertyToID("_TriplanarLocalPos");
        private static readonly int RippleDistance = Shader.PropertyToID("_RippleDistance");

        public static Material[] MovingMaterials(Material[] materials)
        {
            Material[] result = new Material[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                result[i] = Moving(materials[i]);
            }
            return result;
        }

        private static Material Moving(Material source)
        {
            if (!source)
            {
                return source;
            }
            if (movingMaterials.TryGetValue(source, out Material copy) && copy)
            {
                return copy;
            }
            if (movingMaterials.ContainsValue(source))
            {
                return source;
            }
            copy = new Material(source) { name = source.name + " (moving)" };
            string before = "";
            foreach (KeyValuePair<int, float> value in new Dictionary<int, float>
                { { ValueNoise, 0f }, { TriplanarLocalPos, 1f }, { RippleDistance, 0f } })
            {
                // Set even if the shader doesn't list it, as the game does for its ghosts.
                before += copy.HasProperty(value.Key) ? $" {copy.GetFloat(value.Key):0.##}" : " -";
                copy.SetFloat(value.Key, value.Value);
            }
            movingMaterials[source] = copy;
            Plugin.Log.LogInfo($"Moving copy of material {source.name} ({source.shader?.name}): noise, triplanar, ripple were{before}.");
            return copy;
        }

        // A piece keeps its worn and broken looks, its snow covers and its wet look as separate meshes, and the game
        // shows only the one that fits. A copy must leave them out, or they are all drawn on top of each other.
        public static HashSet<Transform> HiddenLooks(GameObject piece)
        {
            HashSet<Transform> hidden = new HashSet<Transform>();
            WearNTear wear = piece.GetComponent<WearNTear>();
            if (!wear)
            {
                return hidden;
            }
            foreach (GameObject look in new[] { wear.m_worn, wear.m_broken, wear.m_wet })
            {
                if (look && look != wear.m_new)
                {
                    hidden.Add(look.transform);
                }
            }
            foreach (MeshRenderer snow in new[] { wear.m_snow, wear.m_snowWorn, wear.m_snowBroken })
            {
                if (snow)
                {
                    hidden.Add(snow.transform);
                }
            }
            return hidden;
        }

        private static bool ShownWhenNew(Transform t, Transform root, HashSet<Transform> hidden)
        {
            for (; t != null && t != root; t = t.parent)
            {
                if (!t.gameObject.activeSelf || hidden.Contains(t))
                {
                    return false;
                }
            }
            return true;
        }

        private static Bounds MeshBounds(Transform model)
        {
            bool any = false;
            Bounds bounds = new Bounds();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds b = filter.sharedMesh.bounds;
                Matrix4x4 m = model.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }
                }
            }
            return bounds;
        }

        // A small bench model under parent, scaled so it is width meters wide, centred on the parent.
        public static Transform NewModel(Transform parent, float width)
        {
            GameObject model = Object.Instantiate(BenchModel, parent, false);
            model.name = "bench";
            FitModel(model.transform, width, Vector3.zero, Quaternion.identity);
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = parent.gameObject.layer;
            }
            return model.transform;
        }

        // Sizes and places a bench model in meters, whatever the scale of its parent (the player's bones are scaled
        // about 100x).
        public static void FitModel(Transform model, float width, Vector3 position, Quaternion rotation)
        {
            float parentScale = model.parent ? Mathf.Max(0.0001f, model.parent.lossyScale.x) : 1f;
            float factor = width / Mathf.Max(0.01f, ModelBounds.size.x);
            model.localScale = PlacedScale * (factor / parentScale);
            model.localRotation = rotation;
            model.localPosition = (position - rotation * (ModelBounds.center * factor)) / parentScale;
        }

        private static void BuildPiece(GameObject workbench)
        {
            PiecePrefab = BuildStationPiece(workbench, PieceName, Token, PlacedScale);
            BenchPiece = PiecePrefab.GetComponent<Piece>();
        }

        // A smaller copy of a crafting station piece that is one of ours (PlacedBench), registered with the game.
        private static GameObject BuildStationPiece(GameObject source, string name, string token, Vector3 scale)
        {
            GameObject prefab = PrefabManager.Instance.CreateClonedPrefab(name, source);
            CraftingStation station = prefab.GetComponent<CraftingStation>();
            Piece piece = prefab.GetComponent<Piece>();
            piece.m_name = token;
            piece.m_description = token + "_desc";

            // Shrink everything (model, colliders, snap points) except the build-area marker and the base area around
            // the bench: those are sized in meters by the build radius.
            Transform root = prefab.transform;
            HashSet<Transform> keep = new HashSet<Transform>();
            if (station.m_areaMarker)
            {
                keep.Add(TopChild(root, station.m_areaMarker.transform));
            }
            if (station.m_effectAreaCollider)
            {
                keep.Add(TopChild(root, station.m_effectAreaCollider.transform));
            }
            GameObject scaled = new GameObject("scaled") { layer = prefab.layer };
            scaled.transform.SetParent(root, false);
            foreach (Transform child in root.Cast<Transform>().ToList())
            {
                if (child != scaled.transform && !keep.Contains(child))
                {
                    child.SetParent(scaled.transform, false);
                }
            }
            scaled.transform.localScale = scale;

            prefab.AddComponent<PlacedBench>();
            PrefabManager.Instance.AddPrefab(prefab);
            return prefab;
        }

        private static Transform TopChild(Transform root, Transform t)
        {
            for (; t != null && t.parent != root; t = t.parent)
            {
            }
            return t;
        }

        private static void BuildItem(Piece workbenchPiece)
        {
            ItemConfig config = new ItemConfig
            {
                Name = Token,
                Description = Token + "_desc",
                CraftingStation = CraftingStations.Workbench,
                MinStationLevel = 1,
                Weight = Settings.Weight.Value,
                Icon = workbenchPiece.m_icon,
                Requirements = new[] { new RequirementConfig("Wood", 10), new RequirementConfig("DeerHide", 2) }
            };
            CustomItem item = new CustomItem(ItemName, "BeltStrength", config);
            ItemPrefab = item.ItemPrefab;

            ItemDrop.ItemData.SharedData shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_maxQuality = MaxLevel;
            shared.m_maxStackSize = 1;
            shared.m_equipStatusEffect = null;
            shared.m_setStatusEffect = null;
            shared.m_setName = "";
            shared.m_setSize = 0;
            shared.m_movementModifier = 0f;
            shared.m_scaleWeightByQuality = 0f;
            shared.m_teleportable = true;
            shared.m_value = 0;
            ApplyShared(shared);

            // Replace the belt's own models: a small bench on the ground, and one for the wearer's back.
            foreach (Transform child in ItemPrefab.transform.Cast<Transform>().ToList())
            {
                Object.DestroyImmediate(child.gameObject);
            }
            LODGroup lod = ItemPrefab.GetComponent<LODGroup>();
            if (lod)
            {
                Object.DestroyImmediate(lod);
            }

            Transform ground = NewModel(ItemPrefab.transform, GroundWidth);
            BoxCollider box = ground.gameObject.AddComponent<BoxCollider>();
            Bounds unit = MeshBounds(ground);
            box.center = unit.center;
            box.size = unit.size;

            GameObject attach = new GameObject("attach_" + BackBone);
            attach.transform.SetParent(ItemPrefab.transform, false);
            attach.SetActive(false);
            NewModel(attach.transform, BackWidth);

            ItemManager.Instance.AddItem(item);
        }

        // The stonecutter add-on: its model (for beside a set-down bench) and the recipe item at the stonecutter.
        private static void BuildCutter()
        {
            GameObject prefab = PrefabManager.Instance.GetPrefab(StonecutterPrefab);
            Stonecutter = prefab ? prefab.GetComponent<CraftingStation>() : null;
            StonecutterPiece = prefab ? prefab.GetComponent<Piece>() : null;
            if (!Stonecutter || !StonecutterPiece)
            {
                Plugin.Log.LogWarning("No stonecutter in this game version; backpacks can't get one.");
                return;
            }
            CutterModel = CopyMeshes(prefab, "CutterModel");
            cutterBounds = MeshBounds(CutterModel.transform);

            ItemConfig config = new ItemConfig
            {
                Name = CutterToken,
                Description = CutterToken + "_desc",
                CraftingStation = StonecutterPrefab,
                MinStationLevel = 1,
                Icon = StonecutterPiece.m_icon,
                // The real list (the backpack plus what building a stonecutter costs) is set in Recipes.Rebuild.
                Requirements = new[] { new RequirementConfig("Wood", 10) }
            };
            CustomItem item = new CustomItem(CutterItemName, "Wood", config);
            ItemDrop.ItemData.SharedData shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_maxStackSize = 1;
            shared.m_weight = 0f;
            shared.m_value = 0;
            shared.m_teleportable = true;
            ItemManager.Instance.AddItem(item);
            Plugin.Log.LogInfo($"Backpack stonecutter added (cost {string.Join(", ", StonecutterPiece.m_resources.Select(r => $"{(r.m_resItem ? r.m_resItem.name : "?")} {r.m_amount}"))}, " +
                $"vanilla build radius {Stonecutter.m_rangeBuild:0} m, craft tab {Stonecutter.m_hasCraftTab}, " +
                $"model {cutterBounds.size.x:0.00} m wide).");
        }

        // The forge backpack: its model, the set-down forge and the item, made from the real forge.
        private static void BuildForge()
        {
            GameObject forge = PrefabManager.Instance.GetPrefab(ForgePrefab);
            Forge = forge ? forge.GetComponent<CraftingStation>() : null;
            ForgeBuildPiece = forge ? forge.GetComponent<Piece>() : null;
            if (!Forge || !ForgeBuildPiece)
            {
                Plugin.Log.LogWarning("No forge in this game version; there is no forge backpack.");
                return;
            }
            ForgeModel = CopyMeshes(forge, "ForgeModel");
            forgeBounds = MeshBounds(ForgeModel.transform);
            // To the left of the bench, its back lined up with the bench's middle, on the same ground.
            ForgeOffset = new Vector3(
                ModelBounds.min.x - ForgeGap - forgeBounds.max.x * ForgeScale.x,
                0f,
                ModelBounds.center.z - forgeBounds.center.z * ForgeScale.z);

            ForgePiecePrefab = BuildStationPiece(forge, ForgePieceName, ForgeToken, ForgeScale);
            ForgePiece = ForgePiecePrefab.GetComponent<Piece>();
            BuildForgeItem();
            Plugin.Log.LogInfo($"Forge backpack added (cost {string.Join(", ", ForgeBuildPiece.m_resources.Select(r => $"{(r.m_resItem ? r.m_resItem.name : "?")} {r.m_amount}"))}, " +
                $"vanilla build radius {Forge.m_rangeBuild:0} m (+{Forge.m_extraRangePerLevel:0.#} per level), model {forgeBounds.size.x * ForgeScale.x:0.00} m wide set down, " +
                $"{ForgeOffset.x:0.00} m beside the bench).");
        }

        private static void BuildForgeItem()
        {
            ItemConfig config = new ItemConfig
            {
                Name = ForgeToken,
                Description = ForgeToken + "_desc",
                CraftingStation = CraftingStations.Forge,
                MinStationLevel = 1,
                Weight = Settings.ForgeWeight.Value,
                Icon = ForgeBuildPiece.m_icon,
                // The real list (the forge's build cost, then its upgrades) is set in Recipes.Rebuild.
                Requirements = new[] { new RequirementConfig("Wood", 10) }
            };
            CustomItem item = new CustomItem(ForgeItemName, "BeltStrength", config);
            ForgeItemPrefab = item.ItemPrefab;

            ItemDrop.ItemData.SharedData shared = item.ItemDrop.m_itemData.m_shared;
            // Carried in the bag, never worn: a plain item, not the belt it's made from.
            shared.m_itemType = ItemDrop.ItemData.ItemType.Misc;
            shared.m_maxStackSize = 1;
            shared.m_equipStatusEffect = null;
            shared.m_setStatusEffect = null;
            shared.m_setName = "";
            shared.m_setSize = 0;
            shared.m_movementModifier = 0f;
            shared.m_scaleWeightByQuality = 0f;
            shared.m_teleportable = true;
            shared.m_value = 0;
            ApplyShared(shared, forge: true);

            foreach (Transform child in ForgeItemPrefab.transform.Cast<Transform>().ToList())
            {
                Object.DestroyImmediate(child.gameObject);
            }
            LODGroup lod = ForgeItemPrefab.GetComponent<LODGroup>();
            if (lod)
            {
                Object.DestroyImmediate(lod);
            }
            Transform ground = Object.Instantiate(ForgeModel, ForgeItemPrefab.transform, false).transform;
            ground.name = "forge";
            float factor = GroundWidth / Mathf.Max(0.01f, forgeBounds.size.x);
            ground.localScale = Vector3.one * factor;
            ground.localPosition = -forgeBounds.center * factor;
            foreach (MeshRenderer renderer in ground.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterials = MovingMaterials(renderer.sharedMaterials);
            }
            foreach (Transform t in ground.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = ForgeItemPrefab.layer;
            }
            BoxCollider box = ForgeItemPrefab.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = forgeBounds.size * factor;

            ItemManager.Instance.AddItem(item);
        }

        // A small forge beside a set-down bench's ghost, where the set-down forge will stand.
        public static void AddForgeModel(Transform bench)
        {
            if (!ForgeModel || bench.Find("forgepack"))
            {
                return;
            }
            GameObject model = Object.Instantiate(ForgeModel, bench, false);
            model.name = "forgepack";
            model.transform.localScale = ForgeScale;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localPosition = ForgeOffset;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = bench.gameObject.layer;
            }
        }

        // A small stonecutter standing to the right of a set-down bench (or its ghost), on the same ground.
        public static void AddCutterModel(Transform bench)
        {
            if (!CutterModel || bench.Find("stonecutter"))
            {
                return;
            }
            GameObject model = Object.Instantiate(CutterModel, bench, false);
            model.name = "stonecutter";
            float factor = CutterWidth / Mathf.Max(0.01f, cutterBounds.size.x);
            model.transform.localScale = Vector3.one * factor;
            model.transform.localRotation = Quaternion.identity;
            Vector3 min = cutterBounds.min * factor;
            Vector3 center = cutterBounds.center * factor;
            model.transform.localPosition = new Vector3(
                ModelBounds.max.x + CutterGap - min.x,
                ModelBounds.min.y - min.y,
                ModelBounds.center.z - center.z);
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = bench.gameObject.layer;
            }
        }

        // Drops a backpack (or forge backpack) of this level on the ground (removed with the hammer, destroyed, or no
        // room).
        public static void DropBackpack(Vector3 position, Quaternion rotation, int level, long crafterId, string crafterName,
            float durability, bool cutter, bool forge = false)
        {
            GameObject prefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(forge ? ForgeItemName : ItemName) : null;
            if (!prefab)
            {
                return;
            }
            ItemDrop.ItemData data = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            data.m_dropPrefab = prefab;
            data.m_stack = 1;
            data.m_quality = Mathf.Clamp(level, 1, MaxLevelOf(forge));
            data.m_crafterID = crafterId;
            data.m_crafterName = crafterName ?? "";
            data.m_worldLevel = (byte)Game.m_worldLevel;
            SetState(data, durability, cutter);
            ItemDrop.DropItem(data, 1, position, rotation);
        }

        // A backpack coming back from a set-down bench: its durability (clamped to its level's) and its stonecutter.
        public static void SetState(ItemDrop.ItemData item, float durability, bool cutter)
        {
            bool forge = IsForge(item);
            item.m_durability = Mathf.Clamp(durability, 0f, MaxDurability(item.m_quality, forge));
            SetCutter(item, cutter && !forge);
            item.m_customData[SeenData] = "1";
        }

        // The tooltip of the forge backpack: its level, its upgrades, what carrying it does and where it repairs.
        public static string ForgeTooltip(int level)
        {
            Localization loc = Localization.instance;
            List<string> packed = new List<string>();
            for (int i = 0; i < ForgeUpgrades.Length && i < level - 1; i++)
            {
                Piece upgrade = PrefabManager.Instance.GetPrefab(ForgeUpgrades[i])?.GetComponent<Piece>();
                if (upgrade)
                {
                    packed.Add(loc.Localize(upgrade.m_name));
                }
            }
            StringBuilder text = new StringBuilder();
            text.Append($"\n{loc.Localize("$shack_forgepack_level")}: <color=orange>{level}</color>");
            text.Append($"\n{loc.Localize("$shack_benchpack_packed")}: <color=orange>" +
                $"{(packed.Count > 0 ? string.Join(", ", packed) : loc.Localize("$shack_benchpack_nothing"))}</color>");
            if (Settings.ForgeSlowPercent.Value > 0)
            {
                text.Append($"\n{loc.Localize("$shack_benchpack_slow")}: <color=orange>-{Settings.ForgeSlowPercent.Value}%</color>");
            }
            if (Settings.RepairNeedsLevel.Value)
            {
                text.Append($"\n{loc.Localize("$shack_forgepack_repair")}: <color=orange>{level}</color> " +
                    loc.Localize("$shack_benchpack_orhigher"));
            }
            return text.ToString();
        }

        // The tooltip of the backpack: its level, what it has packed, what carrying it does and where it repairs.
        public static string Tooltip(int level, bool cutter)
        {
            Localization loc = Localization.instance;
            List<string> packed = new List<string>();
            for (int i = 0; i < Upgrades.Length && i < level - 1; i++)
            {
                Piece upgrade = PrefabManager.Instance.GetPrefab(Upgrades[i])?.GetComponent<Piece>();
                if (upgrade)
                {
                    packed.Add(loc.Localize(upgrade.m_name));
                }
            }
            if (cutter && StonecutterPiece)
            {
                packed.Add(loc.Localize(StonecutterPiece.m_name));
            }
            float radius = Stations.Radius(level);
            StringBuilder text = new StringBuilder();
            text.Append($"\n{loc.Localize("$shack_benchpack_level")}: <color=orange>{level}</color>");
            text.Append($"\n{loc.Localize("$shack_benchpack_packed")}: <color=orange>" +
                $"{(packed.Count > 0 ? string.Join(", ", packed) : loc.Localize("$shack_benchpack_nothing"))}</color>");
            if (Settings.SlowPercent.Value > 0)
            {
                text.Append($"\n{loc.Localize("$shack_benchpack_slow")}: <color=orange>-{Settings.SlowPercent.Value}%</color>");
            }
            text.Append($"\n{loc.Localize("$shack_benchpack_radius")}: <color=orange>{radius:0} m</color>");
            if (Settings.RepairNeedsLevel.Value)
            {
                text.Append($"\n{loc.Localize("$shack_benchpack_repair")}: <color=orange>{level}</color> " +
                    loc.Localize("$shack_benchpack_orhigher"));
            }
            return text.ToString();
        }
    }
}
