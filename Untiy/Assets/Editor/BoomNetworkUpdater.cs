using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

/// <summary>
/// BoomNetwork UPM 强制更新工具
///
/// Editor 菜单: BoomNetwork / Update Packages
/// CLI 用法 (Unity -batchmode):
///   -executeMethod BoomNetworkUpdater.UpdateAll
///   -executeMethod BoomNetworkUpdater.UpdateCore
///   -executeMethod BoomNetworkUpdater.UpdateGM
///
/// 原理: 与 Package Manager "Update" 按钮完全相同 —— Client.Add(gitUrl)
///   → UPM 服务端 PUT /project/dependencies/{packageId}
///   → 清除 lock 条目 → 重新 resolve → 下载 → 域重载
/// </summary>
public class BoomNetworkUpdater : EditorWindow
{
    const string PkgCore = "com.boom.boomnetwork";
    const string PkgGM   = "com.boom.boomnetwork.gm";

    // 当前进行中的请求
    static AddRequest          s_Request;
    static Queue<string>       s_PendingUrls;
    static bool                s_IsBatchMode;
    static string              s_Status = "";

    // ── Menu ─────────────────────────────────────────────────────────────

    [MenuItem("BoomNetwork/Update Packages")]
    static void OpenWindow()
    {
        var win = GetWindow<BoomNetworkUpdater>("BoomNetwork Updater");
        win.minSize = new Vector2(340, 180);
        win.Show();
    }

    // ── GUI ──────────────────────────────────────────────────────────────

    void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("BoomNetwork UPM Updater", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "调用 Client.Add(gitUrl)，与 Package Manager 的 Update 按钮行为完全一致：\n" +
            "清除 lock → 重新从 GitHub dev1.0 拉取 → 触发域重载。",
            MessageType.Info);
        EditorGUILayout.Space(8);

        bool busy = s_Request != null;

        using (new EditorGUI.DisabledScope(busy))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Update Core", GUILayout.Height(32)))
                RunWithConfirm(PkgCore);

            if (GUILayout.Button("Update GM", GUILayout.Height(32)))
                RunWithConfirm(PkgGM);
        }

        EditorGUILayout.Space(4);

        using (new EditorGUI.DisabledScope(busy))
        {
            if (GUILayout.Button("Update Both", GUILayout.Height(36)))
                RunWithConfirm(PkgCore, PkgGM);
        }

        if (!string.IsNullOrEmpty(s_Status))
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(s_Status, MessageType.None);
        }
    }

    void OnInspectorUpdate() => Repaint();

    static void RunWithConfirm(params string[] packageNames)
    {
        string list = string.Join("\n  • ", packageNames);
        bool ok = EditorUtility.DisplayDialog(
            "BoomNetwork Updater",
            $"将强制重新拉取以下包：\n  • {list}\n\nUnity 会重新编译，确认继续？",
            "Update", "Cancel");
        if (ok)
            StartUpdate(packageNames, batchMode: false);
    }

    // ── Core: Client.Add 链式队列 ─────────────────────────────────────────

    static void StartUpdate(string[] packageNames, bool batchMode)
    {
        var manifest = ReadManifest();
        if (manifest == null) return;

        s_PendingUrls = new Queue<string>();
        s_IsBatchMode = batchMode;

        foreach (string name in packageNames)
        {
            if (manifest.TryGetValue(name, out string url))
                s_PendingUrls.Enqueue(url);
            else
                Debug.LogWarning($"[BoomNetworkUpdater] {name} 不在 manifest.json 中，跳过。");
        }

        ProcessNextInQueue();
    }

    static void ProcessNextInQueue()
    {
        if (s_PendingUrls == null || s_PendingUrls.Count == 0)
        {
            s_Request = null;
            s_Status = "完成。等待 Unity 重新编译…";
            Debug.Log("[BoomNetworkUpdater] 所有包已提交更新，等待域重载。");
            if (s_IsBatchMode)
                EditorApplication.Exit(0);
            return;
        }

        string url = s_PendingUrls.Dequeue();
        s_Status = $"正在更新: {url}";
        Debug.Log($"[BoomNetworkUpdater] Client.Add({url})");

        s_Request = Client.Add(url);
        EditorApplication.update += OnUpdate;
    }

    static void OnUpdate()
    {
        if (s_Request == null || !s_Request.IsCompleted) return;

        EditorApplication.update -= OnUpdate;

        if (s_Request.Status == StatusCode.Success)
            Debug.Log($"[BoomNetworkUpdater] 更新成功: {s_Request.Result.packageId}");
        else
            Debug.LogError($"[BoomNetworkUpdater] 更新失败: {s_Request.Error?.message}");

        s_Request = null;
        ProcessNextInQueue();
    }

    // ── manifest.json 读取 ───────────────────────────────────────────────

    /// <summary>返回 packageName → gitUrl 的字典。</summary>
    static Dictionary<string, string> ReadManifest()
    {
        string path = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../Packages/manifest.json"));

        if (!File.Exists(path))
        {
            Debug.LogError($"[BoomNetworkUpdater] 找不到 {path}");
            return null;
        }

        // 用简单字符串解析取出 dependencies 块
        string text = File.ReadAllText(path);
        var result  = new Dictionary<string, string>();

        // 找 "dependencies" : { ... }
        int depIdx = text.IndexOf("\"dependencies\"", StringComparison.Ordinal);
        if (depIdx < 0) return result;

        int braceStart = text.IndexOf('{', depIdx);
        if (braceStart < 0) return result;

        // 提取 dependencies 块内所有 "key": "value" 对
        int i = braceStart + 1;
        int depth = 1;
        while (i < text.Length && depth > 0)
        {
            if (text[i] == '{') { depth++; i++; continue; }
            if (text[i] == '}') { depth--; i++; continue; }

            if (depth == 1 && text[i] == '"')
            {
                // 读 key
                int keyEnd = text.IndexOf('"', i + 1);
                if (keyEnd < 0) break;
                string key = text.Substring(i + 1, keyEnd - i - 1);
                i = keyEnd + 1;

                // 跳到下一个 "
                int valStart = text.IndexOf('"', i);
                if (valStart < 0) break;
                int valEnd = text.IndexOf('"', valStart + 1);
                if (valEnd < 0) break;
                string val = text.Substring(valStart + 1, valEnd - valStart - 1);
                i = valEnd + 1;

                result[key] = val;
                continue;
            }
            i++;
        }

        return result;
    }

    // ── CLI entry points ─────────────────────────────────────────────────

    /// <summary>CLI: 同时更新 Core + GM</summary>
    public static void UpdateAll()  => StartUpdate(new[] { PkgCore, PkgGM }, batchMode: true);

    /// <summary>CLI: 只更新 Core</summary>
    public static void UpdateCore() => StartUpdate(new[] { PkgCore }, batchMode: true);

    /// <summary>CLI: 只更新 GM</summary>
    public static void UpdateGM()   => StartUpdate(new[] { PkgGM }, batchMode: true);
}
