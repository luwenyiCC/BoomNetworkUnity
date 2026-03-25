using UnityEngine;
using UnityEngine.SceneManagement;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo 入口场景 — 场景选择菜单
    /// </summary>
    public class DemoLauncher : MonoBehaviour
    {
        private GUIStyle _titleStyle, _btnStyle, _descStyle, _boxStyle;

        void OnGUI()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _btnStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 18, fixedHeight = 50,
                    fontStyle = FontStyle.Bold
                };
                _descStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13, wordWrap = true,
                    richText = true, alignment = TextAnchor.UpperLeft
                };
                _boxStyle = new GUIStyle("box")
                {
                    padding = new RectOffset(16, 16, 12, 12)
                };
            }

            float w = 500, h = 560;
            GUILayout.BeginArea(new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h));

            GUILayout.Label("BoomNetwork Demo", _titleStyle);
            GUILayout.Space(10);

            // Demo01 - Basic
            GUILayout.BeginVertical(_boxStyle);
            if (GUILayout.Button("Demo01 - Basic Frame Sync", _btnStyle))
                SceneManager.LoadScene("Demo01-Basic");
            GUILayout.Label(
                "Single editor, 2 local Persons (WASD + Arrows).\n" +
                "Basic frame sync, snapshot reconnect, no prediction.",
                _descStyle);
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // Demo01.1 - MultiClient
            GUILayout.BeginVertical(_boxStyle);
            if (GUILayout.Button("Demo01.1 - Multi-Client (ParrelSync)", _btnStyle))
                SceneManager.LoadScene("Demo01.1-MultiClient");
            GUILayout.Label(
                "ParrelSync multi-editor setup. Each editor 1 Person.\n" +
                "Room ID sharing, Drop1s/Drop8s reconnect testing, World Hash sync check.",
                _descStyle);
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // Demo03 - Entity Sync Multi-Client
            GUILayout.BeginVertical(_boxStyle);
            if (GUILayout.Button("Demo03 - Entity Sync (Multi-Client)", _btnStyle))
                SceneManager.LoadScene("Demo03-EntitySync");
            GUILayout.Label(
                "ParrelSync multi-editor, each editor 1 real player.\n" +
                "Entity Authority Sync + Dead Reckoning + Network Simulation.\n" +
                "Self-authority: own input = instant, remote = smooth correction.",
                _descStyle);
            GUILayout.EndVertical();

            GUILayout.Space(15);
            GUILayout.Label("Start Go server first: BoomNetwork > Server Window", _descStyle);

            GUILayout.EndArea();
        }
    }
}
