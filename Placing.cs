using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TheShack.WorkbenchPack
{
    // Setting the backpack down. The hotbar key (or a quick slot) on the backpack, or the "Set down" key while it's
    // worn, shows a ghost of the bench where you aim: left-click sets it down, scroll turns it, right-click (or the
    // same key again) cancels. Attacks and camera zoom are held back meanwhile. The bench is placed with the game's
    // own Player.PlacePiece, so it gets its creator, effects and stats like any built piece. A forge backpack in the
    // bag goes down with it, to the bench's left (its spot has to be fine too), and the two are linked.
    internal static class Placing
    {
        private static GameObject ghost;
        private static Piece ghostPiece;
        private static BoxCollider ghostBox;
        private static ItemDrop.ItemData item;
        private static ItemDrop.ItemData forgeItem;
        private static Transform forgeGhost;
        private static Vector3 forgePoint;
        private static int rotation;
        private static bool holdBack;

        public static bool Active => ghost != null;
        public static bool ReadingScroll;

        // Keeps holding back attacks after placing until the mouse buttons are let go, so the click doesn't punch.
        public static bool HoldBackAttacks => Active || holdBack;

        public static void Toggle(Player player, ItemDrop.ItemData backpack)
        {
            if (Active)
            {
                Cancel();
            }
            else
            {
                Begin(player, backpack);
            }
        }

        private static void Begin(Player player, ItemDrop.ItemData backpack)
        {
            if (!Content.Ready || !player || player.IsDead() || backpack == null)
            {
                return;
            }
            if (Content.Broken(backpack))
            {
                player.Message(MessageHud.MessageType.Center, "$shack_benchpack_broken");
                return;
            }
            if (player.InPlaceMode())
            {
                player.HideHandItems();
            }
            ZNetView.m_forceDisableInit = true;
            try
            {
                ghost = Object.Instantiate(Content.PiecePrefab);
            }
            finally
            {
                ZNetView.m_forceDisableInit = false;
            }
            ghost.name = Content.PieceName + "_ghost";
            // Without its network object the bench never picks its look, so all of them would show at once.
            foreach (Transform look in Content.HiddenLooks(ghost))
            {
                look.gameObject.SetActive(false);
            }
            WearNTear wear = ghost.GetComponent<WearNTear>();
            if (wear && wear.m_new)
            {
                wear.m_new.SetActive(true);
            }
            if (Content.HasCutter(backpack))
            {
                Content.AddCutterModel(ghost.transform);
            }
            forgeItem = null;
            // An unbroken forge backpack goes down; a broken one stays in the bag.
            ItemDrop.ItemData forge = !Content.ForgeReady ? null
                : player.GetInventory().GetAllItems().FirstOrDefault(i => Content.IsForge(i) && !Content.Broken(i))
                ?? player.GetInventory().GetAllItems().FirstOrDefault(Content.IsForge);
            if (forge != null && Content.Broken(forge))
            {
                player.Message(MessageHud.MessageType.Center, "$shack_forgepack_brokenleft");
            }
            else if (forge != null)
            {
                forgeItem = forge;
                Content.AddForgeModel(ghost.transform);
            }
            // Like the game's own ghosts: materials that follow the ghost as it moves, and no shadow.
            foreach (MeshRenderer renderer in ghost.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterials = Content.MovingMaterials(renderer.sharedMaterials);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (EffectArea area in ghost.GetComponentsInChildren<EffectArea>(true))
            {
                Object.DestroyImmediate(area);
            }
            foreach (Joint joint in ghost.GetComponentsInChildren<Joint>(true))
            {
                Object.DestroyImmediate(joint);
            }
            foreach (Rigidbody body in ghost.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(body);
            }
            foreach (Collider collider in ghost.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            int layer = LayerMask.NameToLayer("ghost");
            foreach (Transform t in ghost.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
            }
            ghostPiece = ghost.GetComponent<Piece>();
            ghostBox = ghost.transform.Find("scaled/collider")?.GetComponent<BoxCollider>();
            forgeGhost = ghost.transform.Find("forgepack");
            ghost.SetActive(false);
            item = backpack;
            player.Message(MessageHud.MessageType.TopLeft, "$shack_benchpack_placing");
        }

        public static void Cancel()
        {
            if (ghost)
            {
                Object.Destroy(ghost);
            }
            ghost = null;
            ghostPiece = null;
            ghostBox = null;
            item = null;
            forgeItem = null;
            forgeGhost = null;
            holdBack = true;
        }

        // Called every frame from the plugin.
        public static void Update()
        {
            Player player = Player.m_localPlayer;
            if (holdBack && !ZInput.GetButton("Attack") && !ZInput.GetButton("Block") && !ZInput.GetButton("JoyAttack") &&
                !ZInput.GetButton("JoyBlock"))
            {
                holdBack = false;
            }
            if (!Active)
            {
                CheckSetDownKey(player);
                return;
            }
            if (!player || player.IsDead() || item == null || !player.GetInventory().ContainsItem(item) ||
                player.InPlaceMode() || InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen())
            {
                Cancel();
                return;
            }
            if (!Access.TakeInput(player))
            {
                ghost.SetActive(false);
                return;
            }
            if (ZInput.GetButtonDown("Block") || ZInput.GetButtonDown("JoyBlock") || SetDownKeyDown())
            {
                Cancel();
                return;
            }

            ReadingScroll = true;
            float scroll;
            try
            {
                scroll = ZInput.GetMouseScrollWheel();
            }
            finally
            {
                ReadingScroll = false;
            }
            if (scroll > 0f)
            {
                rotation = (rotation + 1) % 16;
            }
            else if (scroll < 0f)
            {
                rotation = (rotation + 15) % 16;
            }

            if (!Access.PieceRayTest(player, out Vector3 point, out Vector3 normal, out _, out _, out Collider water, false))
            {
                ghost.SetActive(false);
                return;
            }
            ghost.SetActive(true);
            Quaternion turn = Quaternion.Euler(0f, rotation * 22.5f, 0f);
            ghost.transform.SetPositionAndRotation(point, turn);
            // The placement ray ignores water; a second ray that stops at the water says whether the spot is under it.
            Access.PieceRayTest(player, out _, out _, out _, out _, out Collider waterSurface, true);
            string problem = Problem(point, normal, water ? water : waterSurface);
            if (forgeItem != null)
            {
                problem = problem ?? ForgeProblem(point, turn);
            }
            ghostPiece.SetInvalidPlacementHeightlight(problem != null);

            if (ZInput.GetButtonDown("Attack") || ZInput.GetButtonDown("JoyPlace"))
            {
                if (problem != null)
                {
                    player.Message(MessageHud.MessageType.Center, problem);
                }
                else
                {
                    Place(player, point, turn);
                }
            }
        }

        private static void CheckSetDownKey(Player player)
        {
            if (!player || player.IsDead() || !SetDownKeyDown() || !Access.TakeInput(player))
            {
                return;
            }
            ItemDrop.ItemData backpack = Worn.Item(player) ?? player.GetInventory().GetAllItems().FirstOrDefault(Content.IsOurs);
            if (backpack != null)
            {
                Begin(player, backpack);
            }
        }

        // Like vanilla: no setting it down on top of a player or creature. The ghost's colliders are off, so test the
        // bench's box collider's space directly.
        private static bool BlockedByCharacter()
        {
            BoxCollider box = ghostBox;
            if (!box)
            {
                return false;
            }
            Vector3 center = box.transform.TransformPoint(box.center);
            Vector3 half = Vector3.Scale(box.size * 0.5f, box.transform.lossyScale);
            foreach (Collider hit in Physics.OverlapBox(center, half, box.transform.rotation, CharacterMask))
            {
                if (hit.GetComponentInParent<Character>())
                {
                    return true;
                }
            }
            return false;
        }

        private static int characterMask;

        private static int CharacterMask => characterMask != 0 ? characterMask
            : characterMask = LayerMask.GetMask("character", "character_net", "character_ghost", "character_noenv");

        // KeyboardShortcut.IsDown() is false while any other key is held (walking, running), so only the shortcut's
        // own keys count here, as in Shack Magic Cam.
        private static bool SetDownKeyDown()
        {
            BepInEx.Configuration.KeyboardShortcut shortcut = Settings.SetDownKey.Value;
            if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey))
            {
                return false;
            }
            // Alt+key belongs to other mods (Alt+U is the compass), unless the shortcut asks for Alt itself.
            bool altInShortcut = shortcut.Modifiers.Any(k => k == KeyCode.LeftAlt || k == KeyCode.RightAlt);
            if (!altInShortcut && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
            {
                return false;
            }
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (!Input.GetKey(modifier))
                {
                    return false;
                }
            }
            return true;
        }

        private static string Problem(Vector3 point, Vector3 normal, Collider water)
        {
            if (water)
            {
                return "$shack_benchpack_badspot";
            }
            if (normal.y < 0.6f)
            {
                return "$shack_benchpack_steep";
            }
            if (Location.IsInsideNoBuildLocation(point))
            {
                return "$msg_nobuildzone";
            }
            if (!PrivateArea.CheckAccess(point, 0f, flash: false))
            {
                return "$msg_privatezone";
            }
            if (BlockedByCharacter())
            {
                return "$msg_blocked";
            }
            return null;
        }

        // The forge's spot beside the bench: ground under it at about the bench's height (it follows the ground, as the
        // ghost shows), no wall between the two, nobody standing there, not too steep, not under the sea, and buildable.
        private static string ForgeProblem(Vector3 point, Quaternion turn)
        {
            if (forgeGhost)
            {
                forgeGhost.localPosition = Content.ForgeOffset;
            }
            Vector3 spot = point + turn * Content.ForgeOffset;
            if (!Physics.Raycast(spot + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f, GroundMask) ||
                Mathf.Abs(hit.point.y - point.y) > MaxStep)
            {
                return "$shack_forgepack_noroom";
            }
            forgePoint = hit.point;
            if (forgeGhost)
            {
                forgeGhost.position = hit.point;
            }
            if (Physics.Linecast(point + Vector3.up * 0.6f, hit.point + Vector3.up * 0.6f, GroundMask))
            {
                return "$shack_forgepack_noroom";
            }
            Bounds size = Content.ForgeBoundsPlaced;
            foreach (Collider other in Physics.OverlapBox(hit.point + turn * size.center, size.extents, turn, CharacterMask))
            {
                if (other.GetComponentInParent<Character>())
                {
                    return "$msg_blocked";
                }
            }
            if (hit.normal.y < 0.6f)
            {
                return "$shack_benchpack_steep";
            }
            if (ZoneSystem.instance && hit.point.y < ZoneSystem.instance.m_waterLevel)
            {
                return "$shack_forgepack_noroom";
            }
            if (Location.IsInsideNoBuildLocation(hit.point))
            {
                return "$msg_nobuildzone";
            }
            if (!PrivateArea.CheckAccess(hit.point, 0f, flash: false))
            {
                return "$msg_privatezone";
            }
            return null;
        }

        private static int groundMask;
        private const float MaxStep = 0.6f;

        private static int GroundMask => groundMask != 0 ? groundMask
            : groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");

        private static void Place(Player player, Vector3 point, Quaternion turn)
        {
            ItemDrop.ItemData backpack = item;
            ItemDrop.ItemData forge = forgeItem;
            Vector3 forgeAt = forgePoint;
            Cancel();
            // The number that links the bench and the forge set down with it (see PlacedBench.Partner).
            long pair = forge == null ? 0L : ((long)UnityEngine.Random.Range(1, int.MaxValue) << 31) ^ DateTime.UtcNow.Ticks;
            if (forge != null && pair == 0L)
            {
                pair = 1L;
            }
            Inventory inventory = player.GetInventory();
            if (!inventory.ContainsItem(backpack))
            {
                return;
            }
            if (player.IsItemEquiped(backpack))
            {
                player.UnequipItem(backpack, triggerEquipEffects: false);
            }
            if (!inventory.RemoveItem(backpack))
            {
                Plugin.Log.LogWarning("Could not take the backpack out of the inventory to set it down.");
                return;
            }
            PlacedBench.PendingInfo pending = new PlacedBench.PendingInfo
            {
                Level = backpack.m_quality,
                CrafterId = backpack.m_crafterID,
                CrafterName = backpack.m_crafterName,
                Placer = player.GetPlayerName(),
                Durability = backpack.m_durability,
                Cutter = Content.HasCutter(backpack),
                Pair = pair
            };
            PlacedBench.Pending = pending;
            PlacedBench.LastCreated = null;
            try
            {
                player.PlacePiece(Content.BenchPiece, point, turn, doAttack: false);
            }
            catch (Exception e)
            {
                // Only give it back if no bench came of it; otherwise there would be two.
                Plugin.Log.LogError("Setting the bench down failed" + (pending.Created ? " after the bench was made. " :
                    "; the backpack goes back in your inventory. ") + e);
                if (!pending.Created)
                {
                    inventory.AddItem(backpack);
                }
            }
            finally
            {
                PlacedBench.Pending = null;
            }
            PlacedBench bench = pending.Created ? PlacedBench.LastCreated : null;
            if (bench && forge != null && inventory.ContainsItem(forge) && !Content.Broken(forge))
            {
                PlaceForge(player, forge, forgeAt, turn, pair);
            }
        }

        private static void PlaceForge(Player player, ItemDrop.ItemData forge, Vector3 point, Quaternion turn, long pair)
        {
            Inventory inventory = player.GetInventory();
            if (!inventory.RemoveItem(forge))
            {
                Plugin.Log.LogWarning("Could not take the forge backpack out of the inventory to set it down.");
                return;
            }
            PlacedBench.PendingInfo pending = new PlacedBench.PendingInfo
            {
                Forge = true,
                Level = forge.m_quality,
                CrafterId = forge.m_crafterID,
                CrafterName = forge.m_crafterName,
                Placer = player.GetPlayerName(),
                Durability = forge.m_durability,
                Pair = pair
            };
            PlacedBench.Pending = pending;
            PlacedBench.LastCreated = null;
            try
            {
                player.PlacePiece(Content.ForgePiece, point, turn, doAttack: false);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Setting the forge down failed" + (pending.Created ? " after the forge was made. " :
                    "; the forge backpack goes back in your inventory. ") + e);
                if (!pending.Created)
                {
                    inventory.AddItem(forge);
                }
            }
            finally
            {
                PlacedBench.Pending = null;
            }
        }

        // The hotbar and quick slots use items with fromInventoryGui = false; right-clicking in the inventory passes
        // true and still wears or takes off the backpack.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
        private static class Humanoid_UseItem
        {
            private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, bool fromInventoryGui)
            {
                Player player = Player.m_localPlayer;
                if (fromInventoryGui || __instance != player || !player)
                {
                    return true;
                }
                if (Content.IsOurs(item))
                {
                    Toggle(player, item);
                    return false;
                }
                // The forge's own key sets down the workbench backpack, which brings the forge with it.
                if (Content.IsForge(item))
                {
                    ItemDrop.ItemData bench = Worn.Item(player) ?? player.GetInventory().GetAllItems().FirstOrDefault(Content.IsOurs);
                    if (Active)
                    {
                        Cancel();
                    }
                    else if (bench != null)
                    {
                        Begin(player, bench);
                    }
                    else
                    {
                        player.Message(MessageHud.MessageType.Center, "$shack_forgepack_needbench");
                    }
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class Player_SetControls
        {
            private static void Prefix(ref bool attack, ref bool attackHold, ref bool secondaryAttack,
                ref bool secondaryAttackHold, ref bool block, ref bool blockHold)
            {
                if (HoldBackAttacks)
                {
                    attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = false;
                }
            }
        }

        // The scroll wheel turns the ghost instead of zooming the camera.
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
        private static class ZInput_GetMouseScrollWheel
        {
            private static bool Prefix(ref float __result)
            {
                if (!Active || ReadingScroll)
                {
                    return true;
                }
                __result = 0f;
                return false;
            }
        }
    }
}
