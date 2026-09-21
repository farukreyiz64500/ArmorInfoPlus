using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using EFT.InventoryLogic;
using EFT.UI;
using EFT;
using JsonType;

namespace ArmorInfo
{
    public class ArmorContextMenu
    {
        public static void InitPatches()
        {
            var harmony = new Harmony("com.faruk.armorinfo.contextmenu");
            harmony.PatchAll();
        }

        internal static IEftSession GetSession()
        {
            var instance = ItemUiContext.Instance;
            return instance != null ? instance.Session : null;
        }
    }

    [HarmonyPatch]
    public static class ContextMenuShowMenuPatch
    {
        public static MethodBase TargetMethod()
        {
            var method = typeof(SimpleContextMenu)
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => 
                    m.Name == "ShowMenu" && 
                    m.IsGenericMethodDefinition && 
                    m.GetParameters().Any(p => typeof(Item).IsAssignableFrom(p.ParameterType)));

            return method?.MakeGenericMethod(typeof(EItemInfoButton));
        }

        [HarmonyPrefix]
        public static void Prefix(ContextInteractions<EItemInfoButton> contextInteractions, Item item)
        {
            try
            {
                if (contextInteractions == null || item == null) return;

                var dynamicInteractions = AccessTools.Field(typeof(ContextInteractions<EItemInfoButton>), "_dynamicInteractions")?.GetValue(contextInteractions) as Dictionary<string, DynamicContextInteraction>;
                if (dynamicInteractions == null) return;

                // Artık her eşyada gösteriliyor; armor ise IArmorComponentTemplate dolu gelir,
                // değilse null geçilir ve pencere N/A stat'larla ID gösterir.
                var armorTemplate = item.Template as IArmorComponentTemplate;

                if (!dynamicInteractions.ContainsKey("ArmorInfo"))
                {
                    dynamicInteractions["ArmorInfo"] = new DynamicContextInteraction("ArmorInfo", "Item Info", delegate
                    {
                        ArmorInfoPlugin.ShowArmorInfo(item, armorTemplate);
                    }, null);
                }
            }
            catch (Exception ex)
            {
                ArmorInfoPlugin.LogSource?.LogError($"ShowMenu prefix hatası: {ex}");
            }
        }
    }
}