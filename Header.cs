using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheShack.WorkbenchPack
{
    // Working at a backpack bench with a stonecutter packed in (worn or set down): the crafting panel's header shows
    // the stonecutter's icon to the right of the workbench's, and reads "Workbench + Stonecutter".
    internal static class Header
    {
        private static Image cutterIcon;
        private static float nameSize = -1f;
        private static bool nameAutoSize;

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        private static class InventoryGui_UpdateRecipe
        {
            private static void Postfix(InventoryGui __instance, Player player)
            {
                CraftingStation station = player ? player.GetCurrentCraftingStation() : null;
                bool show = Stations.TryGet(station, out IBench bench) && bench.HasCutter && Content.StonecutterPiece;
                TMP_Text name = __instance.m_craftingStationName;
                Image icon = __instance.m_craftingStationIcon;
                if (!name || !icon)
                {
                    return;
                }
                if (nameSize < 0f)
                {
                    nameSize = name.fontSize;
                    nameAutoSize = name.enableAutoSizing;
                }
                if (show)
                {
                    Localization loc = Localization.instance;
                    name.text = loc.Localize(station.m_name) + " + " + loc.Localize(Content.StonecutterPiece.m_name);
                    // The longer name shrinks to fit the header instead of running into the icons.
                    name.enableAutoSizing = true;
                    name.fontSizeMax = nameSize;
                    name.fontSizeMin = nameSize * 0.5f;
                }
                else if (name.enableAutoSizing != nameAutoSize)
                {
                    name.enableAutoSizing = nameAutoSize;
                    name.fontSize = nameSize;
                }

                if (show && !cutterIcon)
                {
                    cutterIcon = Object.Instantiate(icon, icon.transform.parent);
                    cutterIcon.name = "ShackStonecutterIcon";
                    RectTransform from = icon.rectTransform;
                    RectTransform to = cutterIcon.rectTransform;
                    to.anchoredPosition = from.anchoredPosition + new Vector2(from.rect.width * 0.9f, 0f);
                }
                if (cutterIcon)
                {
                    cutterIcon.gameObject.SetActive(show);
                    if (show)
                    {
                        cutterIcon.sprite = Content.StonecutterPiece.m_icon;
                    }
                }
            }
        }
    }
}
