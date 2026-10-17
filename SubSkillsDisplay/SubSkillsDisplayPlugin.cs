using BepInEx;

namespace SubSkillsDisplay
{
    /// <summary>
    /// Shows the skills a survivor can use that character select's Skills tab leaves out (transformations' skills,
    /// recasts, boosted and corrupted versions): in an Extra Skills tab in character select, and under Abilities in
    /// the pause menu during a run.
    /// </summary>
    // Only the player's own screens change: nothing is networked, so players without the mod are unaffected and
    // no network compatibility is declared.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SubSkillsDisplayPlugin : BaseUnityPlugin
    {
        public const string PluginAuthor = "chen-ror2";
        public const string PluginName = "SubSkillsDisplay";
        public const string PluginGuid = PluginAuthor + "." + PluginName;
        public const string PluginVersion = "1.1.4";

        public void Awake()
        {
            Log.Init(Logger);

            ExtraSkillCatalog.includeModdedSurvivors = Config.Bind("General", "ModdedSurvivors", true,
                "Also lists extra skills of survivors from other mods: skills their mod swaps in that no loadout offers. Off, only the vanilla survivors' are listed.").Value;
            bool logExtraSkills = Config.Bind("Debug", "LogExtraSkills", false,
                "Logs every unlocked survivor's extra skills, across all unlocked loadouts, the first time character select opens.").Value;
            bool logHierarchy = Config.Bind("Debug", "LogCharacterSelectHierarchy", false,
                "Logs character select's left-hand panel tree once, to check the Extra Skills tab's layout.").Value;

            ExtraSkillsTab.Init(logExtraSkills);
            PauseAbilitiesPanel.Init();
            if (logHierarchy)
            {
                CharacterSelectDump.Init();
            }

            Log.Info($"{PluginName} {PluginVersion} ready.");
        }
    }
}
