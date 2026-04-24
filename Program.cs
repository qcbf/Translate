using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SharpWebview;
using SharpWebview.Content;

namespace Translate;

internal class Program
{
    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private const int SwHide = 0;

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
        webview.SetTitle(asm.GetName().Name)
            .SetSize(620, 520, WebviewHint.None)
            .InitScript(js)
            .Navigate(new UrlContent("https://dict.youdao.com/result?word=."))
            .Run();
    }
}