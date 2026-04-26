using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Translate;

internal static class Program
{
    private const string SearchInputScript = "(() => { const ipt = document.querySelector('#search_input'); if (ipt) { window.scrollTo(0, 0); ipt.focus(); ipt.select(); } })();";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int SwHide = 0;
    private const string TargetUrl = "https://dict.youdao.com/result?word=.";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "Translate";
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkShift = 0x10;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const uint VkLeft = 0x25;
    private const uint VkUp = 0x26;
    private const uint VkRight = 0x27;
    private const uint VkDown = 0x28;
    private const uint VkEscape = 0x1B;
    private const uint VkT = 0x54;
    private const int WindowMoveStep = 20;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;
    private const int WmNclbuttondown = 0x00A1;
    private const int WmNchittest = 0x0084;
    private const int HtCaption = 0x0002;
    private const int HtClient = 0x0001;
    private const int HtLeft = 0x000A;
    private const int HtRight = 0x000B;
    private const int HtTop = 0x000C;
    private const int HtTopLeft = 0x000D;
    private const int HtTopRight = 0x000E;
    private const int HtBottom = 0x000F;
    private const int HtBottomLeft = 0x0010;
    private const int HtBottomRight = 0x0011;
    private const int TitleBarHeight = 32;
    private const int CaptionButtonWidth = 46;
    private const int HelpButtonWidth = 52;
    private const int ResizeBorderThickness = 8;

    private static readonly nint HwndTopMost = new(-1);
    private static readonly nint HwndNotTopMost = new(-2);

    private static LowLevelKeyboardProc? _keyboardProc;
    private static nint _keyboardHookHandle;
    private static ShellForm.MoveDirection? _activeMoveDirection;

    private delegate nint LowLevelKeyboardProc(int nCode, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    [STAThread]
    private static void Main()
    {
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != 0)
        {
            ShowWindow(consoleWindow, SwHide);
        }

        ApplicationConfiguration.Initialize();
        RegisterKeyboardHook();
        try
        {
            using var form = new ShellForm();
            Application.Run(form);
        }
        finally
        {
            UnregisterKeyboardHook();
        }
    }

    private sealed class ShellForm : Form
    {
        public enum MoveDirection
        {
            Left,
            Up,
            Right,
            Down
        }

        private readonly WebView2 _webView;
        private readonly string _userDataFolder;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _notifyMenu;
        private readonly System.Windows.Forms.Timer _moveTimer;
        private readonly Panel _titleBar;
        private readonly PictureBox _titleIcon;
        private readonly Label _titleLabel;
        private readonly Button _helpButton;
        private readonly Button _minimizeButton;
        private readonly Button _closeButton;
        private readonly Bitmap? _cachedTitleIcon;
        private bool _hasCompletedFirstShow;
        private MoveDirection? _currentMoveDirection;
        private CoreWebView2Environment? _webViewEnvironment;

        public static ShellForm? Instance { get; private set; }

        public ShellForm()
        {
            Instance = this;
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Text = Assembly.GetExecutingAssembly().GetName().Name ?? "Translate";
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(1);
            Width = 620;
            Height = 520;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(620, 520);
            Icon = LoadApplicationIcon();
            BackColor = SystemColors.AppWorkspace;
            _userDataFolder = Path.Combine(Path.GetTempPath(), "Translate", "WebView2");
            _cachedTitleIcon = Icon?.ToBitmap();

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.White
            };

            _titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = TitleBarHeight,
                BackColor = SystemColors.Control
            };
            _titleBar.MouseDown += TitleBarMouseDown;

            _titleIcon = new PictureBox
            {
                Dock = DockStyle.Left,
                Width = TitleBarHeight,
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = _cachedTitleIcon
            };
            _titleIcon.MouseDown += TitleBarMouseDown;

            _titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Text = Text,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            _titleLabel.MouseDown += TitleBarMouseDown;

            _helpButton = CreateTitleBarButton("Help", HelpButtonWidth);
            _helpButton.Click += (_, _) => ShowHelpDialog();

            _minimizeButton = CreateTitleBarButton("—", CaptionButtonWidth);
            _minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;

            _closeButton = CreateTitleBarButton("✕", CaptionButtonWidth);
            _closeButton.Click += (_, _) => HideMainWindow();

            _notifyMenu = new ContextMenuStrip();
            _notifyMenu.Items.Add("显示/隐藏", null, (_, _) => ToggleVisibility());
            _notifyMenu.Items.Add(new ToolStripMenuItem("开机自启")
            {
                Checked = IsAutoStartEnabled()
            });
            _notifyMenu.Items[1].Click += (_, _) => ToggleAutoStart();
            _notifyMenu.Items.Add(new ToolStripSeparator());
            _notifyMenu.Items.Add("退出", null, (_, _) => ExitApplication());

            _notifyIcon = new NotifyIcon
            {
                Text = Text,
                Visible = true,
                ContextMenuStrip = _notifyMenu,
                Icon = Icon ?? SystemIcons.Application
            };
            _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

            _moveTimer = new System.Windows.Forms.Timer
            {
                Interval = 32
            };
            _moveTimer.Tick += (_, _) => MoveWindowStep();

            _titleBar.Controls.Add(_titleLabel);
            _titleBar.Controls.Add(_titleIcon);
            _titleBar.Controls.Add(_helpButton);
            _titleBar.Controls.Add(_minimizeButton);
            _titleBar.Controls.Add(_closeButton);
            Controls.Add(_webView);
            Controls.Add(_titleBar);
            Load += OnLoadAsync;
            Resize += OnResize;
            FormClosing += OnFormClosing;
            Shown += OnShown;
            UpdateTitleBarButtons();
        }

        private async void OnLoadAsync(object? sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_userDataFolder);
                _webViewEnvironment ??= await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
                await _webView.EnsureCoreWebView2Async(_webViewEnvironment);
                var injectedScript = LoadEmbeddedScript();
                if (!string.IsNullOrWhiteSpace(injectedScript))
                {
                    await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(injectedScript);
                }
                _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                _webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                _webView.Source = new Uri(TargetUrl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                MessageBox.Show(this, ex.Message, "加载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var typeProperty))
                {
                    return;
                }

                var type = typeProperty.GetString();
                switch (type)
                {
                    case "ready":
                    case "show":
                        ShowMainWindow();
                        ReplyToWeb("ok");
                        break;
                    case "toggle":
                        ToggleVisibility();
                        ReplyToWeb("ok");
                        break;
                    case "hide":
                    case "close":
                        HideMainWindow();
                        ReplyToWeb("ok");
                        break;
                    case "exit":
                        ReplyToWeb("ok");
                        ExitApplication();
                        break;
                    case "isAutoStartEnabled":
                        ReplyToWeb(IsAutoStartEnabled() ? "true" : "false");
                        break;
                    case "setAutoStartEnabled":
                        var enabled = root.TryGetProperty("enabled", out var enabledProperty) && enabledProperty.GetBoolean();
                        SetAutoStartEnabled(enabled);
                        if (_notifyMenu.Items[1] is ToolStripMenuItem menuItem)
                        {
                            menuItem.Checked = IsAutoStartEnabled();
                        }
                        ReplyToWeb("ok");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                ReplyToWeb("error");
            }
        }

        private void ReplyToWeb(string message)
        {
            _webView.CoreWebView2?.PostWebMessageAsString(message);
        }

        private static string LoadEmbeddedScript()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("app.js");
            if (stream is null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private void OnResize(object? sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                HideMainWindow();
            }
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideMainWindow();
            }
        }

        private void OnShown(object? sender, EventArgs e)
        {
            _hasCompletedFirstShow = true;
        }

        public void ToggleVisibility()
        {
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                if (IsWindowForeground())
                {
                    HideMainWindow();
                }
                else
                {
                    ShowMainWindow();
                }
            }
            else
            {
                ShowMainWindow();
            }
        }

        public void ShowMainWindow()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            UpdateTitleBarButtons();
            SetWindowPos(Handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
            Activate();
            BringToFront();
            SetWindowPos(Handle, HwndNotTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
            _webView.Focus();
            _ = FocusSearchInputAsync();

            // 恢复 WebView2 活动状态
            if (_webView.CoreWebView2 is not null)
            {
                try
                {
                    _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                }
                catch
                {
                    // 忽略错误
                }
            }
        }

        public void StartWindowMove(MoveDirection direction)
        {
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            _currentMoveDirection = direction;
            MoveWindowStep();
            _moveTimer.Start();
        }

        public void StopWindowMove(MoveDirection direction)
        {
            if (_currentMoveDirection != direction)
            {
                return;
            }

            _currentMoveDirection = null;
            _moveTimer.Stop();
        }

        private void HideMainWindow()
        {
            if (!_hasCompletedFirstShow)
            {
                return;
            }

            // 暂停 WebView2 以降低 CPU/GPU 使用率
            if (_webView.CoreWebView2 is not null)
            {
                try
                {
                    _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    // 通过执行空脚本保持连接但不触发渲染
                }
                catch
                {
                    // 忽略错误
                }
            }

            // 停止移动计时器以减少 CPU 唤醒
            _moveTimer.Stop();

            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Hide();
        }

        private bool IsWindowForeground()
        {
            return IsHandleCreated && GetForegroundWindow() == Handle;
        }

        public bool IsVisibleInForeground()
        {
            return Visible && WindowState != FormWindowState.Minimized && IsWindowForeground();
        }

        public void HandleHideKey(uint virtualKey)
        {
            if (virtualKey == VkEscape)
            {
                HideMainWindow();
                return;
            }
        }

        private void ToggleAutoStart()
        {
            SetAutoStartEnabled(!IsAutoStartEnabled());
            if (_notifyMenu.Items[1] is ToolStripMenuItem menuItem)
            {
                menuItem.Checked = IsAutoStartEnabled();
            }
        }

        private void ExitApplication()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyMenu.Dispose();
            _moveTimer.Dispose();
            _cachedTitleIcon?.Dispose();
            _webView.Dispose();
            Instance = null;
            Close();
            Application.ExitThread();
        }

        private async Task FocusSearchInputAsync()
        {
            if (_webView.CoreWebView2 is null)
            {
                return;
            }

            try
            {
                _webView.Focus();
                await _webView.CoreWebView2.ExecuteScriptAsync(SearchInputScript);
                _webView.Focus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _moveTimer.Dispose();
                _cachedTitleIcon?.Dispose();
                _webView.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Icon? LoadApplicationIcon()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var iconStream = assembly.GetManifestResourceStream("app.ico");
            return iconStream is null ? null : new Icon(iconStream);
        }

        private Button CreateTitleBarButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Dock = DockStyle.Right,
                Width = width,
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Margin = Padding.Empty,
                FlatAppearance =
                {
                    BorderSize = 0
                }
            };
        }

        private void UpdateTitleBarButtons()
        {
            _titleLabel.Text = Text;
        }

        private void ShowHelpDialog()
        {
            MessageBox.Show(
                this,
                "显示/隐藏快捷键：Ctrl + Shift + Win + T\n隐藏快捷键：Esc\n移动窗口快捷键：Alt + 方向键",
                "帮助",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void TitleBarMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(Handle, WmNclbuttondown, HtCaption, 0);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmNchittest)
            {
                base.WndProc(ref m);
                if (WindowState == FormWindowState.Normal)
                {
                    var clientPoint = PointToClient(new Point(
                        unchecked((short)(long)m.LParam),
                        unchecked((short)((long)m.LParam >> 16))));

                    m.Result = (nint)GetResizeHitTest(clientPoint);
                    if ((int)m.Result != HtClient)
                    {
                        return;
                    }
                }

                return;
            }

            base.WndProc(ref m);
        }

        private int GetResizeHitTest(Point point)
        {
            var left = point.X <= ResizeBorderThickness;
            var right = point.X >= ClientSize.Width - ResizeBorderThickness;
            var top = point.Y <= ResizeBorderThickness;
            var bottom = point.Y >= ClientSize.Height - ResizeBorderThickness;

            if (left && top)
            {
                return HtTopLeft;
            }

            if (right && top)
            {
                return HtTopRight;
            }

            if (left && bottom)
            {
                return HtBottomLeft;
            }

            if (right && bottom)
            {
                return HtBottomRight;
            }

            if (left)
            {
                return HtLeft;
            }

            if (right)
            {
                return HtRight;
            }

            if (top)
            {
                return HtTop;
            }

            if (bottom)
            {
                return HtBottom;
            }

            return HtClient;
        }

        private void MoveWindowStep()
        {
            if (_currentMoveDirection is null)
            {
                _moveTimer.Stop();
                return;
            }

            var bounds = Bounds;
            switch (_currentMoveDirection)
            {
                case MoveDirection.Left:
                    bounds.X -= WindowMoveStep;
                    break;
                case MoveDirection.Up:
                    bounds.Y -= WindowMoveStep;
                    break;
                case MoveDirection.Right:
                    bounds.X += WindowMoveStep;
                    break;
                case MoveDirection.Down:
                    bounds.Y += WindowMoveStep;
                    break;
            }

            Bounds = bounds;
        }
    }

    private static void RegisterKeyboardHook()
    {
        if (_keyboardHookHandle != 0)
        {
            return;
        }

        _keyboardProc = KeyboardHookCallback;
        var moduleHandle = GetModuleHandle(null);
        _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);
    }

    private static void UnregisterKeyboardHook()
    {
        if (_keyboardHookHandle == 0)
        {
            return;
        }

        UnhookWindowsHookEx(_keyboardHookHandle);
        _keyboardHookHandle = 0;
        _keyboardProc = null;
    }

    private static nint KeyboardHookCallback(int nCode, nuint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var hookData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if ((wParam == WmKeyDown || wParam == WmSysKeyDown))
            {
                var isTogglePressed = hookData.VkCode == VkT
                    && IsKeyPressed(VkControl)
                    && IsKeyPressed(VkShift)
                    && (IsKeyPressed(VkLWin) || IsKeyPressed(VkRWin));

                if (isTogglePressed && ShellForm.Instance is not null)
                {
                    ShellForm.Instance.BeginInvoke(ShellForm.Instance.ToggleVisibility);
                    return 1;
                }

                if (ShellForm.Instance is not null
                    && ShellForm.Instance.IsVisibleInForeground()
                    && IsHideTestKey(hookData.VkCode))
                {
                    ShellForm.Instance.BeginInvoke(() => ShellForm.Instance.HandleHideKey(hookData.VkCode));
                    return 1;
                }

                if (IsKeyPressed(VkMenu) && TryGetMoveDirection(hookData.VkCode, out var moveDirection) && ShellForm.Instance is not null && ShellForm.Instance.IsVisibleInForeground())
                {
                    _activeMoveDirection = moveDirection;
                    ShellForm.Instance.BeginInvoke(() => ShellForm.Instance.StartWindowMove(moveDirection));
                    return 1;
                }
            }

            if ((wParam == WmKeyUp || wParam == WmSysKeyUp) && ShellForm.Instance is not null)
            {
                if (TryGetMoveDirection(hookData.VkCode, out var releasedDirection) && _activeMoveDirection == releasedDirection)
                {
                    _activeMoveDirection = null;
                    ShellForm.Instance.BeginInvoke(() => ShellForm.Instance.StopWindowMove(releasedDirection));
                }

                if (hookData.VkCode == VkMenu && _activeMoveDirection is { } activeDirection)
                {
                    _activeMoveDirection = null;
                    ShellForm.Instance.BeginInvoke(() => ShellForm.Instance.StopWindowMove(activeDirection));
                }
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private static bool TryGetMoveDirection(uint virtualKey, out ShellForm.MoveDirection direction)
    {
        direction = virtualKey switch
        {
            VkLeft => ShellForm.MoveDirection.Left,
            VkUp => ShellForm.MoveDirection.Up,
            VkRight => ShellForm.MoveDirection.Right,
            VkDown => ShellForm.MoveDirection.Down,
            _ => default
        };

        return virtualKey is VkLeft or VkUp or VkRight or VkDown;
    }

    private static bool IsHideTestKey(uint virtualKey)
    {
        return virtualKey == VkEscape;
    }

    private static bool IsKeyPressed(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = NormalizeAutoStartPath(key?.GetValue(AutoStartValueName) as string);
        var processPath = NormalizeAutoStartPath(Environment.ProcessPath);
        return !string.IsNullOrEmpty(processPath)
            && string.Equals(value, processPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetAutoStartEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                key.SetValue(AutoStartValueName, QuoteCommandPath(processPath));
            }
        }
        else
        {
            key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
        }
    }

    private static string NormalizeAutoStartPath(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('"');
    }

    private static string QuoteCommandPath(string path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : $"\"{path}\"";
    }
}
