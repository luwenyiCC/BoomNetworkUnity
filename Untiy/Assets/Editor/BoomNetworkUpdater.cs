using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

/// <summary>
/// BoomNetwork UPM 强制更新工具
///
/// Editor 菜单: BoomNetwork / Update Packages
/// CLI 用法 (Unity -batchmode):
///   -executeMethod BoomNetworkUpdater.UpdateAll
///   -executeMethod BoomNetworkUpdater.UpdateCore
///   -executeMethod BoomNetworkUpdater.UpdateGM
/// </summary>
public class BoomNetworkUpdater : EditorWindow
{
    const string PkgCore = "com.boom.boomnetwork";
    const string PkgGM   = "com.boom.boomnetwork.gm";

    // ── Menu ─────────────────────────────────────────────────────────────

    [MenuItem("BoomNetwork/Update Packages")]
    static void OpenWindow()
    {
        var win = GetWindow<BoomNetworkUpdater>("BoomNetwork Updater");
        win.minSize = new Vector2(340, 160);
        win.Show();
    }

    // ── GUI ──────────────────────────────────────────────────────────────

    void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("BoomNetwork UPM Updater", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "删除 packages-lock.json 中对应条目，强制 Unity 从 GitHub 拉取最新 commit。",
            MessageType.Info);
        EditorGUILayout.Space(8);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Update Core", GUILayout.Height(32)))
                RunWithConfirm(PkgCore);

            if (GUILayout.Button("Update GM", GUILayout.Height(32)))
                RunWithConfirm(PkgGM);
        }

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Update Both", GUILayout.Height(36)))
            RunWithConfirm(PkgCore, PkgGM);
    }

    static void RunWithConfirm(params string[] packages)
    {
        string list = string.Join("\n  • ", packages);
        bool ok = EditorUtility.DisplayDialog(
            "BoomNetwork Updater",
            $"将强制重新拉取以下包：\n  • {list}\n\nUnity 会短暂编译，确认继续？",
            "Update", "Cancel");
        if (ok)
            ForceUpdate(packages);
    }

    // ── Core logic ───────────────────────────────────────────────────────

    /// <summary>从 packages-lock.json 中移除指定包的 hash，触发 Unity 重新解析。</summary>
    static void ForceUpdate(string[] packages)
    {
        string lockPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../Packages/packages-lock.json"));

        if (!File.Exists(lockPath))
        {
            Debug.LogError($"[BoomNetworkUpdater] 找不到 {lockPath}");
            return;
        }

        string json = File.ReadAllText(lockPath);
        var removedList = new List<string>();

        foreach (string pkg in packages)
        {
            string before = json;
            json = RemovePackageEntry(json, pkg);
            if (json != before)
                removedList.Add(pkg);
            else
                Debug.LogWarning($"[BoomNetworkUpdater] {pkg} 不在 lock 文件中，跳过。");
        }

        if (removedList.Count == 0)
        {
            Debug.Log("[BoomNetworkUpdater] 无需更新（包条目已不存在）。");
            return;
        }

        File.WriteAllText(lockPath, json);
        Debug.Log($"[BoomNetworkUpdater] 已移除 lock 条目：{string.Join(", ", removedList)}");

        // 触发 PackageManager 重新解析
        Client.Resolve();
        Debug.Log("[BoomNetworkUpdater] PackageManager.Client.Resolve() 已触发，请等待编译完成。");
    }

    /// <summary>
    /// 从 JSON 文本中删除 "pkgName": { ... } 块（简单字符串处理，不引入 JSON 库）。
    /// </summary>
    static string RemovePackageEntry(string json, string pkg)
    {
        // 找到 "com.xxx.xxx": {
        string key = $"\"{pkg}\"";
        int keyIdx = json.IndexOf(key, StringComparison.Ordinal);
        if (keyIdx < 0) return json;

        // 找到冒号后的 {
        int braceStart = json.IndexOf('{', keyIdx + key.Length);
        if (braceStart < 0) return json;

        // 匹配对应的 }
        int depth = 0;
        int braceEnd = -1;
        for (int i = braceStart; i < json.Length; i++)
        {
            if (json[i] == '{') depth++;
            else if (json[i] == '}')
            {
                depth--;
                if (depth == 0) { braceEnd = i; break; }
            }
        }
        if (braceEnd < 0) return json;

        // 计算要删除的范围（含前面的逗号或后面的逗号）
        int removeStart = keyIdx;
        int removeEnd   = braceEnd + 1;

        // 往前吃掉前置逗号+空白
        int lookBack = removeStart - 1;
        while (lookBack >= 0 && (json[lookBack] == ' ' || json[lookBack] == '\t' || json[lookBack] == '\r' || json[lookBack] == '\n'))
            lookBack--;
        if (lookBack >= 0 && json[lookBack] == ',')
            removeStart = lookBack;
        else
        {
            // 尝试吃掉后置逗号+空白
            int lookAhead = removeEnd;
            while (lookAhead < json.Length && (json[lookAhead] == ' ' || json[lookAhead] == '\t' || json[lookAhead] == '\r' || json[lookAhead] == '\n'))
                lookAhead++;
            if (lookAhead < json.Length && json[lookAhead] == ',')
                removeEnd = lookAhead + 1;
        }

        return json.Remove(removeStart, removeEnd - removeStart);
    }

    // ── CLI entry points ─────────────────────────────────────────────────

    /// <summary>CLI: 同时更新 Core + GM</summary>
    public static void UpdateAll()  => ForceUpdate(new[] { PkgCore, PkgGM });

    /// <summary>CLI: 只更新 Core</summary>
    public static void UpdateCore() => ForceUpdate(new[] { PkgCore });

    /// <summary>CLI: 只更新 GM</summary>
    public static void UpdateGM()   => ForceUpdate(new[] { PkgGM });
}
