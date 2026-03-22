using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// 2D Game HUD — 左上角显示网络状态，右上角显示操作提示
    /// 挂在 PersonManager 同一 GameObject 上
    /// </summary>
    [RequireComponent(typeof(PersonManager))]
    public class GameHUD : MonoBehaviour
    {
        private PersonManager _mgr;
        private GUIStyle _boxStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _textStyle;
        private GUIStyle _tipStyle;
        private float _fps;
        private int _frameCount;
        private float _fpsTimer;

        void Awake()
        {
            _mgr = GetComponent<PersonManager>();
        }

        void Update()
        {
            _frameCount++;
            _fpsTimer += Time.deltaTime;
            if (_fpsTimer >= 1f)
            {
                _fps = _frameCount / _fpsTimer;
                _frameCount = 0;
                _fpsTimer = 0;
            }
        }

        void InitStyles()
        {
            if (_boxStyle != null) return;

            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = MakeTex(2, 2, new Color(0, 0, 0, 0.7f));
            _boxStyle.padding = new RectOffset(8, 8, 6, 6);

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.fontSize = 14;
            _titleStyle.normal.textColor = Color.white;

            _textStyle = new GUIStyle(GUI.skin.label);
            _textStyle.fontSize = 12;
            _textStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

            _tipStyle = new GUIStyle(GUI.skin.label);
            _tipStyle.fontSize = 11;
            _tipStyle.normal.textColor = new Color(0.7f, 0.7f, 0.5f);
            _tipStyle.wordWrap = true;
        }

        void OnGUI()
        {
            InitStyles();

            // === 左上角：网络状态 ===
            GUILayout.BeginArea(new Rect(10, 10, 220, 200), _boxStyle);
            GUILayout.Label("Network", _titleStyle);

            if (_mgr != null && _mgr.persons != null)
            {
                foreach (var slot in _mgr.persons)
                {
                    if (slot.person == null)
                    {
                        GUILayout.Label($"  {slot.inputMode}: Idle", _textStyle);
                        continue;
                    }
                    var p = slot.person;
                    var stateColor = p.State == PersonState.Syncing ? "<color=lime>" :
                                     p.State == PersonState.InRoom ? "<color=yellow>" :
                                     p.State == PersonState.Connected ? "<color=cyan>" : "<color=red>";
                    GUILayout.Label($"  {slot.inputMode}: {stateColor}{p.State}</color> P{p.PlayerId} F{p.FrameNumber}", _textStyle);
                }
            }

            GUILayout.Label($"  FPS: {_fps:F0}", _textStyle);
            GUILayout.Label($"  Sync: {_mgr?.syncStatus ?? "?"}", _textStyle);
            if (_mgr != null && _mgr.enablePrediction && _mgr.PredictionStats != null)
            {
                var ps = _mgr.PredictionStats;
                GUILayout.Label($"  <color=cyan>Prediction: ahead={ps.ahead} rollbacks={ps.totalRollbacks} frames={ps.totalRollbackFrames}</color>", _textStyle);
            }
            GUILayout.EndArea();

            // === 右上角：操作提示 ===
            GUILayout.BeginArea(new Rect(Screen.width - 230, 10, 220, 200), _boxStyle);
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

        static Texture2D MakeTex(int w, int h, Color color)
        {
            var tex = new Texture2D(w, h);
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
