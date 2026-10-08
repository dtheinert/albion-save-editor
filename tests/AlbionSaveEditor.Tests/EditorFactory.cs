using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AlbionSaveEditor.Tests;

/// <summary>Test host without game data (no ALBION_PATH, no auto-detection), so item names are empty.</summary>
public sealed class EditorFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Albion:GamePath", "");
        builder.UseSetting("Albion:AutoDetect", "false");
    }
}
