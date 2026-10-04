using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DeskrawlQoL.Core;

/// <summary>
/// Helpers for adding controls to the game's own uGUI. Cloned game controls look native and, being real
/// game UI, are clickable in overlay mode without <see cref="ClickThrough"/>.
/// Never use AddListener on game UI events here (delegate conversion crashes this Unity build); poll state instead.
/// </summary>
internal static class GameUi
{
    /// <summary>A cloned game toggle and its label.</summary>
    public sealed class ClonedToggle
    {
        public Toggle Toggle;
        public TMP_Text Label;
        public TMP_Text OriginalLabel; // the game's label we copy the font size from

        public bool Live => Toggle != null && !Toggle.WasCollected;

        /// <summary>
        /// Sets the label text and matches the original label's font size. Game labels auto-size to fit
        /// their box, so a longer text would otherwise come out smaller; and the original's size is only
        /// known once the game has drawn it, so this is re-checked every call.
        /// </summary>
        public void SetText(string text)
        {
            if (Label == null || Label.WasCollected) return;
            if (Label.text != text) Label.text = text;
            if (OriginalLabel != null && !OriginalLabel.WasCollected && OriginalLabel.fontSize > 0f
                && !Mathf.Approximately(Label.fontSize, OriginalLabel.fontSize))
                Label.fontSize = OriginalLabel.fontSize;
        }
    }

    /// <summary>
    /// Clones a game toggle (or its row, if the label sits next to the toggle rather than inside it) and
    /// places it directly above or below the original. Reuses an existing clone with the same name.
    /// The clone is detached from everything the original toggle triggers.
    /// </summary>
    public static ClonedToggle CloneToggle(Toggle original, string name, bool above)
    {
        if (original == null) return null;

        // Clone the row when the toggle has no text of its own but a small parent containing just it and a label.
        Transform source = original.transform;
        var row = source.parent;
        if (Texts(source).Count == 0 && row != null && row.parent != null && Texts(row).Count > 0
            && row.GetComponentsInChildren(Il2CppType.Of<Selectable>(), true).Length == 1)
            source = row;

        var container = source.parent;
        var existing = container.Find(name);
        GameObject go;
        if (existing != null) go = existing.gameObject;
        else
        {
            go = Object.Instantiate(source.gameObject.Cast<Object>(), container).Cast<GameObject>();
            go.name = name;
            Place(go.transform.Cast<RectTransform>(), source.Cast<RectTransform>(), container, above);
        }

        var toggle = go.GetComponentInChildren(Il2CppType.Of<Toggle>(), true).Cast<Toggle>();
        toggle.onValueChanged.RemoveAllListeners();
        for (int i = 0; i < toggle.onValueChanged.GetPersistentEventCount(); i++)
            toggle.onValueChanged.SetPersistentListenerState(i, UnityEventCallState.Off);
        toggle.group = null;

        // Keep one label; hide countdowns, hint icons and other extra text the original carried.
        var texts = Texts(go.transform);
        var label = texts.FirstOrDefault(t => !t.name.ToLowerInvariant().Contains("count")) ?? texts.FirstOrDefault();
        foreach (var t in texts)
            if (t.Pointer != label?.Pointer) t.gameObject.SetActive(false);
        foreach (var tr in go.GetComponentsInChildren(Il2CppType.Of<Transform>(), true).Select(c => c.Cast<Transform>()))
            if (tr.name.ToLowerInvariant().Contains("hint")) tr.gameObject.SetActive(false);

        // Fixed font size (matched to the original in SetText); let longer text run past the box instead of shrinking.
        TMP_Text originalLabel = null;
        if (label != null)
        {
            string labelName = label.name;
            originalLabel = Texts(source).FirstOrDefault(t => t.name == labelName);
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        Plugin.L.LogInfo($"Cloned toggle '{original.name}' as '{name}' under '{container.name}'" +
            $" (cloned {(source == original.transform ? "toggle" : "row '" + source.name + "'")}, " +
            $"layout group: {container.GetComponent(Il2CppType.Of<LayoutGroup>()) != null}, texts: {string.Join(", ", texts.Select(t => t.name))}).");
        return new ClonedToggle { Toggle = toggle, Label = label, OriginalLabel = originalLabel };
    }

    private static void Place(RectTransform clone, RectTransform original, Transform container, bool above)
    {
        int index = original.GetSiblingIndex();
        clone.SetSiblingIndex(above ? index : index + 1);
        // Inside a layout group the sibling index places it; otherwise offset it by the original's height.
        if (container.GetComponent(Il2CppType.Of<LayoutGroup>()) == null)
            clone.anchoredPosition = original.anchoredPosition + new Vector2(0f, (original.rect.height + 4f) * (above ? 1f : -1f));
    }

    private static List<TMP_Text> Texts(Transform t) =>
        t.GetComponentsInChildren(Il2CppType.Of<TMP_Text>(), true).Select(c => c.Cast<TMP_Text>()).ToList();
}
