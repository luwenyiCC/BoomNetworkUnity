using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace BoomNetworkDemo.Editor
{
    public class ServerWindow : EditorWindow
    {
        [SerializeField] private string _serverPath = "/Users/boom/Demo/BoomNetwork/svr";
        [SerializeField] private string _configFile = "cmd/framesync/config.yaml";
        [SerializeField] private string _addr       = ":9000";
        [SerializeField] private string _proto      = "tcp";
        [SerializeField] private int    _ppr        = 2;
        [SerializeField] private string _adminAddr  = "http://127.0.0.1:9091";

        private bool   _lastCheckAlive;
        private double _nextCheckTime;
        private const double CHECK_INTERVAL = 2.0;

        // /health
        private int    _rooms   = -1;
        private int    _players = -1;
        private string _uptime  = "";

        // /stats
        private long _rxTotal, _txTotal;
        private long _rx1min,  _tx1min;
        private long _rx5sec,  _tx5sec;
        private bool _hasStats;

        [MenuItem("BoomNetwork/Server Window")]
        public static void ShowWindow() => GetWindow<ServerWindow>("BoomNetwork Server");

        void OnEnable()  => EditorApplication.update += OnEditorUpdate;
        void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextCheckTime) return;
            _nextCheckTime = EditorApplication.timeSinceStartup + CHECK_INTERVAL;

            bool alive = FetchHealth();
            if (alive) FetchStats();
            else { _rooms = _players = -1; _uptime = ""; _hasStats = false; }

            if (alive != _lastCheckAlive || alive)
            {
                _lastCheckAlive = alive;
                Repaint();
            }
        }

        void OnGUI()
        {
            // ===== Status =====
            EditorGUILayout.Space(4);
            var prev = GUI.contentColor;
            GUI.contentColor = _lastCheckAlive ? Color.green : Color.gray;
            EditorGUILayout.LabelField($"● {(_lastCheckAlive ? "RUNNING" : "STOPPED")}", EditorStyles.boldLabel);
            GUI.contentColor = prev;

            if (_lastCheckAlive && _rooms >= 0)
                EditorGUILayout.LabelField($"  Rooms: {_rooms}   Players: {_players}   Uptime: {_uptime}");

            // ===== Traffic =====
            if (_lastCheckAlive && _hasStats)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Traffic", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Total", GUILayout.Width(40));
                DrawTrafficRow(_txTotal, _rxTotal);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("1 min", GUILayout.Width(40));
                DrawTrafficRow(_tx1min, _rx1min);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("5 sec", GUILayout.Width(40));
                // 5秒窗口 / 5 = 每秒速率
                DrawTrafficRow(_tx5sec / 5, _rx5sec / 5, perSec: true);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(6);

            // ===== Config =====
            EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
            _serverPath = EditorGUILayout.TextField("Server Path", _serverPath);
            _configFile = EditorGUILayout.TextField("Config File", _configFile);
            _adminAddr  = EditorGUILayout.TextField("Admin URL",   _adminAddr);

            EditorGUILayout.BeginHorizontal();
            _addr  = EditorGUILayout.TextField("Address", _addr);
            _proto = EditorGUILayout.TextField("Proto",   _proto);
            _ppr   = EditorGUILayout.IntField("PPR",      _ppr);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // ===== Actions =====
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = _lastCheckAlive ? Color.gray : Color.green;
            GUI.enabled = !_lastCheckAlive;
            if (GUILayout.Button("Start Server", GUILayout.Height(30)))
                OpenTerminalWithServer();
            GUI.enabled = true;

            GUI.backgroundColor = _lastCheckAlive ? new Color(1f, 0.4f, 0.3f) : Color.gray;
            GUI.enabled = _lastCheckAlive;
            if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
                KillPort();
            GUI.enabled = true;

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ===== Command =====
            EditorGUILayout.LabelField("Manual Command", EditorStyles.boldLabel);
            var cmd = BuildCommand();
            EditorGUILayout.SelectableLabel(cmd, EditorStyles.textField, GUILayout.Height(20));
            if (GUILayout.Button("Copy Command"))
            {
                GUIUtility.systemCopyBuffer = cmd;
                ShowNotification(new GUIContent("Copied!"));
            }
        }

        // ===================== Traffic Drawing =====================

        static void DrawTrafficRow(long tx, long rx, bool perSec = false)
        {
            string suffix = perSec ? "/s" : "";
            var prevUp   = GUI.contentColor;
            GUI.contentColor = new Color(0.4f, 0.8f, 1f);
            EditorGUILayout.LabelField($"↑ {FmtBytes(tx)}{suffix}", GUILayout.Width(90));
            GUI.contentColor = new Color(0.5f, 1f, 0.5f);
            EditorGUILayout.LabelField($"↓ {FmtBytes(rx)}{suffix}");
            GUI.contentColor = prevUp;
        }

        static string FmtBytes(long b)
        {
            if (b < 0)     return "—";
            if (b < 1024)  return $"{b} B";
            if (b < 1 << 20) return $"{b / 1024.0:F1} KB";
            return $"{b / (1024.0 * 1024):F2} MB";
        }

        // ===================== HTTP Fetch =====================

        bool FetchHealth()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = System.TimeSpan.FromMilliseconds(500);
                var task = client.GetStringAsync($"{_adminAddr.TrimEnd('/')}/health");
                task.Wait(600);
                if (!task.IsCompletedSuccessfully) return false;

                var json = task.Result;
                _rooms   = ParseInt(json, "rooms");
                _players = ParseInt(json, "players");
                _uptime  = ParseStr(json, "uptime");
                return json.Contains("\"ok\"");
            }
            catch { return false; }
        }

        void FetchStats()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = System.TimeSpan.FromMilliseconds(500);
                var task = client.GetStringAsync($"{_adminAddr.TrimEnd('/')}/stats");
                task.Wait(600);
                if (!task.IsCompletedSuccessfully) return;

                var json = task.Result;
                _rxTotal = ParseLong(json, "rx_total_bytes");
                _txTotal = ParseLong(json, "tx_total_bytes");
                _rx1min  = ParseLong(json, "rx_1min_bytes");
                _tx1min  = ParseLong(json, "tx_1min_bytes");
                _rx5sec  = ParseLong(json, "rx_5sec_bytes");
                _tx5sec  = ParseLong(json, "tx_5sec_bytes");
                _hasStats = true;
            }
            catch { _hasStats = false; }
        }

        // ===================== Helpers =====================

        string BuildCommand() =>
            string.IsNullOrEmpty(_configFile)
                ? $"cd {_serverPath} && go run ./cmd/framesync/ -addr={_addr} -proto={_proto} -ppr={_ppr}"
                : $"cd {_serverPath} && go run ./cmd/framesync/ -config={_configFile}";

        void OpenTerminalWithServer()
        {
            var cmd    = BuildCommand();
            var script = $"tell application \"Terminal\" to do script \"{cmd}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName  = "osascript",
                Arguments = $"-e '{script}'",
                UseShellExecute = false,
                CreateNoWindow  = true,
            });
            ShowNotification(new GUIContent("Server starting..."));
            _nextCheckTime = EditorApplication.timeSinceStartup + 3.0;
        }

        void KillPort()
        {
            var port = _addr.TrimStart(':');
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName  = "/bin/bash",
                    Arguments = $"-c \"lsof -ti:{port} -sTCP:LISTEN | xargs kill -9 2>/dev/null; echo done\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow         = true,
                };
                Process.Start(psi)?.WaitForExit(3000);
                _lastCheckAlive = false;
                _rooms = _players = -1; _uptime = "";
                _hasStats = false;
                Repaint();
                ShowNotification(new GUIContent("Server stopped"));
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Kill port failed: {e.Message}");
            }
        }

        static int  ParseInt(string j, string k)  => (int)ParseLong(j, k);
        static long ParseLong(string j, string k)
        {
            var m = Regex.Match(j, $@"""{k}""\s*:\s*(\d+)");
            return m.Success && long.TryParse(m.Groups[1].Value, out var v) ? v : -1;
        }
        static string ParseStr(string j, string k)
        {
            var m = Regex.Match(j, $@"""{k}""\s*:\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}
