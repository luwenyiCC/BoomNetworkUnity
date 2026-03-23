using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// Demo01 HUD — 简化版，无预测统计
    /// </summary>
    [RequireComponent(typeof(BasicPersonManager))]
    public class BasicGameHUD : MonoBehaviour
    {
        private BasicPersonManager _mgr;
        private GUIStyle _boxStyle, _titleStyle, _textStyle, _tipStyle;
        private float _fps;
        private int _frameCount;
        private float _fpsTimer;

        void Awake() => _mgr = GetComponent<BasicPersonManager>();

        void Update()
        {
            _frameCount++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f) { _fps = _frameCount / _fpsTimer; _frameCount = 0; _fpsTimer = 0; }
        }

        void OnGUI()
        {
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle("box") { padding = new RectOffset(8, 8, 6, 6) };
                _titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14, richText = true };
                _textStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
                _tipStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
            }

            // Left: Network status
            GUILayout.BeginArea(new Rect(10, 10, 320, 200), _boxStyle);
            GUILayout.Label("Network", _titleStyle);
            if (_mgr != null)
            {
                foreach (var slot in _mgr.persons)
                {
                    if (slot.person == null) { GUILayout.Label($"  {slot.inputMode}: Idle", _textStyle); continue; }
                    var p = slot.person;
                    var stateColor = p.State == PersonState.Syncing ? "<color=lime>" :
                        p.State == PersonState.Disconnected ? "<color=red>" : "<color=yellow>";
                    GUILayout.Label($"  {slot.inputMode}: {stateColor}{p.State}</color> P{p.PlayerId} F{p.FrameNumber}", _textStyle);
                }
            }
            GUILayout.Label($"  FPS: {_fps:F0}", _textStyle);
            GUILayout.Label($"  Sync: {_mgr?.syncStatus ?? "?"}", _textStyle);
            GUILayout.EndArea();

            // Right: Controls
            GUILayout.BeginArea(new Rect(Screen.width - 230, 10, 220, 180), _boxStyle);
            GUILayout.Label("Controls", _titleStyle);
            GUILayout.Label("  WASD  = Player 1 move", _tipStyle);
            GUILayout.Label("  Arrows = Player 2 move", _tipStyle);
            GUILayout.Space(5);
            GUILayout.Label("Quick Start:", _titleStyle);
            GUILayout.Label("  1. Start Go server", _tipStyle);
            GUILayout.Label("  2. Inspector: Connect All", _tipStyle);
            GUILayout.Label("  3. Inspector: Start Game", _tipStyle);
            GUILayout.Label("  4. Move with keyboard!", _tipStyle);
            GUILayout.EndArea();
        }
    }
}
