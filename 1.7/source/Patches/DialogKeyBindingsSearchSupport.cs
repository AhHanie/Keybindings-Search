using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace Keybindings_Search.Patches
{
    public static class DialogKeyBindingsSearchSupport
    {
        public const float TitleHeight = 40f;
        public const float EntryHeight = 34f;

        private const float CategoryBlockHeight = 44f;
        private const float SearchRowHeight = 36f;
        private const float SearchRowGap = 8f;
        private const float SearchControlGap = 8f;
        private const float SearchModeWidth = 150f;
        private const string SearchFieldControlName = "KeybindSearchField";
        private static readonly Color TextHighlightColor = new Color(1f, 0.9f, 0.2f, 0.45f);
        private static readonly Color FlatTextHighlightColor = new Color(1f, 0.9f, 0.2f, 0.35f);

        private static Dialog_KeyBindings activeDialog;
        private static SearchState activeSearchState = new SearchState();

        public static float SearchRowReserve => SearchRowGap + SearchRowHeight + SearchRowGap;

        public static void DrawSearchControls(Dialog_KeyBindings dialog, KeyPrefsData keyPrefsData, ref Vector2 scrollPosition, Rect headerRect)
        {
            SearchState state = GetState(dialog);
            ReleaseFocusWhileCapturingKey();

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect rowRect = new Rect(headerRect.x, headerRect.y + TitleHeight + SearchRowGap, headerRect.width, SearchRowHeight);
            float modeWidth = Mathf.Min(SearchModeWidth, Mathf.Max(120f, rowRect.width * 0.28f));
            Rect textRect = new Rect(rowRect.x, rowRect.y, Mathf.Max(0f, rowRect.width - modeWidth - SearchControlGap), rowRect.height);
            Rect modeRect = new Rect(textRect.xMax + SearchControlGap, rowRect.y, modeWidth, rowRect.height);

            GUI.SetNextControlName(SearchFieldControlName);
            state.Query = Widgets.TextField(textRect, state.Query ?? string.Empty);
            DrawPlaceholderIfNeeded(textRect, state.Query);

            Widgets.Dropdown(modeRect, dialog, GetSearchMode, GenerateSearchModeOptions, GetSearchModeLabel(state.Mode));

            Text.Anchor = previousAnchor;
            Text.Font = previousFont;

            if (state.AppliedQuery != state.Query || state.AppliedMode != state.Mode)
            {
                scrollPosition = Vector2.zero;
                state.AppliedQuery = state.Query;
                state.AppliedMode = state.Mode;
            }

            RefreshMatches(state, keyPrefsData);
        }

        public static float GetContentHeight(Dialog_KeyBindings dialog, float vanillaHeight)
        {
            return IsSearching(dialog) ? activeSearchState.ContentHeight : vanillaHeight;
        }

        public static IEnumerable<KeyBindingDef> GetBindings(Dialog_KeyBindings dialog, IEnumerable<KeyBindingDef> vanillaBindings)
        {
            return IsSearching(dialog) ? activeSearchState.Matches : vanillaBindings;
        }

        // Once the key capture dialog is up it must be the only thing that can hold keyboard focus. The engine routes
        // key events to the window that owns the focused control and withholds ordinary game keys while a text field
        // has focus, so a focused search field would leave the capture dialog waiting for a key it never receives.
        private static void ReleaseFocusWhileCapturingKey()
        {
            if (GUI.GetNameOfFocusedControl() == SearchFieldControlName && Find.WindowStack.IsOpen<Dialog_DefineBinding>())
            {
                UI.UnfocusCurrentControl();
            }
        }

        private static void RefreshMatches(SearchState state, KeyPrefsData keyPrefsData)
        {
            state.NormalizedQuery = Normalize(state.Query);
            state.Matches.Clear();
            state.MatchedCategories.Clear();
            state.ContentHeight = 0f;
            if (state.NormalizedQuery.Length == 0)
            {
                return;
            }

            foreach (KeyBindingDef keyBindingDef in DefDatabase<KeyBindingDef>.AllDefs)
            {
                if (Matches(state, keyPrefsData, keyBindingDef))
                {
                    state.Matches.Add(keyBindingDef);
                    state.MatchedCategories.Add(keyBindingDef.category);
                }
            }

            state.ContentHeight = Mathf.Max(state.MatchedCategories.Count * CategoryBlockHeight + state.Matches.Count * EntryHeight, 1f);
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

        private static bool IsSearching(Dialog_KeyBindings dialog)
        {
            return ReferenceEquals(activeDialog, dialog) && activeSearchState.NormalizedQuery.Length > 0;
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

        public static void DrawCategoryHighlight(Dialog_KeyBindings dialog, KeyBindingCategoryDef category, float width, float curY, bool skipDrawing)
        {
            if (skipDrawing || !ShouldHighlightCategory(dialog, category))
            {
                return;
            }

            Rect rect = new Rect(0f, curY, width, 40f).ContractedBy(4f);
            DrawMatchedTextHighlight(rect, category.LabelCap, activeSearchState.Query, GameFont.Medium, centered: false, verticallyCentered: true, trimBottom: 2f, flatFill: true);
        }

        public static void DrawKeyLabelHighlight(Dialog_KeyBindings dialog, KeyBindingDef keyBindingDef, Rect parentRect, float curY, bool skipDrawing)
        {
            if (skipDrawing || !IsSearching(dialog) || activeSearchState.Mode != SearchMode.Action)
            {
                return;
            }

            Rect rect = new Rect(parentRect.x, parentRect.y + curY, parentRect.width, EntryHeight).ContractedBy(3f);
            DrawMatchedTextHighlight(rect, keyBindingDef.LabelCap, activeSearchState.Query, GameFont.Small, centered: false, verticallyCentered: true, trimBottom: 0f, flatFill: false);
        }

        public static void DrawKeyBindingHighlight(Dialog_KeyBindings dialog, KeyPrefsData keyPrefsData, KeyBindingDef keyBindingDef, Rect parentRect, float curY, bool skipDrawing)
        {
            if (skipDrawing || !IsSearching(dialog) || activeSearchState.Mode != SearchMode.Key)
            {
                return;
            }

            Rect rect = new Rect(parentRect.x, parentRect.y + curY, parentRect.width, EntryHeight).ContractedBy(3f);
            float gap = 4f;
            Vector2 buttonSize = new Vector2(140f, 28f);
            Rect rectA = new Rect(rect.x + rect.width - buttonSize.x * 2f - gap, rect.y, buttonSize.x, buttonSize.y);
            Rect rectB = new Rect(rect.x + rect.width - buttonSize.x, rect.y, buttonSize.x, buttonSize.y);

            KeyCode keyA = keyPrefsData.GetBoundKeyCode(keyBindingDef, KeyPrefs.BindingSlot.A);
            KeyCode keyB = keyPrefsData.GetBoundKeyCode(keyBindingDef, KeyPrefs.BindingSlot.B);

            if (MatchesKeyCode(keyA, activeSearchState.NormalizedQuery))
            {
                DrawMatchedTextHighlight(rectA, keyA.ToStringReadable(), activeSearchState.Query, GameFont.Small, centered: true, verticallyCentered: true, trimBottom: 0f, flatFill: false);
            }

            if (MatchesKeyCode(keyB, activeSearchState.NormalizedQuery))
            {
                DrawMatchedTextHighlight(rectB, keyB.ToStringReadable(), activeSearchState.Query, GameFont.Small, centered: true, verticallyCentered: true, trimBottom: 0f, flatFill: false);
            }
        }

        private static bool Matches(SearchState state, KeyPrefsData keyPrefsData, KeyBindingDef keyBindingDef)
        {
            string normalizedQuery = state.NormalizedQuery;
            switch (state.Mode)
            {
                case SearchMode.Action:
                    return MatchesText(keyBindingDef.LabelCap, normalizedQuery) || MatchesText(keyBindingDef.defName, normalizedQuery);
                case SearchMode.Key:
                    return MatchesKeyCode(keyPrefsData.GetBoundKeyCode(keyBindingDef, KeyPrefs.BindingSlot.A), normalizedQuery)
                        || MatchesKeyCode(keyPrefsData.GetBoundKeyCode(keyBindingDef, KeyPrefs.BindingSlot.B), normalizedQuery);
                case SearchMode.Category:
                    return MatchesText(keyBindingDef.category.LabelCap, normalizedQuery) || MatchesText(keyBindingDef.category.defName, normalizedQuery);
                default:
                    return true;
            }
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

        private static bool ShouldHighlightCategory(Dialog_KeyBindings dialog, KeyBindingCategoryDef category)
        {
            if (!IsSearching(dialog) || activeSearchState.Mode != SearchMode.Category)
            {
                return false;
            }

            string normalizedQuery = activeSearchState.NormalizedQuery;
            return MatchesText(category.LabelCap, normalizedQuery) || MatchesText(category.defName, normalizedQuery);
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

            public string AppliedQuery = string.Empty;

            public SearchMode AppliedMode = SearchMode.Action;

            public string NormalizedQuery = string.Empty;

            public readonly List<KeyBindingDef> Matches = new List<KeyBindingDef>();

            public readonly HashSet<KeyBindingCategoryDef> MatchedCategories = new HashSet<KeyBindingCategoryDef>();

            public float ContentHeight;
        }

        private enum SearchMode
        {
            Action,
            Key,
            Category
        }
    }
}
