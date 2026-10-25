using System;
using System.Threading;
using UnityEngine;
using ColossalFramework.UI;

namespace CitiesSkylines.ScreenshotLinker
{
    public class ScreenshotLinkerUI : MonoBehaviour
    {
        private const float ButtonWidth = 180f;
        private const float ButtonHeight = 30f;
        private static ScreenshotLinkerUI _instance;
        private UIButton _button;
        private UIView _view;
        private DateTime _lastTryUtc = DateTime.MinValue;

        // ---- 拖拽状态 ----
        private bool _dragRightButton;
        private bool _dragging;
        private bool _rightWasDown;
        private Vector3 _lastMousePx;
        private const float DragThreshold = 3f;

        // ---- 心跳定时器 ----
        private float _heartbeatTimer = 0f;
        private const float HeartbeatInterval = 3.0f;
        private static int _heartbeatPending = 0;

        private void OnEnable()
        {
            _instance = this;
            ModLog.Info("[ScreenshotLinker] ScreenshotLinkerUI OnEnable: 组件已启用");
        }

        private void OnDisable()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        public static void SetOpacity(float percent)
        {
            if (!ReferenceEquals(_instance, null)) _instance.ApplyOpacity(percent);
        }

        private void ApplyOpacity(float percent)
        {
            if (!ReferenceEquals(_button, null)) _button.opacity = percent / 100f;
        }

        private void Update()
        {
            try { EnsureButton(); } catch (Exception ex) { ModLog.Error("[ScreenshotLinker] 创建按钮异常: " + ex.ToString()); }
            try { UpdateDrag(); } catch (Exception ex) { ModLog.Warn("[ScreenshotLinker] 拖拽异常：" + ex.Message); }

            // 心跳
            try
            {
                _heartbeatTimer += Time.deltaTime;
                if (_heartbeatTimer >= HeartbeatInterval)
                {
                    _heartbeatTimer = 0f;
                    if (Interlocked.CompareExchange(ref _heartbeatPending, 1, 0) == 0)
                    {
                        ThreadPool.QueueUserWorkItem(HeartbeatCallback);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn("[ScreenshotLinker] 发送心跳包异常：" + ex.Message);
            }
        }

        private static void HeartbeatCallback(object state)
        {
            try { SocketSender.SendHeartbeat(); }
            catch (Exception ex) { ModLog.Warn("[ScreenshotLinker] 心跳线程异常：" + ex.Message); }
            finally { Interlocked.Exchange(ref _heartbeatPending, 0); }
        }

        private void EnsureButton()
        {
            // 1. 如果按钮已存在，直接返回
            if (_button != null) return;

            // 2. 频率限制：每 5 秒尝试一次，防止刷屏卡死
            DateTime nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastTryUtc).TotalSeconds < 5.0) return;
            _lastTryUtc = nowUtc;

            ModLog.Info("[ScreenshotLinker] 尝试创建按钮 (EnsureButton 开始执行)...");

            // 3. 获取 UIView
            if (_view == null)
            {
                _view = UIView.GetAView();
                if (_view == null)
                {
                    ModLog.Warn("[ScreenshotLinker] 无法获取 UIView，游戏可能还在加载中...");
                    return;
                }
            }

            // 4. 检查配置
            if (ScreenshotLinkerConfig.Current == null)
            {
                ModLog.Error("[ScreenshotLinker] ScreenshotLinkerConfig.Current 为 null，配置未初始化！");
                return;
            }
            string text = ScreenshotLinkerConfig.Current.GetButtonText();

            // 5. 创建按钮
            ModLog.Info("[ScreenshotLinker] 正在实例化 UIButton...");
            UIButton btn = (UIButton)_view.AddUIComponent(typeof(UIButton));

            if (btn == null)
            {
                ModLog.Error("[ScreenshotLinker] AddUIComponent 返回 null，创建 UIButton 失败！");
                return;
            }

            btn.name = "ScreenshotLinkerLaunchButton";
            btn.autoSize = false;
            btn.width = ButtonWidth;
            btn.height = ButtonHeight;
            btn.opacity = ScreenshotLinkerConfig.Current.GetOpacityPercent() / 100f;
            btn.text = text;
            btn.textScale = 0.85f;
            btn.textColor = new Color32(255, 255, 255, 255);
            btn.disabledTextColor = new Color32(160, 160, 160, 255);
            btn.normalBgSprite = "ButtonMenu";
            btn.hoveredBgSprite = "ButtonMenuHovered";
            btn.pressedBgSprite = "ButtonMenuPressed";
            btn.eventClick += new MouseEventHandler(OnLaunchClickedInstance);

            // 6. 计算位置（增加防呆）
            float targetX = (float)_view.fixedWidth - btn.width - 16f;
            float targetY = 16f;

            // 如果 fixedWidth 为 0，说明 UI 还没准备好，把按钮先扔到屏幕外
            if (targetX <= 0)
            {
                targetX = 100;
                ModLog.Warn("[ScreenshotLinker] _view.fixedWidth 异常 (" + _view.fixedWidth + ")，可能 UI 未就绪");
            }

            btn.relativePosition = new Vector3(targetX, targetY);

            _button = btn;
            ModLog.Info("[ScreenshotLinker] ✅ 按钮创建成功！位置: " + targetX + ", " + targetY);
        }

        private void UpdateDrag()
        {
            if (_button == null) return;
            bool rightDown = Input.GetMouseButton(1);
            bool wasDown = _rightWasDown;
            _rightWasDown = rightDown;

            if (!rightDown) { _dragRightButton = false; _dragging = false; return; }

            if (!wasDown)
            {
                _dragRightButton = IsMouseOverButton();
                _dragging = false;
                if (_dragRightButton) _lastMousePx = Input.mousePosition;
                return;
            }

            if (!_dragRightButton) return;

            Vector3 mp = Input.mousePosition;
            float scale = (float)Screen.width / (float)_view.fixedWidth;
            if (scale <= 0f) scale = 1f;
            float dx = (mp.x - _lastMousePx.x) / scale;
            float dy = -(mp.y - _lastMousePx.y) / scale;

            if (!_dragging)
            {
                if (Mathf.Abs(dx * scale) < DragThreshold && Mathf.Abs(dy * scale) < DragThreshold)
                {
                    _lastMousePx = mp; return;
                }
                _dragging = true;
            }

            Vector3 p = _button.relativePosition + new Vector3(dx, dy);
            p.x = Mathf.Clamp(p.x, 0f, (float)_view.fixedWidth - _button.width);
            p.y = Mathf.Clamp(p.y, 0f, (float)_view.fixedHeight - _button.height);
            _button.relativePosition = p;
            _lastMousePx = mp;
        }

        private bool IsMouseOverButton()
        {
            Vector3 mp = Input.mousePosition;
            float scale = (float)Screen.width / (float)_view.fixedWidth;
            if (scale <= 0f) scale = 1f;
            float sx = _button.relativePosition.x * scale;
            float sy = (float)Screen.height - (_button.relativePosition.y + _button.height) * scale;
            float w = _button.width * scale;
            float h = _button.height * scale;
            return mp.x >= sx && mp.x <= sx + w && mp.y >= sy && mp.y <= sy + h;
        }

        private void OnLaunchClickedInstance(UIComponent component, UIMouseEventParameter eventParam)
        {
            if (_dragging) return;
            ModLog.Info("[ScreenshotLinker] 点击了启动按钮");
            Mod.LaunchDesktopApp();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
            _button = null;
            _view = null;
            ModLog.Info("[ScreenshotLinker] ScreenshotLinkerUI 已销毁");
        }
    }
}