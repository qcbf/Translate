using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using SharpWebview;
using SharpWebview.Content;

namespace Translate;

internal class Program
{
    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nuint wParam, nint lParam);

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
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const int GwlWndProc = -4;
    private const uint WmClose = 0x0010;
    private const uint WmCommand = 0x0111;
    private const uint WmApp = 0x8000;
    private const uint WmTrayIcon = WmApp + 1;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const uint VkT = 0x54;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfChecked = 0x00000008;
    private const uint TpMLeftAlign = 0x0000;
    private const uint TpMRightButton = 0x0002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifState = 0x00000008;
    private const uint NisHidden = 0x00000001;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint ImageIcon = 1;
    private const uint LrDefaultSize = 0x00000040;
    private const uint LrLoadFromFile = 0x00000010;
    private const int IdiApplication = 0x7F00;
    private const uint MenuToggleWindow = 1001;
    private const uint MenuAutoStart = 1002;
    private const uint MenuExit = 1003;
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "Translate";

    private static Webview? _webview;
    private static nint _windowHandle;
    private static nint _previousWndProc;
    private static WndProcDelegate? _wndProcDelegate;
    private static LowLevelKeyboardProc? _keyboardProc;
    private static nint _keyboardHookHandle;
    private static NotifyIconData _notifyIconData;
    private static bool _notifyIconCreated;
    private static nint _notifyIconHandle;
    private static string? _notifyIconTempPath;

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nuint wParam, nint lParam);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint CbSize;
        public nint HWnd;
        public uint UId;
        public uint UFlags;
        public uint UCallbackMessage;
        public nint HIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string SzTip;

        public uint DwState;
        public uint DwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string SzInfo;

        public uint UTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string SzInfoTitle;

        public uint DwInfoFlags;
        public Guid GuidItem;
        public nint HBalloonIcon;
    }

    [STAThread]
    private static void Main()
    {
        var consoleWindow = GetConsoleWindow();
        if (consoleWindow != 0)
        {
            ShowWindow(consoleWindow, SwHide);
        }

        var asm = Assembly.GetExecutingAssembly();
        using var jsSteam = asm.GetManifestResourceStream("js")!;
        var jsBytes = new byte[jsSteam.Length];
        jsSteam.ReadExactly(jsBytes);
        var js = Encoding.UTF8.GetString(jsBytes);

        using var webview = new Webview();
        _webview = webview;

        webview.Dispatch(InitializeWindowHook);

        webview.SetTitle(asm.GetName().Name)
            .SetSize(620, 520, WebviewHint.None)
            .Bind("host", HandleHostMessage)
            .InitScript(js)
            .Navigate(new UrlContent("https://dict.youdao.com/result?word=."))
            .Run();

        RemoveNotifyIcon();
        _webview = null;
    }

    private static void HandleHostMessage(string id, string payload)
    {
        if (_webview is null)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

            switch (type)
            {
                case "ready":
                    _webview.Return(id, RPCResult.Success, "true");
                    break;
                case "toggle":
                    ToggleWindowVisibility();
                    _webview.Return(id, RPCResult.Success, "true");
                    break;
                case "show":
                    ShowMainWindow();
                    _webview.Return(id, RPCResult.Success, "true");
                    break;
                case "hide":
                    HideMainWindow();
                    _webview.Return(id, RPCResult.Success, "true");
                    break;
                case "close":
                    HideMainWindow();
                    _webview.Return(id, RPCResult.Success, "true");
                    break;
                case "isAutoStartEnabled":
                    _webview.Return(id, RPCResult.Success, OperatingSystem.IsWindows() && IsAutoStartEnabled() ? "true" : "false");
                    break;
                case "setAutoStartEnabled":
                    var enabled = root.TryGetProperty("enabled", out var enabledElement) && enabledElement.GetBoolean();
                    if (OperatingSystem.IsWindows())
                    {
                        SetAutoStartEnabled(enabled);
                    }
                    _webview.Return(id, RPCResult.Success, enabled ? "true" : "false");
                    break;
                case "exit":
                    _webview.Return(id, RPCResult.Success, "true");
                    ExitApplication();
                    break;
                default:
                    _webview.Return(id, RPCResult.Error, "Unsupported action");
                    break;
            }
        }
        catch (Exception ex)
        {
            _webview.Return(id, RPCResult.Error, ex.Message);
        }
    }

    private static void InitializeWindowHook()
    {
        if (_webview is null || _previousWndProc != 0)
        {
            return;
        }

        _windowHandle = _webview.GetWindow();
        if (_windowHandle == 0)
        {
            return;
        }

        _wndProcDelegate = CustomWndProc;
        var newWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _previousWndProc = Environment.Is64BitProcess
            ? SetWindowLongPtr(_windowHandle, GwlWndProc, newWndProc)
            : SetWindowLong32(_windowHandle, GwlWndProc, newWndProc.ToInt32());

        CreateNotifyIcon();
        RegisterKeyboardHook();
    }

    private static nint CustomWndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == WmTrayIcon)
        {
            if ((uint)lParam == WmLButtonUp)
            {
                ToggleWindowVisibility();
                return 0;
            }

            if ((uint)lParam == WmRButtonUp)
            {
                ShowNotifyIconMenu(hWnd);
                return 0;
            }
        }

        if (msg == WmCommand)
        {
            switch ((uint)(wParam & 0xFFFF))
            {
                case MenuToggleWindow:
                    ToggleWindowVisibility();
                    return 0;
                case MenuAutoStart:
                    if (OperatingSystem.IsWindows())
                    {
                        SetAutoStartEnabled(!IsAutoStartEnabled());
                    }
                    return 0;
                case MenuExit:
                    ExitApplication();
                    return 0;
            }
        }

        if (msg == WmClose)
        {
            HideMainWindow();
            return 0;
        }

        return CallWindowProc(_previousWndProc, hWnd, msg, wParam, lParam);
    }

    private static void ToggleWindowVisibility()
    {
        if (IsWindowCurrentlyVisible())
        {
            HideMainWindow();
        }
        else
        {
            ShowMainWindow();
        }
    }

    private static bool IsWindowCurrentlyVisible()
    {
        return _windowHandle != 0 && IsWindowVisible(_windowHandle);
    }

    private static void CreateNotifyIcon()
    {
        if (_windowHandle == 0 || _notifyIconCreated)
        {
            return;
        }

        _notifyIconHandle = LoadNotifyIconHandle();
        _notifyIconData = new NotifyIconData
        {
            CbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            HWnd = _windowHandle,
            UId = 1,
            UFlags = NifMessage | NifIcon | NifTip,
            UCallbackMessage = WmTrayIcon,
            HIcon = _notifyIconHandle,
            SzTip = Assembly.GetExecutingAssembly().GetName().Name ?? "Translate",
            SzInfo = string.Empty,
            SzInfoTitle = string.Empty,
            GuidItem = Guid.Empty
        };

        _notifyIconCreated = Shell_NotifyIcon(NimAdd, ref _notifyIconData);
        if (!_notifyIconCreated)
        {
            return;
        }

        _notifyIconData.UFlags = NifState;
        _notifyIconData.DwState = 0;
        _notifyIconData.DwStateMask = NisHidden;
        Shell_NotifyIcon(NimModify, ref _notifyIconData);

        _notifyIconData.UFlags = NifMessage | NifIcon | NifTip;
        _notifyIconData.DwState = 0;
        _notifyIconData.DwStateMask = 0;
        Shell_NotifyIcon(NimModify, ref _notifyIconData);
    }

    private static nint LoadNotifyIconHandle()
    {
        var embeddedIconPath = ExtractEmbeddedIconToTempFile();
        if (!string.IsNullOrEmpty(embeddedIconPath))
        {
            var iconHandle = LoadImage(0, embeddedIconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            if (iconHandle != 0)
            {
                return iconHandle;
            }
        }

        var moduleHandle = GetModuleHandle(null);
        if (moduleHandle != 0)
        {
            var resourceHandle = LoadIcon(moduleHandle, new nint(1));
            if (resourceHandle != 0)
            {
                return resourceHandle;
            }
        }

        return LoadIcon(0, IdiApplication);
    }

    private static string? ExtractEmbeddedIconToTempFile()
    {
        if (!string.IsNullOrEmpty(_notifyIconTempPath) && File.Exists(_notifyIconTempPath))
        {
            return _notifyIconTempPath;
        }

        var assembly = Assembly.GetExecutingAssembly();
        using var iconStream = assembly.GetManifestResourceStream("app.ico");
        if (iconStream is null)
        {
            return null;
        }

        _notifyIconTempPath = Path.Combine(Path.GetTempPath(), $"Translate-{assembly.GetName().Version}-{Environment.ProcessId}.ico");

        using var fileStream = File.Create(_notifyIconTempPath);
        iconStream.CopyTo(fileStream);
        return _notifyIconTempPath;
    }

    private static void RemoveNotifyIcon()
    {
        if (_notifyIconCreated)
        {
            Shell_NotifyIcon(NimDelete, ref _notifyIconData);
            _notifyIconCreated = false;
        }

        if (_notifyIconHandle != 0)
        {
            DestroyIcon(_notifyIconHandle);
            _notifyIconHandle = 0;
        }

        if (!string.IsNullOrEmpty(_notifyIconTempPath) && File.Exists(_notifyIconTempPath))
        {
            try
            {
                File.Delete(_notifyIconTempPath);
            }
            catch
            {
            }

            _notifyIconTempPath = null;
        }
    }

    private static void ShowNotifyIconMenu(nint hWnd)
    {
        var menuHandle = CreatePopupMenu();
        if (menuHandle == 0)
        {
            return;
        }

        try
        {
            AppendMenu(menuHandle, MfString, MenuToggleWindow, IsWindowCurrentlyVisible() ? "隐藏" : "显示");
            AppendMenu(menuHandle, MfString | (OperatingSystem.IsWindows() && IsAutoStartEnabled() ? MfChecked : 0), MenuAutoStart, "自动启动");
            AppendMenu(menuHandle, MfSeparator, 0, string.Empty);
            AppendMenu(menuHandle, MfString, MenuExit, "退出");

            if (!GetCursorPos(out var point))
            {
                return;
            }

            SetForegroundWindow(hWnd);
            TrackPopupMenu(menuHandle, TpMLeftAlign | TpMRightButton, point.X, point.Y, 0, hWnd, 0);
        }
        finally
        {
            DestroyMenu(menuHandle);
        }
    }

    private static void RegisterKeyboardHook()
    {
        if (_keyboardHookHandle != 0)
        {
            return;
        }

        _keyboardProc = KeyboardHookCallback;
        using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = GetModuleHandle(currentModule?.ModuleName);
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

            if (isTogglePressed)
            {
                ToggleWindowVisibility();
                return 1;
            }
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private static bool IsKeyPressed(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static void HideMainWindow()
    {
        if (_webview is null)
        {
            return;
        }

        _webview.Dispatch(() =>
        {
            if (_windowHandle != 0)
            {
                ShowWindow(_windowHandle, SwHide);
            }
        });
    }

    private static void ShowMainWindow()
    {
        if (_webview is null)
        {
            return;
        }

        _webview.Dispatch(() =>
        {
            if (_windowHandle == 0)
            {
                return;
            }

            if (IsIconic(_windowHandle))
            {
                ShowWindow(_windowHandle, SwRestore);
            }
            else
            {
                ShowWindow(_windowHandle, SwShow);
            }

            BringWindowToTop(_windowHandle);
            SetForegroundWindow(_windowHandle);
        });
    }

    [SupportedOSPlatform("windows")]
    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(AutoStartValueName) as string;
        return string.Equals(value, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    private static void SetAutoStartEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var executablePath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "Translate.exe");
            key.SetValue(AutoStartValueName, executablePath, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
        }
    }

    private static void ExitApplication()
    {
        UnregisterKeyboardHook();
        RemoveNotifyIcon();

        _webview?.Terminate();
    }
}