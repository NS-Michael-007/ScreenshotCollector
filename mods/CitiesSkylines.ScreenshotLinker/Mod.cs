using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using ColossalFramework.UI;
using HarmonyLib;
using ICities;

namespace CitiesSkylines.ScreenshotLinker
{
    public class Mod : IUserMod
    {
        public const string HarmonyId = "com.wangsiji.screenshotlinker";
        private ScreenshotWatcher _watcher;
        private Harmony _harmony;
        private bool _harmonyActive;
        private UnityEngine.GameObject _uiObject;

        private UIHelperBase _currentHelper;
        private ScreenshotLinkerConfig _currentConfig;

        public Mod()
        {
            ModLog.Info("模组实例已创建（DLL 加载成功，等待启用）");
        }

        // 🚀 修复：去掉 => 语法
        public string Name
        {
            get { return "截图自动收集器联动（ScreenshotLinker）"; }
        }

        public string Description
        {
            get { return string.Empty; }
        }

        public void OnEnabled()
        {
            ModLog.Info("模组已启用（OnEnabled）");
            ScreenshotLinkerConfig.EnsureLoaded();
            var cfg = ScreenshotLinkerConfig.Current;
            SocketSender.Port = cfg.Port;

            if (cfg.UseHarmonyPatch) _harmonyActive = TryPatchScreenshot();
            if (cfg.UseFolderWatch)
            {
                if (_watcher == null) _watcher = new ScreenshotWatcher();
                _watcher.Start();
            }

            if (ReferenceEquals(_uiObject, null))
            {
                try
                {
                    _uiObject = new UnityEngine.GameObject("ScreenshotLinkerUIRoot");
                    _uiObject.AddComponent<ScreenshotLinkerUI>();
                    UnityEngine.Object.DontDestroyOnLoad(_uiObject);
                    ModLog.Info("游戏内按钮 UI 挂载成功！");
                }
                catch (Exception ex)
                {
                    ModLog.Warn("挂载游戏内按钮失败：" + ex.ToString());
                }
            }

            bool heartbeatSent = SocketSender.SendHeartbeat();
            if (heartbeatSent) ModLog.Info("心跳包发送成功，桌面端已接收。");
            else ModLog.Info("心跳包发送失败（可能桌面端未启动）。");
        }

        public void OnDisabled()
        {
            if (_watcher != null) _watcher.Stop();
            try { if (_harmony != null) _harmony.UnpatchAll(HarmonyId); } catch { }
            if (!ReferenceEquals(_uiObject, null))
            {
                UnityEngine.Object.Destroy(_uiObject);
                _uiObject = null;
            }
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            ScreenshotLinkerConfig.EnsureLoaded();
            _currentConfig = ScreenshotLinkerConfig.Current;
            _currentHelper = helper;
            var cfg = _currentConfig;

            helper.AddGroup("桌面端连接");
            helper.AddTextfield("桌面端端口", cfg.Port.ToString(), OnPortTextChangedEmpty, OnPortTextSubmitted);

            helper.AddGroup("捕获通道");
            helper.AddCheckbox("启用 Harmony 补丁", cfg.UseHarmonyPatch, OnHarmonyCheckChanged);
            helper.AddCheckbox("启用目录监听兜底", cfg.UseFolderWatch, OnFolderWatchCheckChanged);

            helper.AddSpace(8);

            helper.AddGroup("一键启动（游戏界面按钮）");
            helper.AddTextfield("程序路径 (若“浏览”无响应，请直接粘贴文件夹路径)", cfg.GetDesktopAppPath(), OnPathTextChangedEmpty, OnPathTextSubmitted);
            helper.AddButton("浏览... 选择程序 (全屏下可能弹不出来)", OnBrowseButtonClicked);
            helper.AddTextfield("游戏内按钮文字（最长 8 字符）", cfg.GetButtonText(), OnButtonTextChangedEmpty, OnButtonTextSubmitted);

            try
            {
                UIComponent root = FindHelperRoot(helper);
                if (!ReferenceEquals(root, null))
                {
                    UITextField pathTextField = FindTextFieldByTitle(root, "程序路径");
                    if (!ReferenceEquals(pathTextField, null))
                    {
                        pathTextField.width = 600f;
                        pathTextField.height = 26f;
                        pathTextField.textScale = 0.8f;

                        if (!ReferenceEquals(pathTextField.parent, null))
                        {
                            UIPanel parentPanel = pathTextField.parent as UIPanel;
                            if (!ReferenceEquals(parentPanel, null))
                            {
                                parentPanel.width = 620f;
                                parentPanel.autoLayout = false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { ModLog.Warn("UI调整失败：" + ex.Message); }

            helper.AddSpace(8);
            helper.AddButton("保存配置", OnSaveButtonClicked);
        }

        private void OnPortTextChangedEmpty(string text) { }
        private void OnPortTextSubmitted(string text)
        {
            int v;
            if (int.TryParse(text, out v) && v > 0 && v < 65536) { _currentConfig.Port = v; _currentConfig.Save(); }
        }

        private void OnHarmonyCheckChanged(bool isChecked)
        {
            _currentConfig.UseHarmonyPatch = isChecked; _currentConfig.Save();
        }

        private void OnFolderWatchCheckChanged(bool isChecked)
        {
            _currentConfig.UseFolderWatch = isChecked; _currentConfig.Save();
        }

        private void OnPathTextChangedEmpty(string text) { }
        private void OnPathTextSubmitted(string text)
        {
            if (!string.IsNullOrEmpty(text)) text = text.Trim().Trim('"', '\'');
            _currentConfig.DesktopAppPath = text;
            _currentConfig.Save();
        }

        private void OnBrowseButtonClicked()
        {
            try
            {
                string selectedPath = ShowOpenFileDialog("选择 ScreenshotCollector.exe", "可执行文件 (*.exe)\0*.exe\0所有文件 (*.*)\0*.*\0");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    _currentConfig.DesktopAppPath = selectedPath;
                    _currentConfig.Save();

                    UIComponent root = FindHelperRoot(_currentHelper);
                    if (!ReferenceEquals(root, null))
                    {
                        UITextField pathField = FindTextFieldByTitle(root, "程序路径");
                        if (!ReferenceEquals(pathField, null)) pathField.text = selectedPath;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("打开文件选择框失败：" + ex.Message);
            }
        }

        private void OnButtonTextChangedEmpty(string text) { }
        private void OnButtonTextSubmitted(string text)
        {
            if (!ReferenceEquals(text, null) && text.Length > ScreenshotLinkerConfig.ButtonTextMaxLength)
                text = text.Substring(0, ScreenshotLinkerConfig.ButtonTextMaxLength);
            _currentConfig.ButtonText = text;
            _currentConfig.Save();
        }

        private void OnSaveButtonClicked()
        {
            _currentConfig.Save();
        }

        public static void LaunchDesktopApp()
        {
            string path = ScreenshotLinkerConfig.Current.GetDesktopAppPath();
            if (!string.IsNullOrEmpty(path)) path = path.Trim().Trim('"', '\'');

            if (Directory.Exists(path))
            {
                string autoExe = PathUtil.Join(path, "ScreenshotCollector.exe");
                if (File.Exists(autoExe)) path = autoExe;
            }
            else if (!path.ToLower().EndsWith(".exe") && File.Exists(path + ".exe"))
            {
                path += ".exe";
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ModLog.Error("启动失败：路径错误 -> " + path);
                return;
            }

            try
            {
                string exeDir = PathUtil.DirName(path);
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c start \"\" \"" + path + "\"",
                    WorkingDirectory = exeDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                System.Diagnostics.Process.Start(psi);
                ModLog.Info("已成功发送启动指令：" + path);
            }
            catch (Exception ex)
            {
                ModLog.Error("启动异常：" + ex.Message);
            }
        }

        private bool TryPatchScreenshot()
        {
            try
            {
                _harmony = new Harmony(HarmonyId);
                var type = Type.GetType("Game.ScreenshotManager, Assembly-CSharp");
                if (ReferenceEquals(type, null)) return false;
                var method = type.GetMethod("TakeScreenshot");
                if (ReferenceEquals(method, null)) return false;
                _harmony.Patch(method, postfix: new HarmonyMethod(typeof(Patches.ScreenshotPatch), nameof(Patches.ScreenshotPatch.Postfix)));
                return true;
            }
            catch { return false; }
        }

        private static UIComponent FindHelperRoot(UIHelperBase helper)
        {
            try
            {
                Type t = helper.GetType();
                FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                for (int i = 0; i < fields.Length; i++)
                {
                    if (typeof(UIComponent).IsAssignableFrom(fields[i].FieldType))
                    {
                        UIComponent v = fields[i].GetValue(helper) as UIComponent;
                        if (!ReferenceEquals(v, null)) return v;
                    }
                }
            }
            catch { }
            return null;
        }

        private static UITextField FindTextFieldByTitle(UIComponent root, string titleText)
        {
            try
            {
                System.Collections.Generic.IList<UIComponent> comps = root.components;
                for (int i = 0; i < comps.Count; i++)
                {
                    UIComponent comp = comps[i];
                    if (comp is UITextField && comp.name.Contains("Textfield"))
                    {
                        return comp as UITextField;
                    }
                    UITextField childField = FindTextFieldByTitle(comp, titleText);
                    if (!ReferenceEquals(childField, null)) return childField;
                }
            }
            catch { }
            return null;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class OpenFileName
        {
            public int structSize = 0; public IntPtr dlgOwner = IntPtr.Zero; public IntPtr instance = IntPtr.Zero;
            public String filter = null; public String customFilter = null; public int maxCustFilter = 0;
            public int filterIndex = 0; public String file = null; public int maxFile = 0;
            public String fileTitle = null; public int maxFileTitle = 0; public String initialDir = null;
            public String title = null; public int flags = 0; public short fileOffset = 0;
            public short fileExtension = 0; public String defExt = null; public IntPtr custData = IntPtr.Zero;
            public IntPtr hook = IntPtr.Zero; public String templateName = null; public IntPtr reservedPtr = IntPtr.Zero;
            public int reservedInt = 0; public int flagsEx = 0;
        }

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        public static string ShowOpenFileDialog(string title, string filter)
        {
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = filter;
            ofn.file = new string(new char[256]);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string(new char[64]);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.initialDir = "D:\\";
            ofn.title = title;
            ofn.dlgOwner = GetForegroundWindow();
            ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;

            if (GetOpenFileName(ofn)) return ofn.file;
            return null;
        }
    }
}