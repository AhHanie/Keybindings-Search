using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Keybindings_Search.Patches
{
    // Every change to DoWindowContents wraps one vanilla operation, so the method body is never replaced.
    // The four infixes live in one class on purpose: Harmony applies them together or not at all.
    [HarmonyPatch(typeof(Dialog_KeyBindings), nameof(Dialog_KeyBindings.DoWindowContents))]
    public static class DialogKeyBindingsSearchPatch
    {
        // The second 40f in DoWindowContents is the title rect height (the first is the Vector2 button size).
        // Growing it moves the scroll view down and shrinks it by the same amount.
        [HarmonyPostfix]
        [HarmonyInfix(40f, Positions = new[] { 2 })]
        public static float ReserveSearchRow(float __result)
        {
            return __result + DialogKeyBindingsSearchSupport.SearchRowReserve;
        }

        // The single title label is the first thing drawn in the header group, so the search row is drawn here
        // and the label keeps its original height.
        [HarmonyPrefix]
        [HarmonyInfix(typeof(Widgets), nameof(Widgets.Label), typeof(Rect), typeof(TaggedString))]
        public static void DrawSearchRow(
            ref Rect rect,
            [HarmonyOuter] Dialog_KeyBindings __instance,
            [HarmonyOuter] KeyPrefsData ___keyPrefsData,
            [HarmonyOuter] ref Vector2 ___scrollPosition)
        {
            DialogKeyBindingsSearchSupport.DrawSearchControls(__instance, ___keyPrefsData, ref ___scrollPosition, rect);
            rect.height = DialogKeyBindingsSearchSupport.TitleHeight;
        }

        [HarmonyPostfix]
        [HarmonyInfix(typeof(Dialog_KeyBindings), "contentHeight", InnerTargetKind.FieldRead)]
        public static float FilterContentHeight(float __result, [HarmonyOuter] Dialog_KeyBindings __instance)
        {
            return DialogKeyBindingsSearchSupport.GetContentHeight(__instance, __result);
        }

        // Vanilla copies this sequence into its working list and sorts it, so returning the matches is enough.
        [HarmonyPostfix]
        [HarmonyInfix(typeof(DefDatabase<KeyBindingDef>), nameof(DefDatabase<KeyBindingDef>.AllDefs), InnerTargetKind.Getter)]
        public static IEnumerable<KeyBindingDef> FilterBindings(IEnumerable<KeyBindingDef> __result, [HarmonyOuter] Dialog_KeyBindings __instance)
        {
            return DialogKeyBindingsSearchSupport.GetBindings(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(Dialog_KeyBindings), "DrawCategoryEntry")]
    public static class DialogKeyBindingsCategoryHighlightPatch
    {
        public static void Prefix(Dialog_KeyBindings __instance, KeyBindingCategoryDef category, float width, float curY, bool skipDrawing)
        {
            DialogKeyBindingsSearchSupport.DrawCategoryHighlight(__instance, category, width, curY, skipDrawing);
        }
    }

    [HarmonyPatch(typeof(Dialog_KeyBindings), "DrawKeyEntry")]
    public static class DialogKeyBindingsKeyHighlightPatch
    {
        public static void Prefix(Dialog_KeyBindings __instance, KeyBindingDef keyDef, Rect parentRect, float curY, bool skipDrawing)
        {
            DialogKeyBindingsSearchSupport.DrawKeyLabelHighlight(__instance, keyDef, parentRect, curY, skipDrawing);
        }

        public static void Postfix(Dialog_KeyBindings __instance, KeyPrefsData ___keyPrefsData, KeyBindingDef keyDef, Rect parentRect, float curY, bool skipDrawing)
        {
            DialogKeyBindingsSearchSupport.DrawKeyBindingHighlight(__instance, ___keyPrefsData, keyDef, parentRect, curY - DialogKeyBindingsSearchSupport.EntryHeight, skipDrawing);
        }
    }
}
