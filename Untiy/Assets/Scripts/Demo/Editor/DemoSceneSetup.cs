using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace BoomNetworkDemo.Editor
{
    /// <summary>
    /// 自动将 Demo 场景添加到 Build Settings，确保 DemoLauncher 场景切换能工作。
    /// </summary>
    [InitializeOnLoad]
    public static class DemoSceneSetup
    {
        private static readonly string[] RequiredScenes = new[]
        {
            "Assets/Scenes/Launcher.unity",
            "Assets/Scenes/Demo01-Basic.unity",
            "Assets/Scenes/Demo01.1-MultiClient.unity",
            "Assets/Scenes/Demo03-EntitySync.unity",
        };

        static DemoSceneSetup()
        {
            EditorApplication.delayCall += EnsureScenesInBuildSettings;
        }

        [MenuItem("BoomNetwork/Setup Build Scenes")]
        public static void EnsureScenesInBuildSettings()
        {
            var current = EditorBuildSettings.scenes.ToList();
            var currentPaths = new HashSet<string>(current.Select(s => s.path));
            bool changed = false;

            foreach (var scenePath in RequiredScenes)
            {
                if (!currentPaths.Contains(scenePath))
                {
                    // 确认文件存在
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
                    {
                        current.Add(new EditorBuildSettingsScene(scenePath, true));
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                EditorBuildSettings.scenes = current.ToArray();
                UnityEngine.Debug.Log($"[DemoSceneSetup] Added {RequiredScenes.Length} demo scenes to Build Settings");
            }
        }
    }
}
