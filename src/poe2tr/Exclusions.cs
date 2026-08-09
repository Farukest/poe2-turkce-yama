namespace Poe2Tr;

/// <summary>
/// Ceviri disi birakilan kolonlar. Esya / yetenek / para birimi ADLARI
/// Ingilizce kalir; aciklamalari cevrilir.
/// </summary>
public sealed class Exclusions {
	private readonly HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

	public int Count => keys.Count;

	public bool IsExcluded(string table, string? columnName, int offset)
		=> (columnName is not null && keys.Contains($"{table}|{columnName}"))
			|| keys.Contains($"{table}|off:{offset}");

	public static Exclusions Load(string? path) {
		var e = new Exclusions();
		if (path is null || !File.Exists(path))
			return e;
		foreach (var raw in File.ReadLines(path)) {
			var line = raw.Trim();
			if (line.Length == 0 || line.StartsWith('#'))
				continue;
			e.keys.Add(line);
		}
		return e;
	}

	/// <summary>
	/// Calisma dizininden yukari dogru yurruyerek ceviri-disi.txt'yi bulur.
	/// Her seviyede "veri" alt klasorune de bakiyor: dagitim paketinde veri
	/// dosyalari exe'nin yanindaki veri/ altinda duruyor.
	/// </summary>
	public static string? Locate() {
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null) {
			foreach (var aday in new[] {
				Path.Combine(dir.FullName, "ceviri-disi.txt"),
				Path.Combine(dir.FullName, "veri", "ceviri-disi.txt"),
			}) {
				if (File.Exists(aday))
					return aday;
			}
			dir = dir.Parent;
		}
		return null;
	}
}
