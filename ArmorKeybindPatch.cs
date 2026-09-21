using System.Reflection;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;
using Comfort.Common;
using EFT;

namespace ArmorInfo
{
    internal class ArmorKeybindPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemUiContext), "Update", null, null);
        }

        [PatchPostfix]
        private static void Postfix(ItemUiContext __instance)
        {
            if (ArmorInfoPlugin.KeybindShowArmorInfo == null || !ArmorInfoPlugin.KeybindShowArmorInfo.Value.IsDown())
                return;

            // Raid içindeyse çalışma
            if (Singleton<GameWorld>.Instantiated && !(Singleton<GameWorld>.Instance is HideoutGameWorld))
                return;

            var currentItemContext = __instance.CurrentItemContext;
            if (currentItemContext == null)
                return;

            Item item = currentItemContext.Item;
            if (item == null)
                return;

            // Artık armor kontrolü zorunlu değil; armor değilse null geçilir,
            // pencere N/A stat'larla sadece ID/isim gösterir.
            var armorTemplate = item.Template as IArmorComponentTemplate;

            ArmorInfoPlugin.ShowArmorInfo(item, armorTemplate);
        }
    }
}