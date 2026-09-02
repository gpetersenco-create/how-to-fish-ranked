using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HowToFish1v1
{
    /// <summary>Testing only (config Debug/DumpMenu): logs the main menu UI hierarchy once so the Ranked button can be cloned from a real one.</summary>
    internal static class DebugMenuDump
    {
        private static bool _done;

        public static void Update()
        {
            if (_done || !Plugin.Cfg.DumpMenu.Value || Time.time < 8f || !MainMenuManager.IsInMenu) return;
            _done = true;
            var mm = Object.FindObjectOfType<MainMenuManager>();
            if (!mm) { Plugin.Log.LogWarning("MenuDump: no MainMenuManager"); return; }
            var sb = new StringBuilder();
            sb.AppendLine("MenuDump: MainMenuManager fields:");
            foreach (var f in AccessTools.GetDeclaredFields(typeof(MainMenuManager)))
            {
                var v = f.GetValue(mm);
                sb.AppendLine($"  {f.FieldType.Name} {f.Name} = {(v is Object o && o ? o.name : v?.ToString() ?? "null")}");
            }
            foreach (var btn in Object.FindObjectsOfType<Button>(true))
            {
                var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
                string text = tmp ? tmp.text.Replace("\n", " ") : "";
                if (string.IsNullOrEmpty(text)) continue;
                var rt = btn.transform as RectTransform;
                sb.AppendLine($"  Button '{btn.name}' text='{text}' path={Path(btn.transform)} active={btn.gameObject.activeInHierarchy} anchoredPos={(rt ? rt.anchoredPosition.ToString() : "?")} size={(rt ? rt.sizeDelta.ToString() : "?")} parentLayout={(btn.transform.parent && btn.transform.parent.GetComponent<LayoutGroup>() ? btn.transform.parent.GetComponent<LayoutGroup>().GetType().Name : "none")}");
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }
    }
}
