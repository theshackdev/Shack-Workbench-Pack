using HarmonyLib;
using UnityEngine;

namespace TheShack.WorkbenchPack
{
    // Shift+E on a set-down bench packs it up as slowly as equipping gear: the backpack's equip time, with the game's
    // equipping animation and its progress bar. Walking away, attacking or dying stops it. On a bench or the forge set
    // down with it, it packs up both.
    internal static class PackingUp
    {
        private const float ExtraReach = 1f;
        private const string Animation = "equipping";

        private static PlacedBench bench;
        private static bool animating;
        private static float elapsed;
        private static readonly Player.MinorActionData progress = new Player.MinorActionData();

        public static bool Active => bench;

        private static float Duration
        {
            get
            {
                float time = Content.ItemPrefab ? Content.ItemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_equipDuration : 1f;
                return Mathf.Max(1f, time);
            }
        }

        public static void Start(Player player, PlacedBench target)
        {
            if (bench == target || !target.MayStartPackUp(player))
            {
                return;
            }
            Stop(player);
            bench = target;
            elapsed = 0f;
            progress.m_duration = Duration;
            progress.m_progressText = "$shack_benchpack_packing " + target.Token;
            Access.Zanim(player)?.SetBool(Animation, true);
            animating = true;
            player.m_equipStartEffects.Create(player.transform.position, Quaternion.identity);
        }

        private static void Stop(Player player)
        {
            if (animating && player)
            {
                Access.Zanim(player)?.SetBool(Animation, false);
            }
            animating = false;
            bench = null;
        }

        // Called every frame from the plugin.
        public static void Update()
        {
            if (bench is null)
            {
                return;
            }
            Player player = Player.m_localPlayer;
            CraftingStation station = bench ? bench.GetComponent<CraftingStation>() : null;
            if (!player || player.IsDead() || !station || player.InAttack() ||
                Vector3.Distance(player.transform.position, bench.transform.position) > station.m_useDistance + ExtraReach)
            {
                Stop(player);
                return;
            }
            elapsed += Time.deltaTime;
            if (elapsed >= progress.m_duration)
            {
                PlacedBench done = bench;
                PlacedBench partner = done.Partner();
                Stop(player);
                done.RequestPackUp(player);
                if (partner && partner.MayPackUp(player, showMessage: false))
                {
                    partner.RequestPackUp(player);
                }
            }
        }

        // The game's progress bar under the crosshair, as when equipping.
        [HarmonyPatch(typeof(Player), nameof(Player.GetActionProgress),
            new[] { typeof(string), typeof(float), typeof(Player.MinorActionData) },
            new[] { ArgumentType.Out, ArgumentType.Out, ArgumentType.Out })]
        private static class Player_GetActionProgress
        {
            private static void Postfix(Player __instance, ref string name, ref float progress, ref Player.MinorActionData data)
            {
                if (!Active || __instance != Player.m_localPlayer || !string.IsNullOrEmpty(name))
                {
                    return;
                }
                name = PackingUp.progress.m_progressText;
                progress = Mathf.Clamp01(elapsed / PackingUp.progress.m_duration);
                data = PackingUp.progress;
            }
        }
    }
}
