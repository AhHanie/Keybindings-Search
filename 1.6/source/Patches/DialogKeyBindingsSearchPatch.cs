using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using Verse;

namespace Keybindings_Search.Patches
{
    [HarmonyPatch(typeof(Dialog_KeyBindings), "DoWindowContents")]
    public static class DialogKeyBindingsSearchPatch
    {
        private static readonly MethodInfo TextFontSetter = AccessTools.PropertySetter(typeof(Text), "Font");
        private static readonly MethodInfo RectHeightGetter = AccessTools.PropertyGetter(typeof(Rect), "height");
        private static readonly MethodInfo DrawSearchControlsMethod = AccessTools.Method(typeof(DialogKeyBindingsSearchSupport), nameof(DialogKeyBindingsSearchSupport.DrawSearchControls));
        private static readonly MethodInfo PrepareWorkingListMethod = AccessTools.Method(typeof(DialogKeyBindingsSearchSupport), nameof(DialogKeyBindingsSearchSupport.PrepareWorkingList));
        private static readonly FieldInfo ContentHeightField = AccessTools.Field(typeof(Dialog_KeyBindings), "contentHeight");
        private static readonly FieldInfo WorkingListField = AccessTools.Field(typeof(Dialog_KeyBindings), "keyBindingsWorkingList");

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> original = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
            List<CodeInstruction> codes = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();

            bool injectedSearchRow = InjectSearchRow(codes);
            bool replacedContentHeight = ReplaceContentHeightLoad(codes);
            bool removedVanillaWorkingListBuild = RemoveVanillaWorkingListBuild(codes);

            if (!injectedSearchRow || !replacedContentHeight || !removedVanillaWorkingListBuild)
            {
                Logger.Error("Dialog_KeyBindings transpiler failed to match all expected IL anchors.");
                return original;
            }

            Logger.Message("Dialog_KeyBindings transpiler applied successfully.");
            return codes;
        }

        private static bool InjectSearchRow(List<CodeInstruction> codes)
        {
            int fontSetIndex = -1;
            for (int i = 1; i < codes.Count; i++)
            {
                if (codes[i].Calls(TextFontSetter) && codes[i - 1].opcode == OpCodes.Ldc_I4_1)
                {
                    fontSetIndex = i;
                    break;
                }
            }

            if (fontSetIndex < 0)
            {
                return false;
            }

            codes.InsertRange(fontSetIndex + 1, new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldloc_S, (byte)4),
                new CodeInstruction(OpCodes.Call, DrawSearchControlsMethod),
                new CodeInstruction(OpCodes.Stloc_1)
            });

            List<int> heightPairStarts = new List<int>();
            for (int i = fontSetIndex + 1; i < codes.Count; i++)
            {
                if (codes[i].Calls(RectHeightGetter) && i > 0 && LoadsLocalAddress(codes[i - 1], 4))
                {
                    heightPairStarts.Add(i - 1);
                    if (heightPairStarts.Count == 2)
                    {
                        break;
                    }
                }
            }

            if (heightPairStarts.Count != 2)
            {
                return false;
            }

            foreach (int pairStart in heightPairStarts)
            {
                ReplaceRectHeightAccessWithHeaderHeight(codes, pairStart);
            }

            return true;
        }

        private static bool ReplaceContentHeightLoad(List<CodeInstruction> codes)
        {
            for (int i = 1; i < codes.Count; i++)
            {
                if (Equals(codes[i].operand, ContentHeightField) && codes[i].opcode == OpCodes.Ldfld && codes[i - 1].opcode == OpCodes.Ldarg_0)
                {
                    List<Label> labels = new List<Label>();
                    List<ExceptionBlock> blocks = new List<ExceptionBlock>();
                    labels.AddRange(codes[i - 1].labels);
                    labels.AddRange(codes[i].labels);
                    blocks.AddRange(codes[i - 1].blocks);
                    blocks.AddRange(codes[i].blocks);

                    codes[i - 1] = new CodeInstruction(OpCodes.Ldarg_0);
                    codes[i - 1].labels.AddRange(labels);
                    codes[i - 1].blocks.AddRange(blocks);

                    codes[i] = new CodeInstruction(OpCodes.Call, PrepareWorkingListMethod);
                    return true;
                }
            }

            return false;
        }

        private static bool RemoveVanillaWorkingListBuild(List<CodeInstruction> codes)
        {
            int startIndex = -1;
            int endIndex = -1;

            for (int i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].opcode == OpCodes.Ldsfld && Equals(codes[i].operand, WorkingListField))
                {
                    MethodInfo method = codes[i + 1].operand as MethodInfo;
                    if (codes[i + 1].opcode == OpCodes.Callvirt && method != null && method.Name == "Clear")
                    {
                        startIndex = i;
                        break;
                    }
                }
            }

            if (startIndex < 0)
            {
                return false;
            }

            for (int i = startIndex; i < codes.Count; i++)
            {
                MethodInfo method = codes[i].operand as MethodInfo;
                if (codes[i].opcode == OpCodes.Call && method != null && method.Name == "SortBy")
                {
                    endIndex = i;
                    break;
                }
            }

            if (endIndex < startIndex)
            {
                return false;
            }

            for (int i = startIndex; i <= endIndex; i++)
            {
                codes[i].opcode = OpCodes.Nop;
                codes[i].operand = null;
            }

            return true;
        }

        private static void ReplaceRectHeightAccessWithHeaderHeight(List<CodeInstruction> codes, int pairStartIndex)
        {
            List<Label> labels = new List<Label>();
            List<ExceptionBlock> blocks = new List<ExceptionBlock>();
            labels.AddRange(codes[pairStartIndex].labels);
            labels.AddRange(codes[pairStartIndex + 1].labels);
            blocks.AddRange(codes[pairStartIndex].blocks);
            blocks.AddRange(codes[pairStartIndex + 1].blocks);

            codes[pairStartIndex] = new CodeInstruction(OpCodes.Ldloc_1);
            codes[pairStartIndex].labels.AddRange(labels);
            codes[pairStartIndex].blocks.AddRange(blocks);

            codes[pairStartIndex + 1].opcode = OpCodes.Nop;
            codes[pairStartIndex + 1].operand = null;
            codes[pairStartIndex + 1].labels.Clear();
            codes[pairStartIndex + 1].blocks.Clear();
        }

        private static bool LoadsLocalAddress(CodeInstruction instruction, int localIndex)
        {
            if (instruction.opcode != OpCodes.Ldloca && instruction.opcode != OpCodes.Ldloca_S)
            {
                return false;
            }

            if (instruction.operand is LocalBuilder)
            {
                return ((LocalBuilder)instruction.operand).LocalIndex == localIndex;
            }

            if (instruction.operand is byte)
            {
                return (byte)instruction.operand == localIndex;
            }

            if (instruction.operand is int)
            {
                return (int)instruction.operand == localIndex;
            }

            if (instruction.operand is short)
            {
                return (short)instruction.operand == localIndex;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Dialog_KeyBindings), "DrawCategoryEntry")]
    public static class DialogKeyBindingsCategoryHighlightPatch
    {
        public static void Prefix(KeyBindingCategoryDef category, float width, ref float curY, bool skipDrawing)
        {
            DialogKeyBindingsSearchSupport.DrawCategoryHighlight(category, width, curY, skipDrawing);
        }
    }

    [HarmonyPatch(typeof(Dialog_KeyBindings), "DrawKeyEntry")]
    public static class DialogKeyBindingsKeyHighlightPatch
    {
        public static void Prefix(Dialog_KeyBindings __instance, KeyBindingDef keyDef, Rect parentRect, ref float curY, bool skipDrawing)
        {
            DialogKeyBindingsSearchSupport.DrawKeyLabelHighlight(__instance, keyDef, parentRect, curY, skipDrawing);
        }

        public static void Postfix(Dialog_KeyBindings __instance, KeyBindingDef keyDef, Rect parentRect, ref float curY, bool skipDrawing)
        {
            DialogKeyBindingsSearchSupport.DrawKeyBindingHighlight(__instance, keyDef, parentRect, curY - 34f, skipDrawing);
        }
    }

    public static class DialogKeyBindingsSearchSupport
    {
        private const float SearchRowHeight = 36f;
        private const float SearchRowGap = 8f;
        private const float SearchControlGap = 8f;
        private const float SearchModeWidth = 150f;
        private const string SearchFieldControlName = "KeybindSearchField";
        private static readonly Color TextHighlightColor = new Color(1f, 0.9f, 0.2f, 0.45f);
        private static readonly Color FlatTextHighlightColor = new Color(1f, 0.9f, 0.2f, 0.35f);

        private static Dialog_KeyBindings activeDialog;
        private static SearchState activeSearchState = new SearchState();

        public static float DrawSearchControls(Dialog_KeyBindings dialog, Rect titleRect)
        {
            SearchState state = GetState(dialog);
            string previousQuery = state.Query;
            SearchMode previousMode = state.Mode;

            float rowY = titleRect.height + SearchRowGap;
            Rect rowRect = new Rect(0f, rowY, titleRect.width, SearchRowHeight);
            float modeWidth = Mathf.Min(SearchModeWidth, Mathf.Max(120f, rowRect.width * 0.28f));
            Rect textRect = new Rect(0f, rowRect.y, Mathf.Max(0f, rowRect.width - modeWidth - SearchControlGap), rowRect.height);
            Rect modeRect = new Rect(textRect.xMax + SearchControlGap, rowRect.y, modeWidth, rowRect.height);

            GUI.SetNextControlName(SearchFieldControlName);
            state.Query = Widgets.TextField(textRect, state.Query ?? string.Empty);
            DrawPlaceholderIfNeeded(textRect, state.Query);

            Widgets.Dropdown(modeRect, dialog, GetSearchMode, GenerateSearchModeOptions, GetSearchModeLabel(state.Mode));

            if (previousQuery != state.Query || previousMode != state.Mode)
            {
                dialog.scrollPosition = Vector2.zero;
            }

            return rowRect.yMax + SearchRowGap;
        }

        public static float PrepareWorkingList(Dialog_KeyBindings dialog)
        {
            List<KeyBindingDef> workingList = Dialog_KeyBindings.keyBindingsWorkingList;
            workingList.Clear();

            foreach (KeyBindingDef keyBindingDef in DefDatabase<KeyBindingDef>.AllDefs)
            {
                if (Matches(dialog, keyBindingDef))
                {
                    workingList.Add(keyBindingDef);
                }
            }

            workingList.SortBy(keyBindingDef => keyBindingDef.category.index, keyBindingDef => keyBindingDef.index);
            return CalculateContentHeight(workingList);
        }

        private static IEnumerable<Widgets.DropdownMenuElement<SearchMode>> GenerateSearchModeOptions(Dialog_KeyBindings dialog)
        {
            foreach (SearchMode mode in Enum.GetValues(typeof(SearchMode)).Cast<SearchMode>())
            {
                SearchMode localMode = mode;
                yield return new Widgets.DropdownMenuElement<SearchMode>
                {
                    payload = localMode,
                    option = new FloatMenuOption(GetSearchModeLabel(localMode), delegate
                    {
                        GetState(dialog).Mode = localMode;
                    })
                };
            }
        }

        private static SearchMode GetSearchMode(Dialog_KeyBindings dialog)
        {
            return GetState(dialog).Mode;
        }

        private static SearchState GetState(Dialog_KeyBindings dialog)
        {
            if (!ReferenceEquals(activeDialog, dialog))
            {
                activeDialog = dialog;
                activeSearchState = new SearchState();
            }

            return activeSearchState;
        }

        private static void DrawPlaceholderIfNeeded(Rect textRect, string query)
        {
            if (!string.IsNullOrEmpty(query) || GUI.GetNameOfFocusedControl() == SearchFieldControlName)
            {
                return;
            }

            Color color = GUI.color;
            TextAnchor anchor = Text.Anchor;
            GUI.color = Color.gray;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(textRect.x + 8f, textRect.y, Mathf.Max(0f, textRect.width - 16f), textRect.height), "KeybindSearch.SearchPlaceholder".Translate());
            Text.Anchor = anchor;
            GUI.color = color;
        }

        public static void DrawCategoryHighlight(KeyBindingCategoryDef category, float width, float curY, bool skipDrawing)
        {
            if (skipDrawing || !ShouldHighlightCategory(category))
            {
                return;
            }

            Rect rect = new Rect(0f, curY, width, 40f).ContractedBy(4f);
            DrawMatchedTextHighlight(rect, category.LabelCap, activeSearchState.Query, GameFont.Medium, centered: false, verticallyCentered: true, trimBottom: 2f, flatFill: true);
        }

        public static void DrawKeyLabelHighlight(Dialog_KeyBindings dialog, KeyBindingDef keyBindingDef, Rect parentRect, float curY, bool skipDrawing)
        {
            if (skipDrawing || !ShouldHighlightKeyLabel(dialog, keyBindingDef))
            {
                return;
            }

            Rect rect = new Rect(parentRect.x, parentRect.y + curY, parentRect.width, 34f).ContractedBy(3f);
            DrawMatchedTextHighlight(rect, keyBindingDef.LabelCap, activeSearchState.Query, GameFont.Small, centered: false, verticallyCentered: true, trimBottom: 0f, flatFill: false);
        }

        public static void DrawKeyBindingHighlight(Dialog_KeyBindings dialog, KeyBindingDef keyBindingDef, Rect parentRect, float curY, bool skipDrawing)
        {
            if (skipDrawing || !ShouldHighlightKeyBinding(dialog))
            {
                return;
            }

            Rect rect = new Rect(parentRect.x, parentRect.y + curY, parentRect.width, 34f).ContractedBy(3f);
            float gap = 4f;
            Vector2 buttonSize = new Vector2(140f, 28f);
            Rect rectA = new Rect(rect.x + rect.width - buttonSize.x * 2f - gap, rect.y, buttonSize.x, buttonSize.y);
            Rect rectB = new Rect(rect.x + rect.width - buttonSize.x, rect.y, buttonSize.x, buttonSize.y);

            KeyPrefsData keyPrefsData = dialog.keyPrefsData;
            KeyCode keyA = keyPrefsData.GetBoundKeyCode(keyBindingDef, KeyPrefs.BindingSlot.A);
            KeyCode keyB = keyPrefsData.GetBoundKeyCode(keyBindingDef, KeyPrefs.BindingSlot.B);
            string labelA = keyA.ToStringReadable();
            string labelB = keyB.ToStringReadable();

            if (MatchesKeyCode(keyA, Normalize(activeSearchState.Query)))
            {
                DrawMatchedTextHighlight(rectA, labelA, activeSearchState.Query, GameFont.Small, centered: true, verticallyCentered: true, trimBottom: 0f, flatFill: false);
            }

            if (MatchesKeyCode(keyB, Normalize(activeSearchState.Query)))
            {
                DrawMatchedTextHighlight(rectB, labelB, activeSearchState.Query, GameFont.Small, centered: true, verticallyCentered: true, trimBottom: 0f, flatFill: false);
            }
        }

        private static bool Matches(Dialog_KeyBindings dialog, KeyBindingDef keyBindingDef)
        {
            SearchState state = GetState(dialog);
            string normalizedQuery = Normalize(state.Query);
            if (normalizedQuery.Length == 0)
            {
                return true;
            }

            switch (state.Mode)
            {
                case SearchMode.Action:
                    return MatchesText(keyBindingDef.LabelCap, normalizedQuery) || MatchesText(keyBindingDef.defName, normalizedQuery);
                case SearchMode.Key:
                    return MatchesKey(dialog, keyBindingDef, normalizedQuery);
                case SearchMode.Category:
                    return MatchesText(keyBindingDef.category.LabelCap, normalizedQuery) || MatchesText(keyBindingDef.category.defName, normalizedQuery);
                default:
                    return true;
            }
        }

        private static bool MatchesKey(Dialog_KeyBindings dialog, KeyBindingDef keyBindingDef, string normalizedQuery)
        {
            KeyPrefsData keyPrefsData = dialog.keyPrefsData;
            return MatchesKeyCode(keyPrefsData.GetBoundKeyCode(keyBindingDef, KeyPrefs.BindingSlot.A), normalizedQuery)
                || MatchesKeyCode(keyPrefsData.GetBoundKeyCode(keyBindingDef, KeyPrefs.BindingSlot.B), normalizedQuery);
        }

        private static bool MatchesKeyCode(KeyCode keyCode, string normalizedQuery)
        {
            if (normalizedQuery.Length == 0)
            {
                return false;
            }

            foreach (string searchTerm in GetKeySearchTerms(keyCode))
            {
                string normalizedTerm = Normalize(searchTerm);
                if (normalizedTerm.Length == 0)
                {
                    continue;
                }

                if (normalizedTerm == normalizedQuery || normalizedTerm.StartsWith(normalizedQuery) || (normalizedQuery.Length >= 3 && normalizedTerm.Contains(normalizedQuery)))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> GetKeySearchTerms(KeyCode keyCode)
        {
            HashSet<string> terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddKeySearchTerm(terms, keyCode.ToStringReadable());
            AddEnumNameSearchTerms(terms, keyCode.ToString());

            switch (keyCode)
            {
                case KeyCode.None:
                    AddKeySearchTerm(terms, "none");
                    AddKeySearchTerm(terms, "unbound");
                    break;
                case KeyCode.Return:
                    AddKeySearchTerm(terms, "enter");
                    break;
                case KeyCode.KeypadEnter:
                    AddKeySearchTerm(terms, "enter");
                    AddKeySearchTerm(terms, "keypad");
                    break;
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                    AddKeySearchTerm(terms, "ctrl");
                    AddKeySearchTerm(terms, "control");
                    break;
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                    AddKeySearchTerm(terms, "shift");
                    break;
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    AddKeySearchTerm(terms, "alt");
                    break;
                case KeyCode.LeftMeta:
                case KeyCode.RightMeta:
                case KeyCode.LeftWindows:
                case KeyCode.RightWindows:
                    AddKeySearchTerm(terms, "meta");
                    AddKeySearchTerm(terms, "command");
                    AddKeySearchTerm(terms, "cmd");
                    AddKeySearchTerm(terms, "win");
                    AddKeySearchTerm(terms, "windows");
                    break;
                case KeyCode.PageDown:
                    AddKeySearchTerm(terms, "page");
                    AddKeySearchTerm(terms, "down");
                    break;
                case KeyCode.PageUp:
                    AddKeySearchTerm(terms, "page");
                    AddKeySearchTerm(terms, "up");
                    break;
            }

            return terms;
        }

        private static void AddKeySearchTerm(HashSet<string> terms, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                terms.Add(value);
            }
        }

        private static void AddEnumNameSearchTerms(HashSet<string> terms, string enumName)
        {
            AddKeySearchTerm(terms, enumName);
            if (string.IsNullOrEmpty(enumName))
            {
                return;
            }

            StringBuilder current = new StringBuilder();
            for (int i = 0; i < enumName.Length; i++)
            {
                char character = enumName[i];
                bool startsNewWord = i > 0 && char.IsUpper(character) && (char.IsLower(enumName[i - 1]) || (i + 1 < enumName.Length && char.IsLower(enumName[i + 1])));
                if (startsNewWord && current.Length > 0)
                {
                    AddKeySearchTerm(terms, current.ToString());
                    current.Clear();
                }

                current.Append(character);
            }

            if (current.Length > 0)
            {
                AddKeySearchTerm(terms, current.ToString());
            }
        }

        private static bool MatchesText(string value, string normalizedQuery)
        {
            return Normalize(value).Contains(normalizedQuery);
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }

        private static float CalculateContentHeight(List<KeyBindingDef> keyBindings)
        {
            float totalHeight = 0f;
            KeyBindingCategoryDef currentCategory = null;

            for (int i = 0; i < keyBindings.Count; i++)
            {
                KeyBindingDef keyBindingDef = keyBindings[i];
                if (currentCategory != keyBindingDef.category)
                {
                    currentCategory = keyBindingDef.category;
                    totalHeight += 44f;
                }

                totalHeight += 34f;
            }

            return Mathf.Max(totalHeight, 1f);
        }

        private static bool HasActiveSearch()
        {
            return Normalize(activeSearchState.Query).Length > 0;
        }

        private static bool ShouldHighlightCategory(KeyBindingCategoryDef category)
        {
            if (!HasActiveSearch())
            {
                return false;
            }

            SearchState state = activeSearchState;
            string normalizedQuery = Normalize(state.Query);
            if (normalizedQuery.Length == 0)
            {
                return false;
            }

            if (state.Mode == SearchMode.Category)
            {
                return MatchesText(category.LabelCap, normalizedQuery) || MatchesText(category.defName, normalizedQuery);
            }

            return false;
        }

        private static bool ShouldHighlightKeyLabel(Dialog_KeyBindings dialog, KeyBindingDef keyBindingDef)
        {
            if (!HasActiveSearch() || activeSearchState.Mode != SearchMode.Action)
            {
                return false;
            }

            return Matches(dialog, keyBindingDef);
        }

        private static bool ShouldHighlightKeyBinding(Dialog_KeyBindings dialog)
        {
            if (!HasActiveSearch())
            {
                return false;
            }

            return activeSearchState.Mode == SearchMode.Key && ReferenceEquals(activeDialog, dialog);
        }

        private static void DrawMatchedTextHighlight(Rect rect, string text, string query, GameFont font, bool centered, bool verticallyCentered, float trimBottom, bool flatFill)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            GameFont previousFont = Text.Font;
            Text.Font = font;

            Vector2 fullSize = Text.CalcSize(text);
            float startX = centered ? rect.x + (rect.width - fullSize.x) / 2f : rect.x;
            float startY = verticallyCentered ? rect.y + (rect.height - fullSize.y) / 2f : rect.y;
            float highlightHeight = Mathf.Max(1f, fullSize.y - trimBottom);

            if (TryFindDirectMatch(text, query, out int matchIndex, out int matchLength))
            {
                string prefix = text.Substring(0, matchIndex);
                string match = text.Substring(matchIndex, matchLength);
                float prefixWidth = Text.CalcSize(prefix).x;
                float matchWidth = Text.CalcSize(match).x;
                DrawHighlightRect(new Rect(startX + prefixWidth, startY, matchWidth, highlightHeight), flatFill);
            }
            else
            {
                DrawHighlightRect(new Rect(startX, startY, fullSize.x, highlightHeight), flatFill);
            }

            Text.Font = previousFont;
        }

        private static void DrawHighlightRect(Rect rect, bool flatFill)
        {
            if (flatFill)
            {
                Widgets.DrawBoxSolid(rect, FlatTextHighlightColor);
                return;
            }

            Widgets.DrawTextHighlight(rect, 0f, TextHighlightColor);
        }

        private static bool TryFindDirectMatch(string text, string query, out int matchIndex, out int matchLength)
        {
            query = query?.Trim();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
            {
                matchIndex = -1;
                matchLength = 0;
                return false;
            }

            matchIndex = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                matchLength = 0;
                return false;
            }

            matchLength = query.Length;
            return true;
        }

        private static string GetSearchModeLabel(SearchMode mode)
        {
            switch (mode)
            {
                case SearchMode.Action:
                    return "KeybindSearch.SearchModeAction".Translate();
                case SearchMode.Key:
                    return "KeybindSearch.SearchModeKey".Translate();
                case SearchMode.Category:
                    return "KeybindSearch.SearchModeCategory".Translate();
                default:
                    return mode.ToString();
            }
        }

        private sealed class SearchState
        {
            public string Query = string.Empty;

            public SearchMode Mode = SearchMode.Action;
        }

        private enum SearchMode
        {
            Action,
            Key,
            Category
        }
    }
}
