using System;
using UnityEngine;
using ColossalFramework.UI;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 游戏界面内的「一键启动桌面端」浮动按钮（常驻 GameObject 上，跨场景保留）。
    /// - 固定尺寸长方形：180×30（尺寸固定，位置自由）；
    /// - **右键拖拽**移动（左键点击仍是启动）；拖拽用每帧轮询 UnityEngine.Input.mousePosition
    ///   （屏幕像素坐标，Y 向上，方向天然与鼠标一致；且不依赖 UI 事件捕获，鼠标移出按钮也不中断）；
    /// - 不透明度由设置页滑块/输入框控制（0-100%）；
    /// - 默认位于屏幕右上角。
    /// 兼容游戏 Mono 的 .NET 3.5：仅用 .NET 1.x API + 游戏自带 UnityEngine/ColossalManaged。
    /// 注意：反射确认该版本 UIView 直接继承 MonoBehaviour（无任何鼠标事件），因此拖拽必须走 Update 轮询。
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

        // ---- 拖拽状态（无锁，全部在 Unity 主线程） ----
        private bool _dragRightButton;  // 右键按住中（仅右键拖拽移动）
        private bool _dragging;         // 本次按压已判定为拖拽
        private bool _rightWasDown;     // 上一帧右键是否按住（检测"刚按下"沿）
        private Vector3 _lastMousePx;   // 上一帧鼠标屏幕像素位置（UnityEngine.Input.mousePosition）

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
            try
            {
                UpdateDrag();
            }
            catch (Exception ex)
            {
                ModLog.Warn("按钮拖拽异常（已忽略）：" + ex.Message);
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

            // 左键点击启动；右键拖拽完全由 Update 每帧轮询 Input.mousePosition 处理（见 UpdateDrag），
            // 不依赖按钮的 mouseDown 事件（反射确认该版本按钮事件对右键不可靠）
            btn.eventClick += OnLaunchClickedInstance;

            // 默认位置：屏幕右上角，留 16 像素边距（UIView 用 fixedWidth 基准宽度定位）
            btn.relativePosition = new Vector3((float)_view.fixedWidth - btn.width - 16f, 16f);
            _button = btn;
            ModLog.Info("已在游戏界面创建按钮（180×30，右键拖拽）：" + text);
        }

        /// <summary>
        /// 每帧轮询拖拽（纯轮询方案，完全绕开 UI 事件捕获限制）：
        /// - 右键"刚按下"且鼠标在按钮矩形内 → 进入拖拽；
        /// - 拖拽中按屏幕像素增量移动按钮：**X 同向、Y 反向**
        ///   （Input.mousePosition 屏幕像素 Y 向上，UIView relativePosition Y 向下，必须翻转，
        ///   否则会出现"鼠标上移按钮下移"的反向问题——上一版漏了翻转）；
        /// - 右键在任何位置松开都会被检测到（GetMouseButton(1)==false），状态不残留；
        /// - 屏幕像素增量 ÷ UI 缩放比 → 换算到 UIView 基准坐标。
        /// </summary>
        private void UpdateDrag()
        {
            if (_button == null) return;

            bool rightDown = Input.GetMouseButton(1);
            bool wasDown = _rightWasDown;
            _rightWasDown = rightDown;

            // 右键已松开（无论在哪松开）：结束拖拽，杜绝状态残留
            if (!rightDown)
            {
                _dragRightButton = false;
                _dragging = false;
                return;
            }

            // 右键刚按下的这一帧：鼠标落在按钮矩形内才进入拖拽
            if (!wasDown)
            {
                _dragRightButton = IsMouseOverButton();
                _dragging = false;
                if (_dragRightButton)
                {
                    _lastMousePx = Input.mousePosition;
                }
                return;
            }

            if (!_dragRightButton) return;

            Vector3 mp = Input.mousePosition;
            float scale = (float)Screen.width / (float)_view.fixedWidth;
            if (scale <= 0f) scale = 1f;
            float dx = (mp.x - _lastMousePx.x) / scale;   // X 同向
            float dy = -(mp.y - _lastMousePx.y) / scale;  // Y 翻转（屏幕像素 Y 向上 → UIView Y 向下）

            if (!_dragging)
            {
                // 未判定拖拽前，先按位移阈值判断
                if (Mathf.Abs(dx * scale) < DragThreshold && Mathf.Abs(dy * scale) < DragThreshold)
                {
                    _lastMousePx = mp;
                    return;
                }
                _dragging = true;
            }

            Vector3 p = _button.relativePosition + new Vector3(dx, dy);
            // 限制在屏幕范围内，避免拖出可视区找不回来
            p.x = Mathf.Clamp(p.x, 0f, (float)_view.fixedWidth - _button.width);
            p.y = Mathf.Clamp(p.y, 0f, (float)_view.fixedHeight - _button.height);
            _button.relativePosition = p;

            _lastMousePx = mp;
        }

        /// <summary>鼠标屏幕像素是否落在按钮矩形内（换算：基准坐标×缩放，Y 翻转）</summary>
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
            _rightWasDown = false;
        }
    }
}
