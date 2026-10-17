using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SubSkillsDisplay
{
    internal static class UiHierarchy
    {
        /// <summary>
        /// The component in <paramref name="copyRoot"/> at the same place as <paramref name="original"/> is in
        /// <paramref name="originalRoot"/>, or null if <paramref name="original"/> is not inside it.
        /// </summary>
        internal static T Counterpart<T>(Transform originalRoot, Transform copyRoot, T original) where T : Component
        {
            GameObject counterpart = original ? Counterpart(originalRoot, copyRoot, original.gameObject) : null;
            return counterpart ? counterpart.GetComponent<T>() : null;
        }

        internal static GameObject Counterpart(Transform originalRoot, Transform copyRoot, GameObject original)
        {
            if (!original)
            {
                return null;
            }

            List<int> path = new List<int>();
            for (Transform t = original.transform; t != originalRoot; t = t.parent)
            {
                if (!t)
                {
                    return null;
                }

                path.Insert(0, t.GetSiblingIndex());
            }

            Transform copy = copyRoot;
            foreach (int index in path)
            {
                if (index >= copy.childCount)
                {
                    return null;
                }

                copy = copy.GetChild(index);
            }

            return copy.gameObject;
        }

        /// <summary>
        /// A paragraph in <paramref name="template"/>'s font (a skill description's), sized by its parent's
        /// layout to fit its text. With <paramref name="wrapWidth"/>, it asks its layout for that width rather
        /// than its whole text on one line (which would widen a parent that fits its children).
        /// </summary>
        internal static TMP_Text AddText(TMP_Text template, Transform parent, string name, float wrapWidth = -1f)
        {
            GameObject copy = Object.Instantiate(template.gameObject, parent, worldPositionStays: false);
            copy.name = name;
            copy.SetActive(true);

            TMP_Text text = copy.GetComponent<TMP_Text>();
            text.enableAutoSizing = false;
            text.fontSize = Mathf.Min(template.fontSizeMax > 0f ? template.fontSizeMax : template.fontSize, 20f);
            text.enableWordWrapping = true;

            LayoutElement layout = copy.GetComponent<LayoutElement>();
            if (!layout && wrapWidth > 0f)
            {
                layout = copy.AddComponent<LayoutElement>();
            }

            if (layout)
            {
                layout.ignoreLayout = false;
                layout.minHeight = layout.preferredHeight = layout.flexibleHeight = -1f;
                layout.preferredWidth = wrapWidth > 0f ? wrapWidth : -1f;
            }

            return text;
        }
    }
}
