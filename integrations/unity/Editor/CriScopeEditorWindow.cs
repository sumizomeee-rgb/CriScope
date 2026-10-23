#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CriScope.Unity.Editor
{
    /// <summary>通用编辑器 CriScope 入口；窗口关闭后立即停止本窗口的采集。</summary>
    public sealed class CriScopeEditorWindow : EditorWindow
    {
        private const string HostPreference = "CriScope.Unity.Editor.ReceiverHost";
        [SerializeField] private string hostInput = "127.0.0.1";

        [MenuItem("Tools/Audio/CriScope")]
        public static void Open()
        {
            var window = GetWindow<CriScopeEditorWindow>("CriScope");
            window.minSize = new Vector2(440, 270);
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(440, 270);
            hostInput = CriScopeEditorCapture.CaptureEnabled ? CriScopeEditorCapture.Host
                : EditorPrefs.GetString(HostPreference, "127.0.0.1");
        }

        private void OnDisable()
        {
            CriScopeEditorCapture.Disconnect();
        }

        private void OnInspectorUpdate() { Repaint(); }

        private void OnGUI()
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField("CriScope 音频采集", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(Application.isPlaying ? "Unity · Play 模式" : "Unity · 编辑器预览");
            GUILayout.Space(8);

            bool enabled = CriScopeEditorCapture.CaptureEnabled;
            using (new EditorGUI.DisabledScope(enabled))
            {
                hostInput = EditorGUILayout.TextField("服务 IP / 主机名", hostInput);
            }
            EditorGUI.BeginChangeCheck();
            bool nextEnabled;
            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isUpdating))
            {
                nextEnabled = EditorGUILayout.Toggle("采集（自动连接 / 重连）", enabled);
            }
            if (EditorGUI.EndChangeCheck()) ApplyCapture(nextEnabled);

            GUILayout.Space(8);
            string status = CriScopeEditorCapture.Status;
            bool warning = status.Contains("不可用") || status.Contains("无法") || status.Contains("异常");
            EditorGUILayout.HelpBox(status, warning ? MessageType.Warning : MessageType.Info);
            EditorGUILayout.HelpBox("先启动 CriScope 桌面工具，再开启采集。服务 IP 默认 127.0.0.1；跨机器填写桌面工具所在电脑的 IP。", MessageType.None);
            EditorGUILayout.LabelField("支持编辑态与 Play 预览；关闭窗口、重编译或切换 Play 会自动停止采集。", EditorStyles.wordWrappedMiniLabel);
        }

        private bool ApplyCapture(bool enabled)
        {
            bool accepted = CriScopeEditorCapture.SetCaptureEnabled(enabled, hostInput);
            if (accepted && enabled)
            {
                hostInput = CriScopeEditorCapture.Host;
                EditorPrefs.SetString(HostPreference, hostInput);
            }
            Repaint();
            return accepted;
        }
    }
}
#endif
