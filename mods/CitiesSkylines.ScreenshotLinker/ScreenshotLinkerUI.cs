using System;
using UnityEngine;
using ColossalFramework.UI;

namespace CitiesSkylines.ScreenshotLinker
{
    /// <summary>
    /// 游戏界面内的「一键启动桌面端」按钮组件（常驻 GameObject 上，跨场景保留）。
    /// 按钮文字取配置（可自定义，最长 8 字），宽度随文字自适应，位于屏幕右上角。
    /// 兼容游戏 Mono 的 .NET 3.5：仅用 .NET 1.x API + 游戏自带 UnityEngine/ColossalManaged。
    /// </summary>
    public class ScreenshotLinkerUI : MonoBehaviour
    {
        private UIButton _button;
        private UIView _view;
        private DateTime _lastTryUtc = DateTime.MinValue;

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

            // 文字取配置（已按 8 字上限截断），宽度随文字自适应
            string text = ScreenshotLinkerConfig.Current.GetButtonText();
            UIButton btn = (UIButton)_view.AddUIComponent(typeof(UIButton)); // 非泛型 API（兼容所有 ColossalManaged 版本）
            btn.name = "ScreenshotLinkerLaunchButton";
            btn.width = 120f + text.Length * 22f; // 上限 8 字 → 最宽约 296px
            btn.height = 36f;
            btn.text = text;
            btn.textScale = 0.85f;
            btn.textColor = new Color32(255, 255, 255, 255);
            btn.disabledTextColor = new Color32(160, 160, 160, 255);
            btn.normalBgSprite = "ButtonMenu";
            btn.hoveredBgSprite = "ButtonMenuHovered";
            btn.pressedBgSprite = "ButtonMenuPressed";
            btn.eventClick += OnLaunchClicked;
            // 屏幕右上角，留 16 像素边距（UIView 用 fixedWidth 基准宽度定位）
            btn.relativePosition = new Vector3((float)_view.fixedWidth - btn.width - 16f, 16f);
            _button = btn;
            ModLog.Info("已在游戏界面创建按钮：" + text);
        }

        private static void OnLaunchClicked(UIComponent component, UIMouseEventParameter eventParam)
        {
            Mod.LaunchDesktopApp();
        }

        private void OnDestroy()
        {
            _button = null;
            _view = null;
        }
    }
}
