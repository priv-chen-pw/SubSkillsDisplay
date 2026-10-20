using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RoR2;
using RoR2.Skills;

namespace SubSkillsDisplay
{
    /// <summary>
    /// The keyword explanations the Skills tab shows on hover ("Stunning: Interrupts enemies..."). An extra
    /// skill often names a keyword in its description without listing it (Soul Storm stuns, a boosted skill
    /// ignites), so besides a skill's own keywords this finds the keywords its description highlights.
    /// </summary>
    internal static class Keywords
    {
        private static readonly Regex keywordName = new Regex("<style=cKeywordName>(.*?)</style>", RegexOptions.Singleline);
        private static readonly Regex highlighted = new Regex("<style=c[A-Za-z]+>(.*?)</style>", RegexOptions.Singleline);
        private static readonly Regex tags = new Regex("<[^>]+>");

        /// <summary>The X in a cost keyword's name ("X% HP"), standing for the amount.</summary>
        private static readonly Regex amount = new Regex(@"\bX(?=\s*%)|(?<=%\s*)X\b");

        /// <summary>Keyword token by its name in the current language; a name several keywords share maps to null.</summary>
        private static Dictionary<string, string> tokenByName;
        private static string builtForLanguage;

        /// <summary>
        /// <paramref name="ownTokens"/>, then the keywords whose names the description highlights (the game
        /// styles keywords it mentions, so "heavy projectile" doesn't count as the Heavy keyword). A survivor's own
        /// keyword, not a shared KEYWORD_ one, counts only on skills with its token prefix: Drifter's Tornado Slam
        /// "Ricochets off of heavy enemies" without Operator's Ricochet.
        /// </summary>
        internal static List<string> For(string[] ownTokens, string description, string nameToken, string descriptionToken)
        {
            Build();
            List<string> found = (ownTokens ?? Array.Empty<string>())
                .Where(token => !string.IsNullOrEmpty(token) && !IsUnstatedCost(token, descriptionToken))
                .Distinct()
                .ToList();
            string survivor = Prefix(nameToken);
            foreach (Match span in highlighted.Matches(description ?? string.Empty))
            {
                string text = tags.Replace(span.Groups[1].Value, string.Empty);
                foreach (KeyValuePair<string, string> keyword in tokenByName)
                {
                    if (keyword.Value != null && !found.Contains(keyword.Value)
                        && (keyword.Value.StartsWith("KEYWORD_", StringComparison.Ordinal) || Prefix(keyword.Value) == survivor)
                        && Regex.IsMatch(text, $@"\b{Regex.Escape(keyword.Key)}(s|es|d|ed)?\b", RegexOptions.IgnoreCase))
                    {
                        found.Add(keyword.Value);
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Whether the keyword is a cost ("X% HP") the description doesn't state. The game lists X% HP on the
        /// Anima Harvester's skills too, which cost no health; REX's skills state theirs ("15% HP"). Checked in
        /// English, which states every cost that way; some translations of Proliferation's don't.
        /// </summary>
        private static bool IsUnstatedCost(string token, string descriptionToken)
        {
            Match name = keywordName.Match(English(token));
            string title = name.Success ? tags.Replace(name.Groups[1].Value, string.Empty).Trim() : string.Empty;
            Match x = amount.Match(title);
            if (!x.Success)
            {
                return false;
            }

            Regex cost = new Regex(Regex.Escape(title.Substring(0, x.Index)) + @"\d+(\.\d+)?" + Regex.Escape(title.Substring(x.Index + 1)));
            return !highlighted.Matches(English(descriptionToken)).Cast<Match>()
                .Any(span => cost.IsMatch(tags.Replace(span.Groups[1].Value, string.Empty)));
        }

        private static string English(string token) =>
            string.IsNullOrEmpty(token) ? string.Empty : Language.english?.GetLocalizedStringByToken(token) ?? Language.GetString(token);

        /// <summary>A token's first part, which a survivor's tokens share (DRIFTER_, ENFORCER2_).</summary>
        private static string Prefix(string token) => token?.Split('_')[0];

        /// <summary>Indexes every keyword a skill or passive lists, by its name in the current language.</summary>
        private static void Build()
        {
            if (tokenByName != null && builtForLanguage == Language.currentLanguageName)
            {
                return;
            }

            builtForLanguage = Language.currentLanguageName;
            tokenByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> tokens = SkillCatalog.allSkillDefs
                .Where(skill => skill && skill.keywordTokens != null)
                .SelectMany(skill => skill.keywordTokens)
                .Concat(BodyCatalog.allBodyPrefabBodyBodyComponents
                    .Where(body => body && body.GetComponent<SkillLocator>())
                    .Select(body => body.GetComponent<SkillLocator>().passiveSkill.keywordToken))
                .Where(token => !string.IsNullOrEmpty(token))
                .Distinct();

            foreach (string token in tokens)
            {
                Match match = keywordName.Match(Language.GetString(token));
                string name = match.Success ? tags.Replace(match.Groups[1].Value, string.Empty).Trim() : null;
                if (string.IsNullOrEmpty(name) || name.Length < 3)
                {
                    continue;
                }

                if (!tokenByName.TryGetValue(name, out string existing))
                {
                    tokenByName[name] = token;
                }
                else if (existing != null && existing != token)
                {
                    // Several keywords share the name (each CHEF skill's "Boost"): keep the shared KEYWORD_ one if
                    // exactly one of them is, otherwise none.
                    bool existingShared = existing.StartsWith("KEYWORD_", StringComparison.Ordinal);
                    bool tokenShared = token.StartsWith("KEYWORD_", StringComparison.Ordinal);
                    tokenByName[name] = existingShared == tokenShared ? null : existingShared ? existing : token;
                }
            }
        }
    }
}
