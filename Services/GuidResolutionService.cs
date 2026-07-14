using Microsoft.Extensions.Logging;

namespace TallySyncService.Services;

/// <summary>
/// Resolves stub _xxx GUID columns in exported CSVs by performing a post-sync
/// name→GUID lookup against the already-exported master CSVs.
///
/// Strategy A columns (TDL-populated) are checked for universal emptiness; if
/// a Strategy-A field returns all empty rows, it falls back to a name lookup
/// here (guards against unresolvable TDL attribute names in a given Tally version).
/// </summary>
public class GuidResolutionService
{
    private readonly ILogger _logger;

    // masterTableName → (lookupKey → guid)
    // lookupKey = "name" or "name\0parent" (NUL-separated to avoid collisions)
    private readonly Dictionary<string, Dictionary<string, string>> _lookups
        = new(StringComparer.OrdinalIgnoreCase);

    // ── Strategy B column map ─────────────────────────────────────────────────
    // (tableName, guidColumnName) → (masterCsv, nameColumnInMaster, parentColumnInMaster?)
    // parentColumnInMaster non-null → lookup key includes parent value for uniqueness
    private static readonly Dictionary<(string table, string guidCol), (string master, string nameCol, string? parentCol)> StrategyBMap
        = new()
    {
        { ("mst_stock_item",       "_parent"),            ("mst_stock_group",     "name", null) },
        { ("mst_stock_item",       "_uom"),               ("mst_uom",             "name", null) },
        { ("mst_stock_item",       "_alternate_uom"),     ("mst_uom",             "name", null) },
        { ("mst_attendance_type",  "_uom"),               ("mst_uom",             "name", null) },
        { ("mst_employee",         "_category"),          ("mst_cost_category",   "name", null) },
        { ("mst_opening_batch_allocation", "_godown"),    ("mst_godown",          "name", null) },
        { ("trn_voucher",          "_voucher_type"),      ("mst_vouchertype",     "name", null) },
        { ("trn_voucher",          "_party_name"),        ("mst_ledger",          "name", null) },
        { ("trn_inventory",        "_godown"),            ("mst_godown",          "name", null) },
        { ("trn_cost_centre",      "_costcentre"),        ("mst_cost_centre",     "name", "parent") },
        { ("trn_cost_category_centre", "_costcategory"),  ("mst_cost_category",   "name", null) },
        { ("trn_cost_category_centre", "_costcentre"),    ("mst_cost_centre",     "name", "parent") },
        { ("trn_cost_inventory_category_centre", "_item"),        ("mst_stock_item",  "name", null) },
        { ("trn_cost_inventory_category_centre", "_costcategory"),("mst_cost_category","name", null) },
        { ("trn_cost_inventory_category_centre", "_costcentre"),  ("mst_cost_centre", "name", "parent") },
        { ("trn_batch",            "_item"),              ("mst_stock_item",      "name", null) },
        { ("trn_batch",            "_godown"),            ("mst_godown",          "name", null) },
        { ("trn_batch",            "_destination_godown"),("mst_godown",          "name", null) },
        { ("trn_employee",         "_category"),          ("mst_cost_category",   "name", null) },
        { ("trn_employee",         "_employee_name"),     ("mst_employee",        "name", null) },
        { ("trn_payhead",          "_category"),          ("mst_cost_category",   "name", null) },
        { ("trn_payhead",          "_employee_name"),     ("mst_employee",        "name", null) },
        { ("trn_payhead",          "_payhead_name"),      ("mst_payhead",         "name", null) },
        { ("trn_attendance",       "_employee_name"),     ("mst_employee",        "name", null) },
        { ("trn_attendance",       "_attendancetype_name"),("mst_attendance_type","name", null) },
    };

    // ── Strategy A fall-through map ───────────────────────────────────────────
    // If a Strategy-A _xxx column is universally empty after TDL export,
    // fall back to a name-based lookup using the sibling name column in the same CSV.
    // (tableName, guidCol) → (masterCsv, nameColInMaster, siblingNameColInSameTable)
    private static readonly Dictionary<(string table, string guidCol), (string master, string nameCol, string siblingCol)> StrategyAFallback
        = new()
    {
        { ("trn_accounting",       "_ledger"),   ("mst_ledger",     "name", "ledger") },
        { ("trn_inventory",        "_item"),     ("mst_stock_item", "name", "item") },
        { ("trn_cost_centre",      "_ledger"),   ("mst_ledger",     "name", "ledger") },
        { ("trn_cost_category_centre", "_ledger"),   ("mst_ledger", "name", "ledger") },
        { ("trn_cost_inventory_category_centre", "_ledger"), ("mst_ledger", "name", "ledger") },
        { ("trn_bill",             "_ledger"),   ("mst_ledger",     "name", "ledger") },
        { ("trn_bank",             "_ledger"),   ("mst_ledger",     "name", "ledger") },
        { ("trn_inventory_accounting", "_ledger"),("mst_ledger",    "name", "ledger") },
        { ("mst_gst_effective_rate",   "_item"), ("mst_stock_item", "name", "item") },
        { ("trn_closingstock_ledger",  "_ledger"),("mst_ledger",    "name", "ledger") },
        { ("mst_stockitem_standard_cost",  "_item"),("mst_stock_item","name","item") },
        { ("mst_stockitem_standard_price", "_item"),("mst_stock_item","name","item") },
    };

    public GuidResolutionService(ILogger logger)
    {
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Loads all master CSVs from <paramref name="csvDirectory"/> and builds lookup tables.</summary>
    public async Task BuildLookupTablesAsync(string csvDirectory)
    {
        var masterTables = new[]
        {
            "mst_group", "mst_ledger", "mst_vouchertype", "mst_uom",
            "mst_godown", "mst_stock_category", "mst_stock_group",
            "mst_stock_item", "mst_cost_category", "mst_cost_centre",
            "mst_attendance_type", "mst_employee", "mst_payhead"
        };

        foreach (var table in masterTables)
        {
            var path = Path.Combine(csvDirectory, $"{table}.csv");
            if (!File.Exists(path))
            {
                _logger.LogDebug("Master CSV not found, skipping lookup build: {Table}", table);
                continue;
            }

            try
            {
                await BuildLookupFromCsvAsync(table, path);
                _logger.LogDebug("Lookup table built: {Table} ({Count} entries)", table, _lookups.GetValueOrDefault(table)?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build lookup table for {Table}", table);
            }
        }

        _logger.LogInformation("GUID lookup tables built for {Count} master table(s)", _lookups.Count);
    }

    /// <summary>Resolves all stub _xxx GUID columns in every CSV under <paramref name="csvDirectory"/>.</summary>
    public async Task ResolveGuidsAsync(string csvDirectory)
    {
        var csvFiles = Directory.GetFiles(csvDirectory, "*.csv");
        int totalResolved = 0;
        int totalUnresolved = 0;

        foreach (var csvPath in csvFiles)
        {
            var tableName = Path.GetFileNameWithoutExtension(csvPath);
            try
            {
                var (resolved, unresolved) = await ResolveTableAsync(csvPath, tableName);
                totalResolved += resolved;
                totalUnresolved += unresolved;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GUID resolution failed for table {Table}", tableName);
            }
        }

        _logger.LogInformation(
            "GUID resolution complete — resolved: {Resolved}, unresolved (logged above): {Unresolved}",
            totalResolved, totalUnresolved);
    }

    /// <summary>Resolves a single GUID lookup (for testing).</summary>
    public string? ResolveGuid(string masterTable, string name, string? parent = null)
    {
        if (!_lookups.TryGetValue(masterTable, out var lookup)) return null;
        var key = MakeKey(name, parent);
        lookup.TryGetValue(key, out var guid);
        return guid;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task BuildLookupFromCsvAsync(string tableName, string path)
    {
        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length < 2) return;

        var headers = ParseCsvRow(lines[0]);
        int guidIdx  = Array.FindIndex(headers, h => h.Equals("guid",   StringComparison.OrdinalIgnoreCase));
        int nameIdx  = Array.FindIndex(headers, h => h.Equals("name",   StringComparison.OrdinalIgnoreCase));
        int parentIdx= Array.FindIndex(headers, h => h.Equals("parent", StringComparison.OrdinalIgnoreCase));

        if (guidIdx < 0 || nameIdx < 0)
        {
            _logger.LogWarning("Master CSV {Table} missing required 'guid' or 'name' columns — skipping", tableName);
            return;
        }

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cols = ParseCsvRow(lines[i]);
            if (cols.Length <= Math.Max(guidIdx, nameIdx)) continue;

            var guid = cols[guidIdx].Trim();
            var name = cols[nameIdx].Trim();
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(name)) continue;

            // Simple key (just name)
            var simpleKey = MakeKey(name, null);
            lookup.TryAdd(simpleKey, guid);

            // Compound key with parent if column exists
            if (parentIdx >= 0 && cols.Length > parentIdx)
            {
                var parent = cols[parentIdx].Trim();
                var compoundKey = MakeKey(name, parent);
                lookup.TryAdd(compoundKey, guid);
            }
        }

        _lookups[tableName] = lookup;
    }

    private async Task<(int resolved, int unresolved)> ResolveTableAsync(string csvPath, string tableName)
    {
        var lines = (await File.ReadAllLinesAsync(csvPath)).ToList();
        if (lines.Count < 2) return (0, 0);

        var headers = ParseCsvRow(lines[0]);
        bool changed = false;
        int resolved = 0, unresolved = 0;

        // ── Strategy B: direct stub lookups ───────────────────────────────────
        foreach (var ((tbl, guidCol), (masterCsv, _, parentCol)) in StrategyBMap)
        {
            if (!tbl.Equals(tableName, StringComparison.OrdinalIgnoreCase)) continue;
            int guidIdx = Array.FindIndex(headers, h => h.Equals(guidCol, StringComparison.OrdinalIgnoreCase));
            if (guidIdx < 0) continue;

            // Infer the sibling name column (strip leading '_')
            var siblingCol = guidCol.TrimStart('_');
            // Handle special aliases
            if (guidCol == "_party_name")    siblingCol = "party_name";
            if (guidCol == "_voucher_type")  siblingCol = "voucher_type";
            if (guidCol == "_employee_name") siblingCol = "employee_name";
            if (guidCol == "_payhead_name")  siblingCol = "payhead_name";
            if (guidCol == "_attendancetype_name") siblingCol = "attendancetype_name";
            if (guidCol == "_destination_godown") siblingCol = "destination_godown";
            if (guidCol == "_alternate_uom") siblingCol = "alternate_uom";

            int nameIdx = Array.FindIndex(headers, h => h.Equals(siblingCol, StringComparison.OrdinalIgnoreCase));
            int parentIdx = parentCol != null
                ? Array.FindIndex(headers, h => h.Equals(parentCol, StringComparison.OrdinalIgnoreCase))
                : -1;

            if (nameIdx < 0) continue;

            for (int i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = ParseCsvRow(lines[i]);
                if (cols.Length <= Math.Max(guidIdx, nameIdx)) continue;

                // Only fill if currently empty
                if (!string.IsNullOrEmpty(cols[guidIdx])) continue;

                var nameVal  = cols[nameIdx].Trim();
                var parentVal = (parentIdx >= 0 && cols.Length > parentIdx) ? cols[parentIdx].Trim() : null;

                if (string.IsNullOrEmpty(nameVal)) continue; // blank ref → leave empty

                var guid = LookupGuid(masterCsv, nameVal, parentVal);
                if (guid != null)
                {
                    cols[guidIdx] = guid;
                    lines[i] = BuildCsvRow(cols);
                    changed = true;
                    resolved++;
                }
                else
                {
                    _logger.LogWarning(
                        "GUID not found — table: {Table}, row: {RowNum}, column: {GuidCol}, name: '{Name}'",
                        tableName, i, guidCol, nameVal);
                    unresolved++;
                }
            }
        }

        // ── Strategy A fall-through: check if TDL-populated columns are all empty ──
        foreach (var ((tbl, guidCol), (masterCsv, _, siblingCol)) in StrategyAFallback)
        {
            if (!tbl.Equals(tableName, StringComparison.OrdinalIgnoreCase)) continue;
            int guidIdx = Array.FindIndex(headers, h => h.Equals(guidCol, StringComparison.OrdinalIgnoreCase));
            if (guidIdx < 0) continue;

            // Check if universally empty (all data rows blank in this column)
            bool allEmpty = true;
            for (int i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = ParseCsvRow(lines[i]);
                if (cols.Length > guidIdx && !string.IsNullOrEmpty(cols[guidIdx].Trim()))
                {
                    allEmpty = false;
                    break;
                }
            }

            if (!allEmpty) continue; // Strategy A worked — nothing to do

            _logger.LogWarning(
                "Strategy-A column '{GuidCol}' on table '{Table}' is universally empty — " +
                "TDL attribute may not be supported by this Tally version. Falling back to name lookup.",
                guidCol, tableName);

            int nameIdx = Array.FindIndex(headers, h => h.Equals(siblingCol, StringComparison.OrdinalIgnoreCase));
            if (nameIdx < 0) continue;

            for (int i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = ParseCsvRow(lines[i]);
                if (cols.Length <= Math.Max(guidIdx, nameIdx)) continue;

                var nameVal = cols[nameIdx].Trim();
                if (string.IsNullOrEmpty(nameVal)) continue;

                var guid = LookupGuid(masterCsv, nameVal, null);
                if (guid != null)
                {
                    cols[guidIdx] = guid;
                    lines[i] = BuildCsvRow(cols);
                    changed = true;
                    resolved++;
                }
                else
                {
                    _logger.LogWarning(
                        "Fallback GUID not found — table: {Table}, row: {RowNum}, column: {GuidCol}, name: '{Name}'",
                        tableName, i, guidCol, nameVal);
                    unresolved++;
                }
            }
        }

        if (changed)
            await File.WriteAllLinesAsync(csvPath, lines);

        return (resolved, unresolved);
    }

    private string? LookupGuid(string masterTable, string name, string? parent)
    {
        if (!_lookups.TryGetValue(masterTable, out var lookup)) return null;

        // Try compound key first (more specific), then simple key
        if (parent != null)
        {
            var compoundKey = MakeKey(name, parent);
            if (lookup.TryGetValue(compoundKey, out var g1)) return g1;
        }

        var simpleKey = MakeKey(name, null);
        lookup.TryGetValue(simpleKey, out var g2);
        return g2;
    }

    private static string MakeKey(string name, string? parent)
        => parent != null ? $"{name}\0{parent}" : name;

    // ── Minimal RFC-4180 CSV parser ───────────────────────────────────────────

    private static string[] ParseCsvRow(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == '"') { inQuotes = true; }
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    private static string BuildCsvRow(string[] cols)
    {
        return string.Join(",", cols.Select(v =>
        {
            bool needsQuote = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
            if (needsQuote) return $"\"{v.Replace("\"", "\"\"")}\"";
            return v;
        }));
    }
}
