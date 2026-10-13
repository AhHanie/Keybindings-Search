using HarmonyLib;
using Verse;

namespace Keybindings_Search
{
    public class Mod : Verse.Mod
    {
        public Mod(ModContentPack content) : base(content)
        {
            new Harmony("sk.keybindsearch").PatchAll();
        }
    }
}
