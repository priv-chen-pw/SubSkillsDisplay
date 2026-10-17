using System.Linq;
using System.Text.RegularExpressions;
using RoR2;
using RoR2.Skills;
using UnityEngine;

namespace SubSkillsDisplay
{
    /// <summary>One skill in a line: its icon, and the text shown when hovering it.</summary>
    internal readonly struct SkillEntry
    {
        internal readonly Sprite icon;
        private readonly string nameToken;
        private readonly string sourceToken;
        private readonly string descriptionToken;
        private readonly string[] keywordTokens;

        private SkillEntry(Sprite icon, string nameToken, string sourceToken, string descriptionToken, string[] keywordTokens)
        {
            this.icon = icon;
            this.nameToken = nameToken;
            this.sourceToken = sourceToken;
            this.descriptionToken = descriptionToken;
            this.keywordTokens = keywordTokens;
        }

        internal static SkillEntry Of(SkillDef skill) =>
            new SkillEntry(skill.icon, skill.skillNameToken, null, skill.skillDescriptionToken, skill.keywordTokens);

        internal static SkillEntry Of(ExtraSkill extra) =>
            new SkillEntry(extra.skill.icon, extra.skill.skillNameToken, extra.sourceToken, extra.descriptionToken, extra.skill.keywordTokens);

        internal static SkillEntry Of(SkillLocator.PassiveSkill passive) =>
            new SkillEntry(passive.icon, passive.skillNameToken, null, passive.skillDescriptionToken,
                string.IsNullOrEmpty(passive.keywordToken) ? null : new[] { passive.keywordToken });

        /// <summary>The skill's name, then what gives it.</summary>
        internal string Title
        {
            get
            {
                string name = Language.GetString(nameToken);
                string source = string.IsNullOrEmpty(sourceToken) ? null : Language.GetString(sourceToken);
                return string.IsNullOrEmpty(source) || source == name ? name : $"{name} ({source})";
            }
        }

        private static readonly Regex keywordTitle = new Regex("<style=cKeywordName>.*?</style>", RegexOptions.Singleline);
        private static readonly Regex subduedBody = new Regex("^<style=cSub>(.*)</style>$", RegexOptions.Singleline);

        /// <summary>
        /// The description, which may be a keyword tooltip (Void Fiend's corrupted skills): its title is dropped
        /// and its grey body kept as plain text.
        /// </summary>
        internal string Description
        {
            get
            {
                string text = keywordTitle.Replace(Language.GetString(descriptionToken), string.Empty).Trim();
                Match body = subduedBody.Match(text);
                return body.Success ? body.Groups[1].Value : text;
            }
        }

        /// <summary>
        /// The hover text: title and description in the Loadout tab's format, then the keyword explanations the
        /// Skills tab shows.
        /// </summary>
        internal string HoverText(Color color)
        {
            string description = Description;
            color.a = 0.2f;
            string text = Language.GetStringFormatted("LOGBOOK_HOVER_DESCRIPTION_FORMAT", Title, description, ColorUtility.ToHtmlStringRGBA(color));
            string[] keywords = Keywords.For(keywordTokens, description).Select(Language.GetString).ToArray();
            return keywords.Length == 0 ? text : text + "\n\n" + string.Join("\n\n", keywords);
        }
    }
}
