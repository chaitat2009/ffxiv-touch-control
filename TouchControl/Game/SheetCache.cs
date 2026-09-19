using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace TouchControl.Game;

/// <summary>Names and icons for the rows a button can point at, read once from the game data.</summary>
public sealed class SheetCache
{
    public readonly record struct Entry(uint RowId, string Name, uint IconId);

    public IReadOnlyList<Entry> GeneralActions { get; }
    public IReadOnlyList<Entry> MainCommands { get; }

    private readonly Dictionary<uint, Entry> generalById;
    private readonly Dictionary<uint, Entry> mainById;

    public SheetCache()
    {
        GeneralActions = Plugin.DataManager.GetExcelSheet<GeneralAction>()
            .Where(r => !r.Name.IsEmpty)
            .Select(r => new Entry(r.RowId, r.Name.ExtractText(), (uint)r.Icon))
            .ToList();

        MainCommands = Plugin.DataManager.GetExcelSheet<MainCommand>()
            .Where(r => !r.Name.IsEmpty)
            .Select(r => new Entry(r.RowId, r.Name.ExtractText(), (uint)r.Icon))
            .ToList();

        generalById = GeneralActions.ToDictionary(e => e.RowId);
        mainById = MainCommands.ToDictionary(e => e.RowId);
    }

    public Entry? GeneralAction(uint id) => generalById.TryGetValue(id, out var e) ? e : null;

    public Entry? MainCommand(uint id) => mainById.TryGetValue(id, out var e) ? e : null;
}
