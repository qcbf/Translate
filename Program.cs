using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using System.Drawing;
using Microsoft.Win32;
using SharpWebview;
using SharpWebview.Content;
using Forms = System.Windows.Forms;
using Application = System.Windows.Forms.Application;

namespace Translate;

internal class Program
{
    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

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
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nuint wParam, nint lParam);

    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int GwlWndProc = -4;
    private const uint WmClose = 0x0010;
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "Translate";

    private static Webview? _webview;
    private static Forms.NotifyIcon? _notifyIcon;
    private static nint _windowHandle;
    private static nint _previousWndProc;
    private static WndProcDelegate? _wndProcDelegate;

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nuint wParam, nint lParam);

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

        using var notifyIcon = CreateNotifyIcon(asm);
        _notifyIcon = notifyIcon;

        using var webview = new Webview();
        _webview = webview;

        webview.Dispatch(InitializeWindowHook);

        webview.SetTitle(asm.GetName().Name)
            .SetSize(620, 520, WebviewHint.None)
            .Bind("host", HandleHostMessage)
            .InitScript(js)
            .Navigate(new UrlContent("https://dict.youdao.com/result?word=."))
            .Run();

        notifyIcon.Visible = false;
        _notifyIcon = null;
        _webview = null;
    }

    private static Forms.NotifyIcon CreateNotifyIcon(Assembly asm)
    {
        var notifyIcon = new Forms.NotifyIcon
        {
            Text = asm.GetName().Name ?? "Translate",
            Visible = true,
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath)
        };

        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ToggleWindowVisibility();
            }
        };

        var menu = new Forms.ContextMenuStrip();
        var toggleItem = new Forms.ToolStripMenuItem("显示/隐藏");
        var autoStartItem = new Forms.ToolStripMenuItem("自动启动")
        {
            Checked = IsAutoStartEnabled(),
            CheckOnClick = false
        };
        var exitItem = new Forms.ToolStripMenuItem("退出");

        toggleItem.Click += (_, _) => ToggleWindowVisibility();
        autoStartItem.Click += (_, _) =>
        {
            var enabled = !IsAutoStartEnabled();
            SetAutoStartEnabled(enabled);
            autoStartItem.Checked = enabled;
        };
        exitItem.Click += (_, _) => ExitApplication();
        menu.Opening += (_, _) =>
        {
            toggleItem.Text = IsWindowCurrentlyVisible() ? "隐藏" : "显示";
            autoStartItem.Checked = IsAutoStartEnabled();
        };

        menu.Items.Add(toggleItem);
        menu.Items.Add(autoStartItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        notifyIcon.ContextMenuStrip = menu;
        return notifyIcon;
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
                    _webview.Return(id, RPCResult.Success, IsAutoStartEnabled() ? "true" : "false");
                    break;
                case "setAutoStartEnabled":
                    var enabled = root.TryGetProperty("enabled", out var enabledElement) && enabledElement.GetBoolean();
                    SetAutoStartEnabled(enabled);
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
    }

    private static nint CustomWndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
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
                ShowWindow(_windowHandle, 9);
            }
            else
            {
                ShowWindow(_windowHandle, SwShow);
            }

            BringWindowToTop(_windowHandle);
            SetForegroundWindow(_windowHandle);
        });
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
            var executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(AutoStartValueName, executablePath, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
        }
    }

    private static void ExitApplication()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
        }

        _webview?.Terminate();
    }
}