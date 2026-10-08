using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace AlbionSaveEditor.Desktop;

/// <summary>
/// Desktop entry point: starts the editor server in-process on 127.0.0.1 (free port) and shows it in a
/// WebView2 window. Without the WebView2 runtime the default browser is opened instead and a small window
/// keeps the server alive. Closing the window stops the server.
///
/// Options: --port N (fixed port instead of a random one), --self-test SAVEFILE OUTDIR (automated check:
/// uploads SAVEFILE through the UI's WebView2, downloads it via a blob link like the "Download" button and
/// verifies the saved file; exit code 0 = pass). Other arguments go to ASP.NET Core configuration,
/// e.g. --Albion:GamePath="D:\Games\Albion".
/// </summary>
internal static class Program
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlbionSaveEditor");

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var hostArgs = new List<string>();
        int port = 0;
        SelfTest? selfTest = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int p))
            {
                port = p; i++;
            }
            else if (args[i] == "--self-test" && i + 2 < args.Length)
            {
                selfTest = new SelfTest(Path.GetFullPath(args[i + 1]), Path.GetFullPath(args[i + 2]));
                i += 2;
            }
            else hostArgs.Add(args[i]);
        }

        using var log = new FileLoggerProvider(Path.Combine(DataDirectory, "editor.log"));
        WebApplication app;
        string url;
        try
        {
            app = EditorHost.Build(hostArgs.ToArray(), new EditorHostOptions
            {
                Port = port,
                EmbeddedAssetsOnly = true,
                ContentRootPath = AppContext.BaseDirectory,
                ConfigureLogging = l =>
                {
                    l.ClearProviders();
                    l.AddProvider(log);
                },
            });
            app.StartAsync().GetAwaiter().GetResult();
            url = app.Urls.First().TrimEnd('/') + "/"; // actual address (port 0 -> assigned port)
        }
        catch (Exception e)
        {
            log.Write("Desktop", LogLevel.Error, e.ToString());
            MessageBox.Show("The editor could not start:\n\n" + e.Message, "Albion Save Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        log.Write("Desktop", LogLevel.Information, $"Editor UI at {url} (version {EditorHost.Version})");

        int exitCode = 0;
        try
        {
            string? webView = WebViewVersion();
            log.Write("Desktop", LogLevel.Information, webView != null ? $"WebView2 runtime {webView}" : "WebView2 runtime not found; using the default browser");
            if (webView != null)
            {
                using var form = new MainForm(url, DataDirectory, selfTest, log);
                Application.Run(form);
                exitCode = form.ExitCode;
            }
            else
            {
                if (selfTest != null)
                {
                    exitCode = 2; // the self-test needs WebView2; only the detection is exercised
                }
                else
                {
                    using var form = new FallbackForm(url, openBrowser: true);
                    Application.Run(form);
                }
            }
        }
        finally
        {
            log.Write("Desktop", LogLevel.Information, "Shutting down");
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                app.StopAsync(cts.Token).GetAwaiter().GetResult();
            ((IAsyncDisposable)app).DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        // Make sure nothing (e.g. a lingering foreground thread) keeps the process alive after the window closed.
        Environment.Exit(exitCode);
        return exitCode;
    }

    private static string? WebViewVersion()
    {
        try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException) { return null; }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException) { return null; }
    }

    public static Icon? LoadIcon()
    {
        try
        {
            using var s = typeof(Program).Assembly.GetManifestResourceStream("app.ico");
            return s == null ? null : new Icon(s);
        }
        catch (Exception e) when (e is ArgumentException or IOException) { return null; }
    }
}

internal sealed record SelfTest(string SaveFile, string OutDir);
