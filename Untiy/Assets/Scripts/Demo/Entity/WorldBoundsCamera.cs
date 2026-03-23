using UnityEngine;

namespace BoomNetworkDemo
{
    /// <summary>
    /// 正交摄像机自适配：保证固定世界边界（WorldHalfWidth × WorldHalfHeight）
    /// 在任意窗口分辨率下完整可见。
    ///
    /// 窗口比世界更宽 → 左右留空，高度刚好
    /// 窗口比世界更窄 → 上下留空，宽度刚好
    /// 两个编辑器看到的游戏区域完全一致。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class WorldBoundsCamera : MonoBehaviour
    {
        private Camera _cam;

        void Awake() => _cam = GetComponent<Camera>();

        void Update()
        {
            if (_cam == null || !_cam.orthographic) return;

            float worldW = PlayerEntity.WorldHalfWidth;
            float worldH = PlayerEntity.WorldHalfHeight;
            float screenAspect = (float)Screen.width / Screen.height;
            float worldAspect = worldW / worldH;

            if (screenAspect >= worldAspect)
            {
                // 窗口更宽：以高度为基准
                _cam.orthographicSize = worldH;
            }
            else
            {
                // 窗口更窄：以宽度为基准，增大 orthoSize 保证宽度可见
                _cam.orthographicSize = worldW / screenAspect;
            }
        }
    }
}
