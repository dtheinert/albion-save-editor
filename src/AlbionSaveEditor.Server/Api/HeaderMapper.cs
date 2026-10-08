using AlbLib.SaveGame;
using AlbLib.Texts;

namespace AlbionSaveEditor.Api;

public static class HeaderMapper
{
    public static HeaderDto ToDto(SaveGameInfo h) => new()
    {
        Name = h.Name,
        Version = $"{h.Version.Major}.{h.Version.Minor:00}",
        Days = h.Days,
        Hours = h.Hours,
        Minutes = h.Minutes,
        MapId = h.MapID,
        PartyX = h.PartyX,
        PartyY = h.PartyY,
    };

    /// <summary>
    /// Returns a new header with <paramref name="dto"/> applied (the current header is not modified).
    /// Unknown header fields are carried over because the copy is re-parsed from the current header bytes.
    /// <c>version</c> is read-only and ignored.
    /// </summary>
    public static SaveGameInfo Apply(HeaderDto dto, SaveGameInfo current)
    {
        var h = new SaveGameInfo(new MemoryStream(current.ToRawData(), false));

        if (dto.Name != null && dto.Name != h.Name)
        {
            if (dto.Name.Length > Limits.SaveNameMaxLength)
                throw new ApiValidationException("name", $"Save name can be at most {Limits.SaveNameMaxLength} characters.");
            var enc = TextCore.DefaultEncoding;
            if (enc.GetString(enc.GetBytes(dto.Name)) != dto.Name)
                throw new ApiValidationException("name", "Save name contains characters the game cannot display.");
            h.Name = dto.Name;
        }
        // changed values only; untouched out-of-range values from the save are accepted
        h.Days = Limits.ToInt16(dto.Days, h.Days, "days", Limits.NonNegative16);
        h.Hours = Limits.ToInt16(dto.Hours, h.Hours, "hours", Limits.Hours);
        h.Minutes = Limits.ToInt16(dto.Minutes, h.Minutes, "minutes", Limits.Minutes);
        h.MapID = Limits.ToInt16(dto.MapId, h.MapID, "mapId", Limits.NonNegative16);
        h.PartyX = Limits.ToInt16(dto.PartyX, h.PartyX, "partyX", Limits.NonNegative16);
        h.PartyY = Limits.ToInt16(dto.PartyY, h.PartyY, "partyY", Limits.NonNegative16);
        return h;
    }
}
