using System;
using UnityEngine;
using ColossalFramework.UI;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 游戏界面内的「一键启动桌面端」浮动按钮（常驻 GameObject 上，跨场景保留）。
    /// - 固定尺寸长方形：180×30；
    /// - **右键拖拽**移动（左键点击仍是启动）；绝对坐标跟随，无反向问题；
    /// - 不透明度由设置页滑块控制（0-100%）；
    /// - 默认位于屏幕右上角。
    /// 兼容游戏 Mono 的 .NET 3.5：仅用 .NET 1.x API + 游戏自带 UnityEngine/ColossalManaged。
    /// </summary>
    public class ScreenshotLinkerUI : MonoBehaviour
    {
        /// <summary>按钮固定尺寸（用户要求 180×30）</summary>
        private const float ButtonWidth = 180f;
        private const float ButtonHeight = 30f;

        /// <summary>单例引用（设置页滑块实时应用不透明度用）</summary>
        private static ScreenshotLinkerUI _instance;

        private UIButton _button;
        private UIView _view;
        private DateTime _lastTryUtc = DateTime.MinValue;

        // ---- 拖拽状态（无锁，全部在 UI 主线程事件里） ----
        private bool _dragRightButton; // 右键按住中（仅右键拖拽移动）
        private bool _dragging;        // 本次按压已判定为拖拽
        private Vector2 _pressPos;     // 按下时的鼠标位置（相对按钮控件，UIMouseEventParameter.position 为 Vector2）

        /// <summary>拖动判定阈值（像素）：小于此距离视为点击，大于等于视为拖拽</summary>
        private const float DragThreshold = 3f;

        private void OnEnable()
        {
            _instance = this;
        }

        private void OnDisable()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        /// <summary>设置页滑块调用：立即应用按钮不透明度（0-100%）</summary>
        public static void SetOpacity(float percent)
        {
            if (!ReferenceEquals(_instance, null)) _instance.ApplyOpacity(percent);
        }

        private void ApplyOpacity(float percent)
        {
            if (!ReferenceEquals(_button, null))
            {
                _button.opacity = percent / 100f;
            }
        }

        private void Update()
        {
            try
            {
                EnsureButton();
            }
            catch (Exception ex)
            {
                ModLog.Warn("创建游戏内按钮失败：" + ex.Message);
            }
        }

        private void EnsureButton()
        {
            if (_button != null)
            {
                return;
            }
            // 场景/UI 未就绪时 UIView 可能为 null，5 秒内不重复尝试
            DateTime nowUtc = DateTime.UtcNow;
            TimeSpan span = nowUtc - _lastTryUtc;
            if (span.TotalSeconds < 5.0)
            {
                return;
            }
            _lastTryUtc = nowUtc;

            if (_view == null)
            {
                _view = UIView.GetAView();
                if (_view == null)
                {
                    return;
                }
            }

            // 文字取配置（已按 8 字上限截断）；固定长方形 180×30
            string text = ScreenshotLinkerConfig.Current.GetButtonText();
            UIButton btn = (UIButton)_view.AddUIComponent(typeof(UIButton)); // 非泛型 API（兼容所有 ColossalManaged 版本）
            btn.name = "ScreenshotLinkerLaunchButton";
            btn.autoSize = false;            // 固定尺寸，不用 autoSize
            btn.width = ButtonWidth;         // 180
            btn.height = ButtonHeight;       // 30
            btn.opacity = ScreenshotLinkerConfig.Current.GetOpacityPercent() / 100f; // 不透明度（设置页滑块）
            btn.text = text;
            btn.textScale = 0.85f;
            btn.textColor = new Color32(255, 255, 255, 255);
            btn.disabledTextColor = new Color32(160, 160, 160, 255);
            btn.normalBgSprite = "ButtonMenu";
            btn.hoveredBgSprite = "ButtonMenuHovered";
            btn.pressedBgSprite = "ButtonMenuPressed";

            // 右键拖拽：按下记录起点 → 移动超阈值进入拖拽 → 绝对坐标跟随 → 松手结束
            btn.eventMouseDown += OnMouseDownInstance;
            btn.eventMouseMove += OnMouseMoveInstance;
            btn.eventMouseUp += OnMouseUpInstance;
            btn.eventClick += OnLaunchClickedInstance;

            // 默认位置：屏幕右上角，留 16 像素边距（UIView 用 fixedWidth 基准宽度定位）
            btn.relativePosition = new Vector3((float)_view.fixedWidth - btn.width - 16f, 16f);
            _button = btn;
            ModLog.Info("已在游戏界面创建按钮（180×30，右键拖拽）：" + text);
        }

        private void OnMouseDownInstance(UIComponent component, UIMouseEventParameter eventParam)
        {
            // 仅右键进入拖拽；左键保留给点击启动
            if (eventParam.buttons != UIMouseButton.Right)
            {
                return;
            }
            _dragRightButton = true;
            _dragging = false;
            _pressPos = eventParam.position; // 按下时鼠标相对按钮的偏移
        }

        private void OnMouseMoveInstance(UIComponent component, UIMouseEventParameter eventParam)
        {
            if (!_dragRightButton) return;

            // 未判定拖拽前，先按位移阈值判断
            if (!_dragging)
            {
                Vector2 delta = eventParam.position - _pressPos;
                if (Mathf.Abs(delta.x) < DragThreshold && Mathf.Abs(delta.y) < DragThreshold)
                {
                    return;
                }
                _dragging = true;
            }

            // 绝对坐标跟随（修复反向问题）：
            // 鼠标在 UIView 的绝对位置 = 事件位置（相对按钮）+ 按钮相对位置；
            // 新按钮位置 = 鼠标绝对位置 - 按下时的按钮内偏移。鼠标上移按钮就上移，永不同向反向。
            Vector2 mouseInView = eventParam.position + (Vector2)_button.relativePosition;
            float x = mouseInView.x - _pressPos.x;
            float y = mouseInView.y - _pressPos.y;
            // 限制在屏幕范围内，避免拖出可视区找不回来
            x = Mathf.Clamp(x, 0f, (float)_view.fixedWidth - _button.width);
            y = Mathf.Clamp(y, 0f, (float)_view.fixedHeight - _button.height);
            _button.relativePosition = new Vector3(x, y);
        }

        private void OnMouseUpInstance(UIComponent component, UIMouseEventParameter eventParam)
        {
            _dragRightButton = false;
            _dragging = false;
        }

        private void OnLaunchClickedInstance(UIComponent component, UIMouseEventParameter eventParam)
        {
            if (_dragging)
            {
                // 本次是拖拽松手，不当作点击启动（click 在 mouseUp 之后触发）
                return;
            }
            Mod.LaunchDesktopApp();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_instance, this)) _instance = null;
            _button = null;
            _view = null;
            _dragRightButton = false;
            _dragging = false;
        }
    }
}
