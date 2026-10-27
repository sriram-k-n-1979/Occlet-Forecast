using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Occlet.Core.Io;

/// <summary>
/// Minimal .xlsx reader. An .xlsx file is just a zip of XML files, so the .NET base library
/// (System.IO.Compression + System.Xml.Linq) is enough — no NuGet package needed.
/// Returns each row as an array of cell strings (numbers and dates come back as their raw text).
/// </summary>
public static class XlsxReader
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static List<string?[]> ReadSheet(string path, string sheetName)
    {
        using var zip = ZipFile.OpenRead(path);
        var workbook = Load(zip, "xl/workbook.xml");
        var sheet = workbook.Descendants(S + "sheet")
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("name"), sheetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Sheet '{sheetName}' was not found in {path}.");
        string relId = (string)sheet.Attribute(R + "id")!;

        // workbook.xml.rels maps the sheet's relationship id to its XML file inside the zip
        var rels = Load(zip, "xl/_rels/workbook.xml.rels");
        string target = (string)rels.Descendants(PR + "Relationship")
            .First(e => (string?)e.Attribute("Id") == relId).Attribute("Target")!;
        string entry = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

        // Excel stores repeated text once, in sharedStrings.xml, and cells point to it by index
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") != null)
            foreach (var si in Load(zip, "xl/sharedStrings.xml").Root!.Elements(S + "si"))
                shared.Add(string.Concat(si.Descendants(S + "t").Select(t => t.Value)));

        var rows = new List<string?[]>();
        foreach (var row in Load(zip, entry).Descendants(S + "row"))
        {
            var cells = new SortedDictionary<int, string?>();
            int next = 0;
            foreach (var c in row.Elements(S + "c"))
            {
                string? cellRef = (string?)c.Attribute("r");
                int col = cellRef != null ? ColumnIndex(cellRef) : next;
                next = col + 1;
                string? type = (string?)c.Attribute("t");
                string? v = c.Element(S + "v")?.Value;
                cells[col] = type switch
                {
                    "s" => v == null ? null : shared[int.Parse(v, CultureInfo.InvariantCulture)],
                    "inlineStr" => string.Concat(c.Descendants(S + "t").Select(x => x.Value)),
                    "b" => v == "1" ? "TRUE" : "FALSE",
                    _ => v,
                };
            }
            var arr = new string?[cells.Count == 0 ? 0 : cells.Keys.Max() + 1];
            foreach (var kv in cells) arr[kv.Key] = kv.Value;
            rows.Add(arr);
        }
        return rows;
    }

    /// <summary>"C12" → 2 (zero-based column index).</summary>
    public static int ColumnIndex(string cellRef)
    {
        int n = 0;
        foreach (char ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return n - 1;
    }

    private static XDocument Load(ZipArchive zip, string name)
    {
        using var s = (zip.GetEntry(name) ?? throw new InvalidDataException($"{name} missing from workbook")).Open();
        return XDocument.Load(s);
    }
}

/// <summary>
/// Minimal .xlsx writer: header row (bold, white on navy, frozen), strings, numbers, dates, booleans.
/// Enough to hand results to people who live in Excel, with no third-party library.
/// </summary>
public sealed class XlsxWriter
{
    private sealed record Sheet(string Name, string[] Headers, List<object?[]> Rows);
    private readonly List<Sheet> _sheets = new();

    public XlsxWriter AddSheet(string name, IEnumerable<string> headers, IEnumerable<object?[]> rows)
    {
        string safe = new string(name.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
        _sheets.Add(new Sheet(safe.Length > 31 ? safe[..31] : safe, headers.ToArray(), rows.ToList()));
        return this;
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path)) File.Delete(path);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Put(zip, "[Content_Types].xml", ContentTypes());
        Put(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");
        Put(zip, "xl/workbook.xml", WorkbookXml());
        Put(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
        Put(zip, "xl/styles.xml", StylesXml);
        for (int i = 0; i < _sheets.Count; i++) Put(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(_sheets[i]));
    }

    private static void Put(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        e.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero); // stable bytes run to run
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private string ContentTypes()
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int i = 0; i < _sheets.Count; i++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        return sb.Append("</Types>").ToString();
    }

    private string WorkbookXml()
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        for (int i = 0; i < _sheets.Count; i++)
            sb.Append($"<sheet name=\"{Esc(_sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        return sb.Append("</sheets></workbook>").ToString();
    }

    private string WorkbookRels()
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 0; i < _sheets.Count; i++)
            sb.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
        sb.Append($"<Relationship Id=\"rId{_sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    // Style index: 0 normal, 1 header, 2 date (yyyy-mm-dd), 3 number with 2 decimals
    private const string StylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd\"/><numFmt numFmtId=\"165\" formatCode=\"#,##0.00\"/></numFmts>" +
        "<fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Arial\"/></font><font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Arial\"/></font></fonts>" +
        "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F3A5F\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"4\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";

    private static string SheetXml(Sheet s)
    {
        int ncol = Math.Max(s.Headers.Length, s.Rows.Count == 0 ? 0 : s.Rows.Max(r => r.Length));
        var widths = new double[ncol];
        for (int c = 0; c < ncol; c++)
        {
            int w = c < s.Headers.Length ? s.Headers[c].Length : 8;
            foreach (var r in s.Rows.Take(400))
                if (c < r.Length && r[c] != null) w = Math.Max(w, Display(r[c]!).Length);
            widths[c] = Math.Clamp(w + 2, 9, 80);
        }
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        if (ncol > 0)
        {
            sb.Append("<cols>");
            for (int c = 0; c < ncol; c++)
                sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{widths[c].ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        WriteRow(sb, 1, s.Headers.Cast<object?>().ToArray(), header: true);
        for (int i = 0; i < s.Rows.Count; i++) WriteRow(sb, i + 2, s.Rows[i], header: false);
        return sb.Append("</sheetData></worksheet>").ToString();
    }

    private static void WriteRow(StringBuilder sb, int rowNum, object?[] values, bool header)
    {
        sb.Append($"<row r=\"{rowNum}\">");
        for (int c = 0; c < values.Length; c++)
        {
            object? v = values[c];
            if (v is null) continue;
            string r = ColumnName(c) + rowNum;
            string style = header ? " s=\"1\"" : "";
            switch (v)
            {
                case string str:
                    sb.Append($"<c r=\"{r}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{Esc(str)}</t></is></c>");
                    break;
                case bool b:
                    sb.Append($"<c r=\"{r}\" t=\"b\"{style}><v>{(b ? 1 : 0)}</v></c>");
                    break;
                case DateOnly d:
                    sb.Append($"<c r=\"{r}\" s=\"2\"><v>{d.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>");
                    break;
                case DateTime dt:
                    sb.Append($"<c r=\"{r}\" s=\"2\"><v>{dt.ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>");
                    break;
                case int or long or short:
                    sb.Append($"<c r=\"{r}\"{style}><v>{Convert.ToString(v, CultureInfo.InvariantCulture)}</v></c>");
                    break;
                case double or float or decimal:
                    double x = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                    if (double.IsNaN(x) || double.IsInfinity(x)) break;
                    sb.Append($"<c r=\"{r}\" s=\"{(header ? 1 : 3)}\"><v>{x.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                    break;
                default:
                    sb.Append($"<c r=\"{r}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{Esc(v.ToString() ?? "")}</t></is></c>");
                    break;
            }
        }
        sb.Append("</row>");
    }

    private static string Display(object v) => v switch
    {
        double d => d.ToString("#,##0.00", CultureInfo.InvariantCulture),
        DateOnly => "2026-01-01",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
    };

    private static string ColumnName(int index)
    {
        string s = "";
        for (int n = index + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue; // not allowed in XML
            sb.Append(ch switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => ch.ToString() });
        }
        return sb.ToString();
    }
}
