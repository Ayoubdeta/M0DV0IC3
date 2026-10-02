namespace M0DV0IC3.Cli;

/// <summary>Tabla de texto con columnas alineadas.</summary>
internal sealed class TextTable(params string[] headers)
{
    private readonly List<string[]> _rows = [];
    private readonly HashSet<int> _rightAligned = [];

    /// <summary>Alinea a la derecha las columnas indicadas (las numéricas).</summary>
    public TextTable AlignRight(params int[] columns)
    {
        _rightAligned.UnionWith(columns);
        return this;
    }

    public void Add(params string[] cells) => _rows.Add(cells);

    public void Print(string indent = "")
    {
        var widths = new int[headers.Length];
        foreach (var row in _rows.Prepend(headers))
        {
            for (int c = 0; c < widths.Length && c < row.Length; c++) widths[c] = Math.Max(widths[c], row[c].Length);
        }

        WriteRow(headers, widths, indent);
        Console.WriteLine((indent + string.Join("  ", widths.Select(w => new string('-', w)))).TrimEnd());
        foreach (var row in _rows) WriteRow(row, widths, indent);
    }

    private void WriteRow(string[] row, int[] widths, string indent)
    {
        var cells = new string[widths.Length];
        for (int c = 0; c < widths.Length; c++)
        {
            string text = c < row.Length ? row[c] : "";
            cells[c] = _rightAligned.Contains(c) ? text.PadLeft(widths[c]) : text.PadRight(widths[c]);
        }
        Console.WriteLine((indent + string.Join("  ", cells)).TrimEnd());
    }
}
