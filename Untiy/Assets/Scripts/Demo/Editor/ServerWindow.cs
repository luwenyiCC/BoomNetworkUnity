using UnityEngine;
using UnityEditor;
using System.Diagnostics;

namespace BoomNetworkDemo.Editor
{
    public class ServerWindow : EditorWindow
    {
        [SerializeField] private string _serverPath = "/Users/boom/Demo/BoomNetwork/svr";
        [SerializeField] private string _configFile = "cmd/framesync/config.yaml";
        [SerializeField] private string _addr = ":9000";
        [SerializeField] private string _proto = "tcp";
        [SerializeField] private int _ppr = 2;

        [MenuItem("BoomNetwork/Server Window")]
        public static void ShowWindow()
        {
            GetWindow<ServerWindow>("BoomNetwork Server");
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Server Config", EditorStyles.boldLabel);
            _serverPath = EditorGUILayout.TextField("Server Path", _serverPath);
            _configFile = EditorGUILayout.TextField("Config File", _configFile);

            EditorGUILayout.BeginHorizontal();
            _addr = EditorGUILayout.TextField("Address", _addr);
            _proto = EditorGUILayout.TextField("Proto", _proto);
            _ppr = EditorGUILayout.IntField("PPR", _ppr);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 一键启动：打开终端运行服务器
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Start Server (Open Terminal)", GUILayout.Height(35)))
            {
                OpenTerminalWithServer();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(5);

            // 一键停止：杀端口
            GUI.backgroundColor = new Color(1f, 0.5f, 0.3f);
            if (GUILayout.Button("Stop Server (Kill Port 9000)", GUILayout.Height(30)))
            {
                KillPort();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(
                "Server runs in a separate Terminal window.\n" +
                "Close the Terminal window to stop the server.\n\n" +
                "Or use 'Stop Server' button to kill port 9000.",
                MessageType.Info);

            EditorGUILayout.Space(10);

            // 快速复制命令
            EditorGUILayout.LabelField("Manual Command", EditorStyles.boldLabel);
            var cmd = string.IsNullOrEmpty(_configFile)
                ? $"cd {_serverPath} && go run ./cmd/framesync/ -addr={_addr} -proto={_proto} -ppr={_ppr}"
                : $"cd {_serverPath} && go run ./cmd/framesync/ -config={_configFile}";
            EditorGUILayout.SelectableLabel(cmd, EditorStyles.textField, GUILayout.Height(20));

            if (GUILayout.Button("Copy Command"))
            {
                GUIUtility.systemCopyBuffer = cmd;
                ShowNotification(new GUIContent("Copied!"));
            }
        }

        void OpenTerminalWithServer()
        {
            var cmd = string.IsNullOrEmpty(_configFile)
                ? $"cd {_serverPath} && go run ./cmd/framesync/ -addr={_addr} -proto={_proto} -ppr={_ppr}"
                : $"cd {_serverPath} && go run ./cmd/framesync/ -config={_configFile}";

            // macOS: 打开 Terminal.app 执行命令
            var script = $"tell application \"Terminal\" to do script \"{cmd}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e '{script}'",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            ShowNotification(new GUIContent("Server starting in Terminal..."));
        }

        void KillPort()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = "-c \"lsof -ti:9000 | xargs kill -9 2>/dev/null; echo done\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                var proc = Process.Start(psi);
                proc.WaitForExit(3000);
                ShowNotification(new GUIContent("Port 9000 freed"));
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Kill port failed: {e.Message}");
            }
        }
    }
}
