using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace TheShack.WorkbenchPack
{
    // The set-down bench, or the set-down forge beside it (the same component on both pieces). Its level, durability,
    // stonecutter and who crafted it are kept on its ZDO, so packing it up gives back the same backpack. Packing up asks
    // the ZDO's owner, which removes the bench and only then sends the backpack to whoever asked, so two players packing
    // it at once can't both get one. A bench and the forge set down with it share a random number (Content.PairKey), so
    // packing up either packs both.
    internal class PlacedBench : MonoBehaviour, IBench
    {
        public class PendingInfo
        {
            public bool Forge;
            public int Level;
            public long CrafterId;
            public string CrafterName;
            public string Placer;
            public float Durability;
            public bool Cutter;
            public long Pair;
            public bool Created;
        }

        // Set by Placing around Player.PlacePiece; the new bench (or forge) takes it in Awake (and marks it Created).
        public static PendingInfo Pending;
        // The one that took Pending last, so Placing knows whether the bench (and the forge) came of it.
        public static PlacedBench LastCreated;

        // Every set-down bench and forge in loaded zones, to find a partner nearby.
        private static readonly List<PlacedBench> all = new List<PlacedBench>();
        private const float PairReach = 6f;

        private const string PackUpRpc = "TheShack_BenchPackUp";
        private const string PackedRpc = "TheShack_BenchPacked";
        private const string WearRpc = "TheShack_BenchWear";

        private ZNetView nview;
        private Piece piece;
        private CraftingStation station;
        private CraftingStation cutter;

        public bool Worn => false;

        public bool IsForge { get; private set; }

        public string Token => IsForge ? Content.ForgeToken : Content.Token;

        public int Level => nview && nview.IsValid()
            ? Mathf.Clamp(nview.GetZDO().GetInt(Content.LevelKey, 1), 1, Content.MaxLevelOf(IsForge))
            : 1;

        // Benches set down before 1.1.0 have no durability stored: full.
        public float Durability
        {
            get
            {
                float max = Content.MaxDurability(Level, IsForge);
                float stored = nview && nview.IsValid() ? nview.GetZDO().GetFloat(Content.DurabilityKey, -1f) : -1f;
                return stored < 0f ? max : Mathf.Min(stored, max);
            }
        }

        public bool Broken => Durability <= 0f;

        public bool HasCutter => !IsForge && nview && nview.IsValid() && nview.GetZDO().GetBool(Content.CutterKey);

        private long Pair => nview && nview.IsValid() ? nview.GetZDO().GetLong(Content.PairKey) : 0L;

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            piece = GetComponent<Piece>();
            station = GetComponent<CraftingStation>();
            IsForge = piece && piece.m_name == Content.ForgeToken;
            if (station)
            {
                Stations.Register(station, this, IsForge ? Role.Forge : Role.Workbench);
            }
            if (nview && nview.GetZDO() != null)
            {
                all.Add(this);
                nview.Register(PackUpRpc, RPC_PackUp);
                nview.Register<float>(WearRpc, RPC_Wear);
                TakePending();
                if (HasCutter)
                {
                    cutter = Stonecutter.AddStation(gameObject, this);
                    Content.AddCutterModel(transform);
                }
            }
        }

        // Right as it's created, so the level is stored even if something later in PlacePiece fails.
        private void TakePending()
        {
            if (Pending == null || Pending.Created || Pending.Forge != IsForge || !nview.IsOwner())
            {
                return;
            }
            Pending.Created = true;
            LastCreated = this;
            ZDO zdo = nview.GetZDO();
            zdo.Set(Content.LevelKey, Mathf.Clamp(Pending.Level, 1, Content.MaxLevelOf(IsForge)));
            zdo.Set(Content.CrafterIdKey, Pending.CrafterId);
            zdo.Set(Content.CrafterNameKey, Pending.CrafterName ?? "");
            zdo.Set(Content.PlacerNameKey, Pending.Placer ?? "");
            zdo.Set(Content.DurabilityKey, Mathf.Max(0f, Pending.Durability));
            zdo.Set(Content.CutterKey, Pending.Cutter && !IsForge);
            if (Pending.Pair != 0L)
            {
                zdo.Set(Content.PairKey, Pending.Pair);
            }
        }

        // The forge set down with this bench (or the bench this forge was set down with), if it's still there: the one
        // nearby of the other kind with the same number.
        public PlacedBench Partner()
        {
            long pair = Pair;
            if (pair == 0L)
            {
                return null;
            }
            foreach (PlacedBench other in all)
            {
                if (other && other != this && other.IsForge != IsForge && other.Pair == pair &&
                    Vector3.Distance(other.transform.position, transform.position) < PairReach)
                {
                    return other;
                }
            }
            return null;
        }

        private void OnDestroy()
        {
            all.Remove(this);
            Stations.Unregister(station);
            Stations.Unregister(cutter);
        }

        // Whoever crafts here asks the bench's owner, who keeps its ZDO.
        public void Wear(float amount)
        {
            if (amount > 0f && nview && nview.IsValid())
            {
                nview.InvokeRPC(WearRpc, amount);
            }
        }

        private void RPC_Wear(long sender, float amount)
        {
            if (!nview.IsValid() || !nview.IsOwner() || float.IsNaN(amount))
            {
                return;
            }
            // Only someone working at the bench wears it down.
            Player worker = Player.GetAllPlayers().FirstOrDefault(p => p && p.GetZDOID().UserID == sender);
            if (!worker || Vector3.Distance(worker.transform.position, transform.position) > WearReach)
            {
                return;
            }
            float left = Mathf.Max(0f, Durability - Mathf.Clamp(amount, 0f, 1000f));
            nview.GetZDO().Set(Content.DurabilityKey, left);
        }

        public bool MayPackUp(Player player, bool showMessage)
        {
            long creator = piece ? piece.GetCreator() : 0L;
            if (!Settings.OwnerOnly.Value || creator == 0L || creator == player.GetPlayerID() ||
                SynchronizationManager.Instance.PlayerIsAdmin)
            {
                return true;
            }
            if (showMessage)
            {
                string placer = nview && nview.IsValid() ? nview.GetZDO().GetString(Content.PlacerNameKey) : "";
                player.Message(MessageHud.MessageType.Center, string.Format(
                    Localization.instance.Localize("$shack_benchpack_notyours"), placer.Length > 0 ? placer : "?"));
            }
            return false;
        }

        // Before the slow pack-up starts: the owner rule and the ward, with their messages.
        public bool MayStartPackUp(Player player)
        {
            if (!nview || !nview.IsValid() || !MayPackUp(player, showMessage: true))
            {
                return false;
            }
            if (!PrivateArea.CheckAccess(transform.position))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone");
                return false;
            }
            return true;
        }

        public void RequestPackUp(Player player)
        {
            if (!nview || !nview.IsValid() || !MayPackUp(player, showMessage: true))
            {
                return;
            }
            if (!PrivateArea.CheckAccess(transform.position))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone");
                return;
            }
            // A reply that never came (the bench was packed by someone else, or its owner left) doesn't count forever.
            if (Time.time - askedAt > ReplyWindow)
            {
                awaited = 0;
            }
            askedAt = Time.time;
            awaited++;
            nview.InvokeRPC(PackUpRpc);
        }

        // When this player last asked a bench to pack up, and how many replies are still due (a bench and its forge
        // answer separately). A "packed" reply is only taken shortly after asking, so no one can push backpacks into
        // other players' inventories.
        private static float askedAt = -100f;
        private static int awaited;
        private const float WearReach = 10f;
        private const float ReplyWindow = 15f;

        private void RPC_PackUp(long sender)
        {
            if (!nview.IsValid() || !nview.IsOwner())
            {
                return;
            }
            ZDO zdo = nview.GetZDO();
            ZPackage package = new ZPackage();
            package.Write(Level);
            package.Write(zdo.GetLong(Content.CrafterIdKey));
            package.Write(zdo.GetString(Content.CrafterNameKey));
            package.Write(transform.position);
            package.Write(Durability);
            package.Write(HasCutter);
            package.Write(IsForge);
            if (piece)
            {
                piece.m_placeEffect.Create(transform.position, transform.rotation);
            }
            // If whoever asked has just left, the backpack would be lost in the post: drop it here instead.
            bool requesterHere = sender == ZDOMan.GetSessionID() ||
                ZNet.instance.GetPlayerList().Any(p => p.m_characterID.UserID == sender);
            if (!requesterHere)
            {
                DropAsItem();
            }
            ZNetScene.instance.Destroy(gameObject);
            if (requesterHere)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, PackedRpc, package);
            }
        }

        private static void RPC_Packed(long sender, ZPackage package)
        {
            if (awaited <= 0 || Time.time - askedAt > ReplyWindow)
            {
                awaited = 0;
                Plugin.Log.LogWarning($"Ignored a packed bench nobody here asked for (from {sender}).");
                return;
            }
            if (--awaited <= 0)
            {
                askedAt = -100f;
            }
            int level;
            long crafterId;
            string crafterName;
            Vector3 position;
            float durability;
            bool hasCutter;
            bool forge;
            try
            {
                level = package.ReadInt();
                crafterId = package.ReadLong();
                crafterName = package.ReadString() ?? "";
                position = package.ReadVector3();
                durability = package.ReadSingle();
                hasCutter = package.ReadBool();
                forge = package.ReadBool();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Ignored a malformed packed-bench message: " + e.Message);
                return;
            }
            level = Mathf.Clamp(level, 1, Content.MaxLevelOf(forge));
            hasCutter = hasCutter && !forge;
            if (crafterName.Length > 64)
            {
                crafterName = crafterName.Substring(0, 64);
            }
            if (float.IsNaN(durability))
            {
                durability = 0f;
            }
            Player player = Player.m_localPlayer;
            if (!player)
            {
                Content.DropBackpack(position + Vector3.up * 0.5f, Quaternion.identity, level, crafterId, crafterName,
                    durability, hasCutter, forge);
                return;
            }
            Give(player, forge, level, crafterId, crafterName, durability, hasCutter);
        }

        private static void Give(Player player, bool forge, int level, long crafterId, string crafterName, float durability,
            bool hasCutter)
        {
            Inventory inventory = player.GetInventory();
            GameObject prefab = forge ? Content.ForgeItemPrefab : Content.ItemPrefab;
            string token = forge ? Content.ForgeToken : Content.Token;
            ItemDrop.ItemData item = prefab && inventory.CanAddItem(prefab, 1)
                ? inventory.AddItem(forge ? Content.ForgeItemName : Content.ItemName, 1, level, 0, crafterId, crafterName, false)
                : null;
            if (item == null)
            {
                Content.DropBackpack(player.transform.position + player.transform.forward * 0.6f + Vector3.up,
                    player.transform.rotation, level, crafterId, crafterName, durability, hasCutter, forge);
                player.Message(MessageHud.MessageType.Center, "$shack_benchpack_full");
                return;
            }
            Content.SetState(item, durability, hasCutter);
            Access.UpdateTotalWeight(inventory);
            player.Message(MessageHud.MessageType.TopLeft, "$msg_added " + token, 1, item.GetIcon());
            // A broken one can't be worn (the game refuses), so it stays in the bag. The forge always goes in the bag.
            if (!forge && Settings.WearAfterPackUp.Value && Access.UtilityItem(player) == null && !Content.Broken(item))
            {
                player.EquipItem(item);
            }
        }

        private void DropAsItem()
        {
            ZDO zdo = nview && nview.IsValid() ? nview.GetZDO() : null;
            Content.DropBackpack(transform.position + Vector3.up * 0.5f, transform.rotation, Level,
                zdo?.GetLong(Content.CrafterIdKey) ?? 0L, zdo?.GetString(Content.CrafterNameKey) ?? "", Durability, HasCutter,
                IsForge);
        }

        [HarmonyPatch(typeof(Game), "Start")]
        private static class Game_Start
        {
            private static void Postfix() => ZRoutedRpc.instance.Register<ZPackage>(PackedRpc, RPC_Packed);
        }

        // Removed with the hammer or destroyed: drop the backpack (at its level) instead of the workbench's wood.
        [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
        private static class Piece_DropResources
        {
            private static bool Prefix(Piece __instance)
            {
                PlacedBench bench = __instance.GetComponent<PlacedBench>();
                if (!bench)
                {
                    return true;
                }
                bench.DropAsItem();
                return false;
            }
        }

        // The hammer's removal checks the owner rule; repairing (which runs the same check) doesn't.
        private static bool removing;

        [HarmonyPatch(typeof(Player), "RemovePiece")]
        private static class Player_RemovePiece
        {
            private static void Prefix() => removing = true;

            private static void Finalizer() => removing = false;
        }

        [HarmonyPatch(typeof(Player), "CheckCanRemovePiece")]
        private static class Player_CheckCanRemovePiece
        {
            private static void Postfix(Player __instance, Piece piece, ref bool __result)
            {
                if (removing && __result && piece && piece.GetComponent<PlacedBench>() is PlacedBench bench &&
                    !bench.MayPackUp(__instance, showMessage: true))
                {
                    __result = false;
                }
            }
        }
    }
}
