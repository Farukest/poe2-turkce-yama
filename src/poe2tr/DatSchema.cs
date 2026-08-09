using System.Text.Json;
using System.Text.Json.Serialization;

namespace Poe2Tr;

/// <summary>
/// poe-tool-dev/dat-schema tarafindan uretilen schema.min.json'un okunmasi.
/// </summary>
public sealed class SchemaRoot {
	[JsonPropertyName("tables")] public List<SchemaTable> Tables { get; set; } = [];

	/// <summary>PoE2 (validFor=2) tablolarini isimden bulmak icin sozluk.</summary>
	public Dictionary<string, SchemaTable> Poe2ByName(StringComparer? cmp = null) {
		var d = new Dictionary<string, SchemaTable>(cmp ?? StringComparer.OrdinalIgnoreCase);
		foreach (var t in Tables)
			if (t.ValidFor is 2 or 3) // 3 = her iki oyun icin gecerli
				d[t.Name] = t;
		return d;
	}

	public static SchemaRoot Load(string path) {
		using var fs = File.OpenRead(path);
		return JsonSerializer.Deserialize<SchemaRoot>(fs)
			?? throw new InvalidDataException("schema.min.json okunamadi: " + path);
	}

	/// <summary>
	/// Calisma dizininden yukari dogru yurruyerek schema.min.json'u bulur.
	/// Dagitim paketinde exe'nin yanindaki veri/ altinda duruyor.
	/// </summary>
	public static string Locate() {
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null) {
			foreach (var aday in new[] {
				Path.Combine(dir.FullName, "veri", "schema.min.json"),
				Path.Combine(dir.FullName, "tools", "dat-schema", "schema.min.json"),
			}) {
				if (File.Exists(aday))
					return aday;
			}
			dir = dir.Parent;
		}
		throw new FileNotFoundException("schema.min.json bulunamadi (veri/ ya da tools/dat-schema/ altinda olmali)");
	}
}

public sealed class SchemaTable {
	[JsonPropertyName("validFor")] public int ValidFor { get; set; }
	[JsonPropertyName("name")] public string Name { get; set; } = "";
	[JsonPropertyName("columns")] public List<SchemaColumn> Columns { get; set; } = [];

	/// <summary>Bir satirin toplam bayt uzunlugu.</summary>
	public int RowLength => Columns.Sum(c => c.Size);

	/// <summary>Kolonun satir icindeki bayt konumu.</summary>
	public int OffsetOf(int columnIndex) {
		var o = 0;
		for (var i = 0; i < columnIndex; ++i)
			o += Columns[i].Size;
		return o;
	}

	public bool HasLocalized => Columns.Any(c => c.Localized);
}

public sealed class SchemaColumn {
	[JsonPropertyName("name")] public string? Name { get; set; }
	[JsonPropertyName("array")] public bool Array { get; set; }
	[JsonPropertyName("type")] public string Type { get; set; } = "";
	[JsonPropertyName("localized")] public bool Localized { get; set; }

	/// <summary>
	/// 64-bit dat (.datc64) icin alan boyutlari.
	/// Dizi alanlari her zaman (uzunluk:u64 + offset:u64) = 16 bayt.
	/// </summary>
	public int Size => Array ? 16 : Type switch {
		"bool" => 1,
		"i16" or "u16" => 2,
		"i32" or "u32" or "f32" or "enumrow" => 4,
		"string" or "row" => 8,
		"foreignrow" => 16,
		"array" => 16,
		_ => throw new NotSupportedException($"Bilinmeyen kolon tipi: {Type}")
	};

	/// <summary>Ceviri hedefi olan kolon: yerellestirilmis metin.</summary>
	public bool IsTranslatable => Localized && !Array && Type == "string";
}
