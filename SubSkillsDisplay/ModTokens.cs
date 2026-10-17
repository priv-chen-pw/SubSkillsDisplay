using RoR2;

namespace SubSkillsDisplay
{
    /// <summary>
    /// The mod's own language tokens. They are set on English, which every other language falls back to, so the
    /// labels read the same everywhere.
    /// </summary>
    internal static class ModTokens
    {
        internal const string ExtraTab = "SUBSKILLSDISPLAY_TAB_EXTRA";
        internal const string ExtraIntro = "SUBSKILLSDISPLAY_EXTRA_INTRO";
        internal const string ExtraNone = "SUBSKILLSDISPLAY_EXTRA_NONE";
        internal const string PauseAbilities = "SUBSKILLSDISPLAY_PAUSE_ABILITIES";
        internal const string PauseIntro = "SUBSKILLSDISPLAY_PAUSE_INTRO";
        internal const string PassiveLine = "SUBSKILLSDISPLAY_LINE_PASSIVE";

        /// <summary>Vanilla's "Back", as on the Settings panel's back button.</summary>
        internal const string Back = "SUBMENU_BACK";

        /// <summary>Safe to call repeatedly; does nothing until the languages are loaded.</summary>
        internal static void Register()
        {
            Language english = Language.english;
            if (english == null)
            {
                return;
            }

            english.SetStringByToken(ExtraTab, "Extra Skills");
            english.SetStringByToken(ExtraIntro,
                "<style=cSub>What this survivor's skills turn into during a run, in the line of the skill they replace. Hover one for details. Skills from choices you haven't unlocked are hidden.</style>");
            english.SetStringByToken(ExtraNone,
                "<style=cSub>This survivor has no extra skills: everything it uses is on the Skills tab.</style>");
            english.SetStringByToken(PauseAbilities, "Abilities");
            english.SetStringByToken(PauseIntro,
                "<style=cSub>Your skills, each followed by what it turns into during the run. Hover one for details.</style>");
            english.SetStringByToken(PassiveLine, "Passive");
        }
    }
}
