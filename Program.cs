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
    private const uint WmSysKeyDown = 0x0104;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const uint VkT = 0x54;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;

    private static readonly nint HwndTopMost = new(-1);
    private static readonly nint HwndNotTopMost = new(-2);

    private static LowLevelKeyboardProc? _keyboardProc;
    private static nint _keyboardHookHandle;

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
        using var form = new ShellForm();
        Application.Run(form);
        UnregisterKeyboardHook();
    }

    private sealed class ShellForm : Form
    {
        private readonly WebView2 _webView;
        private readonly string _userDataFolder;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _notifyMenu;
        private bool _hasCompletedFirstShow;

        public static ShellForm? Instance { get; private set; }

        public ShellForm()
        {
            Instance = this;
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Text = Assembly.GetExecutingAssembly().GetName().Name ?? "Translate";
            Width = 620;
            Height = 520;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(620, 520);
            Icon = LoadApplicationIcon();
            _userDataFolder = Path.Combine(Path.GetTempPath(), "Translate", "WebView2");

            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.White
            };

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

            Controls.Add(_webView);
            Load += OnLoadAsync;
            Resize += OnResize;
            FormClosing += OnFormClosing;
            Shown += OnShown;
        }

        private async void OnLoadAsync(object? sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_userDataFolder);
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
                await _webView.EnsureCoreWebView2Async(environment);
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
            catch
            {
                ReplyToWeb("error");
            }
        }

        private void ReplyToWeb(string message)
        {
            _webView.CoreWebView2.PostWebMessageAsString(message);
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
                HideMainWindow();
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
            SetWindowPos(Handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
            Activate();
            BringToFront();
            SetWindowPos(Handle, HwndNotTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
            _ = FocusSearchInputAsync();
        }

        private void HideMainWindow()
        {
            if (!_hasCompletedFirstShow)
            {
                return;
            }

            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Hide();
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
                await _webView.CoreWebView2.ExecuteScriptAsync("(() => { const ipt = document.querySelector('#search_input'); if (ipt) { window.scrollTo(0, 0); ipt.focus(); ipt.select(); } })();");
            }
            catch
            {
            }
        }

        private static Icon? LoadApplicationIcon()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var iconStream = assembly.GetManifestResourceStream("app.ico");
            return iconStream is null ? null : new Icon(iconStream);
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
        if (nCode >= 0 && (wParam == WmKeyDown || wParam == WmSysKeyDown))
        {
            var hookData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var isTogglePressed = hookData.VkCode == VkT
                && IsKeyPressed(VkControl)
                && IsKeyPressed(VkShift)
                && (IsKeyPressed(VkLWin) || IsKeyPressed(VkRWin));

            if (isTogglePressed && ShellForm.Instance is not null)
            {
                ShellForm.Instance.BeginInvoke(ShellForm.Instance.ToggleVisibility);
                return 1;
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private static bool IsKeyPressed(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(AutoStartValueName) as string;
        return string.Equals(value, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetAutoStartEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(AutoStartValueName, Environment.ProcessPath ?? string.Empty);
        }
        else
        {
            key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
        }
    }
}
