using System.Diagnostics;

namespace AlbionSaveEditor.Desktop;

/// <summary>
/// Shown when the WebView2 runtime is missing: the editor opens in the default browser and this small
/// window keeps the server running until it is closed.
/// </summary>
internal sealed class FallbackForm : Form
{
    private readonly string url;

    public FallbackForm(string url, bool openBrowser)
    {
        this.url = url;
        Text = "Albion Save Editor";
        Icon = Program.LoadIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(460, 150);

        var label = new Label
        {
            Text = "Editor running — close this to quit.\n\nThe editor opened in your web browser. " +
                   "If it did not, open this address:",
            AutoSize = false,
            Location = new Point(16, 14),
            Size = new Size(428, 64),
        };
        var link = new LinkLabel { Text = url, AutoSize = true, Location = new Point(16, 82) };
        link.LinkClicked += (_, _) => OpenBrowser();
        var quit = new Button { Text = "Quit", Location = new Point(364, 110), Size = new Size(80, 28), DialogResult = DialogResult.OK };
        quit.Click += (_, _) => Close();
        Controls.AddRange([label, link, quit]);
        AcceptButton = quit;

        if (openBrowser) Shown += (_, _) => OpenBrowser();
    }

    private void OpenBrowser()
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e)
        {
            MessageBox.Show(this, "Could not open the browser: " + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
