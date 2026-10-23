using System;
using UnityEngine;
using ColossalFramework.UI;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 游戏界面内的「一键启动桌面端」浮动按钮（常驻 GameObject 上，跨场景保留）。
    /// - 浮动面板：按住左键可拖拽到任意位置（拖动超过阈值视为拖拽，松手不触发启动）；
    /// - 宽度贴合文字：随文字长度自适应（8 字上限），无固定底座；
    /// - 默认位于屏幕右上角。
    /// 兼容游戏 Mono 的 .NET 3.5：仅用 .NET 1.x API + 游戏自带 UnityEngine/ColossalManaged。
    /// </summary>
    public class ScreenshotLinkerUI : MonoBehaviour
    {
        private UIButton _button;
        private UIView _view;
        private DateTime _lastTryUtc = DateTime.MinValue;

        // ---- 拖拽状态（无锁，全部在 UI 主线程事件里） ----
        private bool _mouseDown;
        private bool _dragging;      // 本次按压已判定为拖拽
        private Vector2 _pressPos;   // 按下时的鼠标位置（相对控件，UIMouseEventParameter.position 为 Vector2）

        /// <summary>拖动判定阈值（像素）：小于此距离视为点击，大于等于视为拖拽</summary>
        private const float DragThreshold = 3f;

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

            // 文字取配置（已按 8 字上限截断），面板 autoSize 完全贴合字符长度（无固定底座）
            string text = ScreenshotLinkerConfig.Current.GetButtonText();
            UIButton btn = (UIButton)_view.AddUIComponent(typeof(UIButton)); // 非泛型 API（兼容所有 ColossalManaged 版本）
            btn.name = "ScreenshotLinkerLaunchButton";
            btn.autoSize = true; // 宽高随文字自动计算，刚好包住字符（ButtonMenu 皮肤自带视觉边距）
            btn.text = text;
            btn.textScale = 0.85f;
            btn.textColor = new Color32(255, 255, 255, 255);
            btn.disabledTextColor = new Color32(160, 160, 160, 255);
            btn.normalBgSprite = "ButtonMenu";
            btn.hoveredBgSprite = "ButtonMenuHovered";
            btn.pressedBgSprite = "ButtonMenuPressed";

            // 拖拽：按下记录起点 → 移动超阈值进入拖拽并随鼠标移动 → 松手（拖拽过则不触发启动）
            btn.eventMouseDown += OnMouseDownInstance;
            btn.eventMouseMove += OnMouseMoveInstance;
            btn.eventMouseUp += OnMouseUpInstance;
            btn.eventClick += OnLaunchClickedInstance;

            // 默认位置：屏幕右上角，留 16 像素边距（UIView 用 fixedWidth 基准宽度定位）
            btn.relativePosition = new Vector3((float)_view.fixedWidth - btn.width - 16f, 16f);
            _button = btn;
            ModLog.Info("已在游戏界面创建可拖拽按钮：" + text);
        }

        private void OnMouseDownInstance(UIComponent component, UIMouseEventParameter eventParam)
        {
            _mouseDown = true;
            _dragging = false;
            _pressPos = eventParam.position; // 相对按钮控件的位置
        }

        private void OnMouseMoveInstance(UIComponent component, UIMouseEventParameter eventParam)
        {
            if (!_mouseDown) return;

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

            // 拖动：按钮位置随鼠标移动（moveDelta 相对 UIView 坐标，与 relativePosition 同系）
            float x = _button.relativePosition.x + eventParam.moveDelta.x;
            float y = _button.relativePosition.y + eventParam.moveDelta.y;
            // 限制在屏幕范围内，避免拖出可视区找不回来
            x = Mathf.Clamp(x, 0f, (float)_view.fixedWidth - _button.width);
            y = Mathf.Clamp(y, 0f, (float)_view.fixedHeight - _button.height);
            _button.relativePosition = new Vector3(x, y);
        }

        private void OnMouseUpInstance(UIComponent component, UIMouseEventParameter eventParam)
        {
            _mouseDown = false;
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
            _button = null;
            _view = null;
            _mouseDown = false;
            _dragging = false;
        }
    }
}
