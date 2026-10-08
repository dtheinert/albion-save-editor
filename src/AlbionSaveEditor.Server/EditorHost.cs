using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlbLib.SaveGame;
using AlbionSaveEditor.Api;
using AlbionSaveEditor.Domain;
using AlbionSaveEditor.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;

namespace AlbionSaveEditor;

/// <summary>Options for <see cref="EditorHost.Build"/>.</summary>
public sealed class EditorHostOptions
{
    /// <summary>
    /// Port to listen on at 127.0.0.1 (0 = pick a free port). When null, the configured URLs are used
    /// (--urls / ASPNETCORE_URLS), defaulting to http://localhost:5080.
    /// </summary>
    public int? Port { get; init; }

    /// <summary>
    /// Serve the frontend from the embedded copy even if a wwwroot folder exists on disk.
    /// When false (web project), wwwroot on disk is preferred so frontend edits show up without a rebuild.
    /// </summary>
    public bool EmbeddedAssetsOnly { get; init; }

    /// <summary>Content root (defaults to the current directory, like WebApplication.CreateBuilder).</summary>
    public string? ContentRootPath { get; init; }

    /// <summary>Extra logging setup (e.g. a file logger for the desktop app).</summary>
    public Action<ILoggingBuilder>? ConfigureLogging { get; init; }
}

/// <summary>Builds the editor web application (API + frontend). Used by the web project and the desktop app.</summary>
public static class EditorHost
{
    public const long MaxUploadBytes = 16 * 1024 * 1024;
    public const int DefaultPort = 5080;

    private static readonly JsonSerializerOptions JsonIn = new(JsonSerializerDefaults.Web);

    public static string Version =>
        typeof(EditorHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(EditorHost).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    public static WebApplication Build(string[] args, EditorHostOptions? options = null)
    {
        options ??= new EditorHostOptions();

        // Error messages and number formatting must not depend on the machine's locale (e.g. "6553,5" on German Windows).
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = options.ContentRootPath,
        });
        if (options.Port is { } port)
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        else if (string.IsNullOrEmpty(builder.Configuration["urls"]))
            builder.WebHost.UseUrls($"http://localhost:{DefaultPort}"); // override with --urls or ASPNETCORE_URLS
        options.ConfigureLogging?.Invoke(builder.Logging);

        builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxUploadBytes);
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        builder.Services.AddSingleton<SessionStore>();
        builder.Services.AddSingleton<GameDataService>();
        builder.Services.AddSingleton<CharacterMapper>();

        var app = builder.Build();

        LoadGameData(app);
        ConfigureStaticFiles(app, options.EmbeddedAssetsOnly);
        MapApi(app);
        return app;
    }

    /// <summary>
    /// Game data (item/spell names): from config/env (Albion:GamePath or ALBION_PATH), otherwise auto-detect
    /// common GOG/Steam install folders. Failures only mean names stay empty.
    /// </summary>
    private static void LoadGameData(WebApplication app)
    {
        var gameDataService = app.Services.GetRequiredService<GameDataService>();
        var gamePath = app.Configuration["Albion:GamePath"] ?? Environment.GetEnvironmentVariable("ALBION_PATH");
        if (!string.IsNullOrWhiteSpace(gamePath))
        {
            try
            {
                var r = gameDataService.Configure(gamePath);
                app.Logger.LogInformation("Game data loaded from {Source} ({Items} items, {Spells} spell names)", r.Source, r.ItemCount, r.SpellCount);
            }
            catch (Exception e) { app.Logger.LogWarning("Game data not loaded from '{Path}': {Message}", gamePath, e.Message); }
        }
        else if (app.Configuration.GetValue("Albion:AutoDetect", true))
        {
            gameDataService.TryAutoDetect(app.Logger);
        }
    }

    /// <summary>Serves wwwroot from disk when present (development), otherwise from the embedded copy.</summary>
    private static void ConfigureStaticFiles(WebApplication app, bool embeddedOnly)
    {
        string diskRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
        if (embeddedOnly || !File.Exists(Path.Combine(diskRoot, "index.html")))
        {
            app.Environment.WebRootFileProvider = new ManifestEmbeddedFileProvider(typeof(EditorHost).Assembly, "wwwroot");
            app.Logger.LogInformation("Serving the editor UI from embedded resources");
        }
        app.UseDefaultFiles();
        app.UseStaticFiles();
    }

    private static void MapApi(WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/saves", async (HttpRequest request, SessionStore store, CharacterMapper mapper) =>
        {
            if (!request.HasFormContentType)
                return Error("Upload the save as multipart/form-data with a 'file' field.");
            IFormCollection form;
            try { form = await request.ReadFormAsync(); }
            catch (Exception e) when (e is InvalidDataException or BadHttpRequestException)
            {
                return Error("The upload could not be read: " + e.Message);
            }
            var file = form.Files["file"] ?? form.Files.FirstOrDefault();
            if (file == null || file.Length == 0) return Error("No file was uploaded (field 'file').");
            if (file.Length > MaxUploadBytes) return Error("That file is too large to be an Albion save.");

            byte[] data;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                data = ms.ToArray();
            }
            string fileName = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "SAVE.001";

            SaveFile save;
            try { save = SaveFile.Parse(data, fileName); }
            catch (SaveFormatException e) { return Error(e.Message); }

            string id = store.Add(save);
            lock (save.SyncRoot) return Results.Ok(ToSaveDto(id, save, mapper));
        }).DisableAntiforgery();

        api.MapGet("/saves/{sessionId}", (string sessionId, SessionStore store, CharacterMapper mapper) =>
        {
            var save = store.Get(sessionId);
            if (save == null) return SessionNotFound();
            lock (save.SyncRoot) return Results.Ok(ToSaveDto(sessionId, save, mapper));
        });

        api.MapPut("/saves/{sessionId}/header", async (string sessionId, HttpRequest request, SessionStore store) =>
        {
            var save = store.Get(sessionId);
            if (save == null) return SessionNotFound();
            var (dto, error) = await ReadBody<HeaderDto>(request);
            if (error != null) return error;
            lock (save.SyncRoot)
            {
                try
                {
                    var header = HeaderMapper.Apply(dto!, save.Header);
                    save.SetHeader(header);
                    return Results.Ok(HeaderMapper.ToDto(save.Header));
                }
                catch (ApiValidationException e) { return Error(e.Message, e.Field); }
            }
        });

        api.MapGet("/saves/{sessionId}/characters/{index:int}", (string sessionId, int index, SessionStore store, CharacterMapper mapper) =>
        {
            var save = store.Get(sessionId);
            if (save == null) return SessionNotFound();
            lock (save.SyncRoot)
            {
                if (!save.Characters.TryGetValue(index, out var ch)) return CharacterNotFound(index);
                return Results.Ok(mapper.ToDto(index, ch));
            }
        });

        api.MapPut("/saves/{sessionId}/characters/{index:int}", async (string sessionId, int index, HttpRequest request, SessionStore store, CharacterMapper mapper) =>
        {
            var save = store.Get(sessionId);
            if (save == null) return SessionNotFound();
            var (dto, error) = await ReadBody<CharacterDto>(request);
            if (error != null) return error;
            lock (save.SyncRoot)
            {
                if (!save.Characters.TryGetValue(index, out var ch)) return CharacterNotFound(index);
                // Apply to a copy so a validation failure leaves the session unchanged.
                var copy = new Character(ch.ToRawData());
                try { mapper.Apply(dto!, copy); }
                catch (ApiValidationException e) { return Error(e.Message, e.Field); }
                save.Characters[index] = copy;
                return Results.Ok(mapper.ToDto(index, copy));
            }
        });

        api.MapGet("/saves/{sessionId}/download", (string sessionId, SessionStore store) =>
        {
            var save = store.Get(sessionId);
            if (save == null) return SessionNotFound();
            byte[] bytes;
            lock (save.SyncRoot) bytes = save.Build();
            return Results.File(bytes, "application/octet-stream", save.FileName);
        });

        api.MapGet("/meta", (GameDataService gameData) => Results.Ok(new
        {
            enums = new Dictionary<string, object>
            {
                ["gender"] = EnumMapper.Describe(typeof(Gender)),
                ["race"] = EnumMapper.Describe(typeof(Race)),
                ["class"] = EnumMapper.Describe(typeof(CharacterClass)),
                ["type"] = EnumMapper.Describe(typeof(NPCType)),
                ["magic"] = EnumMapper.Describe(typeof(MagicFlags)),
                ["language"] = EnumMapper.Describe(typeof(LanguageFlags)),
                ["conditions"] = EnumMapper.Describe(typeof(ConditionFlags)),
                ["itemFlags"] = EnumMapper.Describe(typeof(ItemFlags)),
            },
            limits = Limits.ForMeta(),
            readOnly = Limits.ReadOnly,
            rules = new[] { new { type = "currentLeMax" } },
            items = gameData.Items.Select(i => new
            {
                id = i.Id,
                name = i.Name,
                stackable = i.Stackable,
                // without ITEMLIST.DAT no per-item limit is known: fall back to the byte range
                maxCount = i.Def != null ? i.MaxCount : byte.MaxValue,
                maxCharges = i.MaxCharges,
                maxEnchantments = i.MaxEnchantments,
                plotItem = i.PlotItem,
                cursed = i.Cursed,
                slot = i.Def != null ? EnumMapper.ToName(i.Def.Slot) : null,
                @class = i.Def != null ? EnumMapper.ToName(i.Def.Class) : null,
            }),
            gameDataConfigured = gameData.IsConfigured,
            gameDataSource = gameData.Source,
        }));

        api.MapPost("/gamedata", async (HttpRequest request, GameDataService gameData) =>
        {
            var (dto, error) = await ReadBody<GameDataRequest>(request);
            if (error != null) return error;
            try
            {
                var r = gameData.Configure(dto!.Path ?? "");
                return Results.Ok(new { ok = true, itemCount = r.ItemCount, spellCount = r.SpellCount, source = r.Source });
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return Results.BadRequest(new { ok = false, error = e.Message, field = "path" });
            }
        });

        api.MapGet("/about", () => Results.Ok(About.Get()));
    }

    private static IResult Error(string message, string? field = null)
        => Results.BadRequest(field == null ? new { error = message } : (object)new { error = message, field });

    private static IResult SessionNotFound()
        => Results.NotFound(new { error = "The save is no longer loaded on the server (maybe it was restarted). Please open the file again." });

    private static IResult CharacterNotFound(int index)
        => Results.NotFound(new { error = $"Character {index} does not exist in this save." });

    private static SaveDto ToSaveDto(string id, SaveFile save, CharacterMapper mapper) => new()
    {
        SessionId = id,
        FileName = save.FileName,
        Header = HeaderMapper.ToDto(save.Header),
        Characters = save.Characters.Select(p => mapper.ToSummary(p.Key, p.Value)).ToList(),
    };

    private static async Task<(T? Value, IResult? Error)> ReadBody<T>(HttpRequest request) where T : class
    {
        try
        {
            var value = await JsonSerializer.DeserializeAsync<T>(request.Body, JsonIn);
            return value == null ? (null, Error("Request body is required.")) : (value, null);
        }
        catch (JsonException e)
        {
            string? field = string.IsNullOrEmpty(e.Path) ? null : e.Path.TrimStart('$', '.');
            return (null, Error("Invalid JSON or wrong value type" + (field != null ? $" at '{field}'" : "") + ".", field));
        }
    }
}

/// <summary>Data for GET /api/about.</summary>
public static class About
{
    public static object Get() => new
    {
        name = "Albion Save Editor",
        version = EditorHost.Version,
        credits = new object[]
        {
            new
            {
                name = "AlbLib",
                author = "IS4Code (IllidanS4)",
                url = "https://github.com/IS4Code/AlbLib",
                license = "MIT",
                usage = "Save game, XLD and text parsing (ported to .NET 10, with fixes and additions).",
                licenseText = AlbLibLicense,
            },
            new
            {
                name = "UAlbion",
                author = "csinkers and contributors",
                url = "https://github.com/csinkers/ualbion",
                license = "MIT",
                usage = "File format research (character sheet layout, item flags, stack limits). No code is included.",
                licenseText = (string?)null,
            },
        },
        notice = "Albion is (c) Blue Byte. No game files are included; item and spell names are read from your own Albion installation.",
    };

    private static string? albLibLicense;

    /// <summary>AlbLib's MIT license text (embedded resource).</summary>
    public static string AlbLibLicense
    {
        get
        {
            if (albLibLicense != null) return albLibLicense;
            using var s = typeof(About).Assembly.GetManifestResourceStream("LICENSE-AlbLib.txt");
            albLibLicense = s == null ? "" : new StreamReader(s).ReadToEnd();
            return albLibLicense;
        }
    }
}
