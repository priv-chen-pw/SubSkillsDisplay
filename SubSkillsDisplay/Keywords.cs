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

        /// <summary>Keyword token by its name in the current language; a name several keywords share maps to null.</summary>
        private static Dictionary<string, string> tokenByName;
        private static string builtForLanguage;

        /// <summary>
        /// <paramref name="ownTokens"/>, then the keywords whose names the description highlights (the game
        /// styles keywords it mentions, so "heavy projectile" doesn't count as the Heavy keyword).
        /// </summary>
        internal static List<string> For(string[] ownTokens, string description)
        {
            Build();
            List<string> found = (ownTokens ?? Array.Empty<string>()).Where(token => !string.IsNullOrEmpty(token)).Distinct().ToList();
            foreach (Match span in highlighted.Matches(description ?? string.Empty))
            {
                string text = tags.Replace(span.Groups[1].Value, string.Empty);
                foreach (KeyValuePair<string, string> keyword in tokenByName)
                {
                    if (keyword.Value != null && !found.Contains(keyword.Value)
                        && Regex.IsMatch(text, $@"\b{Regex.Escape(keyword.Key)}(s|es|d|ed)?\b", RegexOptions.IgnoreCase))
                    {
                        found.Add(keyword.Value);
                    }
                }
            }

            return found;
        }

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
