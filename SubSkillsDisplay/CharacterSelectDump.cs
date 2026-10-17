using System.Collections;
using System.Text;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SubSkillsDisplay
{
    /// <summary>
    /// Logs character select's left-hand panel tree once, after the Extra tab is added, so layout problems can
    /// be worked out from the log against the real hierarchy. Off unless enabled in the config.
    /// </summary>
    internal static class CharacterSelectDump
    {
        private static bool logged;

        internal static void Init()
        {
            On.RoR2.UI.CharacterSelectController.Awake += (orig, self) =>
            {
                orig(self);
                if (!logged)
                {
                    self.StartCoroutine(DumpAfterLayout(self));
                }
            };
        }

        /// <summary>Waits until the screen has been laid out, so the logged sizes are real.</summary>
        private static IEnumerator DumpAfterLayout(CharacterSelectController controller)
        {
            yield return new WaitForSecondsRealtime(2f);
            if (logged || !controller || !controller.skillStripContainer)
            {
                yield break;
            }

            logged = true;

            // From the panel holding the tab bar and the tab contents.
            Transform root = controller.skillStripContainer;
            for (int i = 0; i < 2 && root.parent; i++)
            {
                root = root.parent;
            }

            StringBuilder sb = new StringBuilder("Character select hierarchy:\n");
            Append(sb, root, 0);
            Log.Info(sb.ToString());
        }

        private static void Append(StringBuilder sb, Transform transform, int depth)
        {
            sb.Append(' ', depth * 2).Append("- ").Append(transform.name);
            if (!transform.gameObject.activeSelf)
            {
                sb.Append(" (inactive)");
            }

            if (transform is RectTransform rect)
            {
                sb.Append("  rect=").Append(Mathf.Round(rect.rect.width)).Append('x').Append(Mathf.Round(rect.rect.height));
            }

            LayoutElement layoutElement = transform.GetComponent<LayoutElement>();
            if (layoutElement)
            {
                sb.Append("  LE(prefW=").Append(layoutElement.preferredWidth)
                  .Append(",prefH=").Append(layoutElement.preferredHeight)
                  .Append(",ignore=").Append(layoutElement.ignoreLayout).Append(')');
            }

            sb.Append('\n');
            for (int i = 0; i < transform.childCount; i++)
            {
                Append(sb, transform.GetChild(i), depth + 1);
            }
        }
    }
}
