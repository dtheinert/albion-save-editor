using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AlbionSaveEditor.Desktop;

/// <summary>Main window: the editor UI in a WebView2 control.</summary>
internal sealed class MainForm : Form
{
    private readonly WebView2 web;
    private readonly Uri home;
    private readonly SelfTest? selfTest;
    private readonly FileLoggerProvider log;

    public int ExitCode { get; private set; }

    public MainForm(string url, string dataDirectory, SelfTest? selfTest, FileLoggerProvider log)
    {
        home = new Uri(url);
        this.selfTest = selfTest;
        this.log = log;

        Text = "Albion Save Editor";
        Icon = Program.LoadIcon();
        StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1400, 900);
        Size = new Size(Math.Min(1400, area.Width), Math.Min(900, area.Height));
        MinimumSize = new Size(800, 600);

        web = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(22, 38, 74),
            CreationProperties = new CoreWebView2CreationProperties
            {
                // user data (cache, local storage) must not go next to the exe, which may be read-only
                UserDataFolder = Path.Combine(dataDirectory, "WebView2"),
            },
        };
        Controls.Add(web);
        Load += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await web.EnsureCoreWebView2Async();
        }
        catch (Exception e)
        {
            Log(LogLevel.Error, "WebView2 failed: " + e);
            MessageBox.Show(this, "The built-in browser (WebView2) could not start:\n\n" + e.Message +
                $"\n\nThe editor will open in your web browser instead: {home}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            OpenExternal(home);
            ExitCode = 3;
            return;
        }

        var core = web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.DownloadStarting += OnDownloadStarting;
        core.NewWindowRequested += (_, e) =>
        {
            // links with target=_blank (e.g. credits in "About") open in the user's browser
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u)) OpenExternal(u);
        };
        core.NavigationStarting += (_, e) =>
        {
            // keep the window on the editor; anything else goes to the default browser
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"
                && !string.Equals(u.Authority, home.Authority, StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                OpenExternal(u);
            }
        };
        if (selfTest != null)
            core.NavigationCompleted += async (_, e) => await RunSelfTestAsync(e.IsSuccess);
        web.Source = home;
    }

    // ------------------------------------------------------------------ downloads

    /// <summary>
    /// "Download modified save" creates a blob link; WebView2 raises DownloadStarting for it.
    /// We ask where to save it (Save As dialog, original file name) and hide the default download UI.
    /// </summary>
    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        if (selfTest != null)
        {
            Directory.CreateDirectory(selfTest.OutDir);
            e.ResultFilePath = Path.Combine(selfTest.OutDir, Path.GetFileName(e.ResultFilePath));
            e.Handled = true;
            TrackDownload(e.DownloadOperation, e.ResultFilePath);
            return;
        }

        var deferral = e.GetDeferral();
        var context = SynchronizationContext.Current!;
        context.Post(_ =>
        {
            using (deferral)
            {
                string suggested = e.ResultFilePath;
                using var dialog = new SaveFileDialog
                {
                    Title = "Save the modified Albion save",
                    FileName = Path.GetFileName(suggested),
                    InitialDirectory = Path.GetDirectoryName(suggested),
                    Filter = "Albion save (SAVE.*)|SAVE.*|All files (*.*)|*.*",
                    FilterIndex = 2,
                    OverwritePrompt = true,
                    AddExtension = false,
                };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    e.ResultFilePath = dialog.FileName;
                    e.Handled = true;
                    TrackDownload(e.DownloadOperation, dialog.FileName);
                }
                else
                {
                    e.Cancel = true;
                }
            }
        }, null);
    }

    private void TrackDownload(CoreWebView2DownloadOperation op, string path)
    {
        Log(LogLevel.Information, $"Download started: {path}");
        op.StateChanged += (_, _) =>
        {
            switch (op.State)
            {
                case CoreWebView2DownloadState.Completed:
                    Log(LogLevel.Information, $"Download completed: {path} ({op.BytesReceived} bytes)");
                    if (selfTest != null) FinishSelfTest(path);
                    break;
                case CoreWebView2DownloadState.Interrupted:
                    Log(LogLevel.Warning, $"Download interrupted: {path} ({op.InterruptReason})");
                    if (selfTest != null) { ExitCode = 1; Close(); }
                    else MessageBox.Show(this, $"Saving the file failed ({op.InterruptReason}).", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
            }
        };
    }

    // ------------------------------------------------------------------ self-test

    private bool selfTestStarted;

    private async Task RunSelfTestAsync(bool navigationOk)
    {
        if (selfTestStarted || selfTest == null) return;
        selfTestStarted = true;
        var timeout = new System.Windows.Forms.Timer { Interval = 30000 };
        timeout.Tick += (_, _) => { Log(LogLevel.Error, "SELFTEST FAIL: timeout"); ExitCode = 1; Close(); };
        timeout.Start();

        string title = await web.CoreWebView2.ExecuteScriptAsync("document.title");
        string hasApp = await web.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('script[src*=\"app.js\"]')");
        Log(LogLevel.Information, $"SELFTEST page loaded={navigationOk} title={title} appScript={hasApp}");
        if (!navigationOk) { ExitCode = 1; Close(); return; }

        string b64 = Convert.ToBase64String(File.ReadAllBytes(selfTest.SaveFile));
        string name = Path.GetFileName(selfTest.SaveFile).Replace("'", "");
        // same steps as the UI: upload, fetch the rebuilt file, save it through a blob link
        string script = $$"""
            (async () => {
              const bytes = Uint8Array.from(atob('{{b64}}'), c => c.charCodeAt(0));
              const fd = new FormData();
              fd.append('file', new Blob([bytes]), '{{name}}');
              const up = await (await fetch('/api/saves', { method: 'POST', body: fd })).json();
              const blob = await (await fetch('/api/saves/' + up.sessionId + '/download')).blob();
              const url = URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = url; a.download = up.fileName;
              document.body.append(a); a.click(); a.remove();
              setTimeout(() => URL.revokeObjectURL(url), 10000);
            })();
            """;
        await web.CoreWebView2.ExecuteScriptAsync(script);
    }

    private void FinishSelfTest(string path)
    {
        bool same = File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(selfTest!.SaveFile));
        Log(same ? LogLevel.Information : LogLevel.Error, same ? $"SELFTEST PASS: {path} is identical to the input" : $"SELFTEST FAIL: {path} differs");
        ExitCode = same ? 0 : 1;
        Close();
    }

    // ------------------------------------------------------------------ helpers

    private void Log(LogLevel level, string message) => log.Write("Desktop", level, message);

    private static void OpenExternal(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); }
        catch (Exception) { /* no browser registered */ }
    }
}
