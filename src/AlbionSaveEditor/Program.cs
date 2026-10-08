// Web host for development and for running the editor in a browser: `dotnet run` -> http://localhost:5080.
// All API/hosting code lives in AlbionSaveEditor.Server (shared with the Windows desktop app).
using AlbionSaveEditor;

var app = EditorHost.Build(args);
app.Run();

/// <summary>Exposed for WebApplicationFactory in tests.</summary>
public partial class Program;
