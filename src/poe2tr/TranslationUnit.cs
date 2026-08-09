using System.Text.Json.Serialization;

namespace Poe2Tr;

/// <summary>
/// Tek bir cevrilecek metin. JSONL dosyasinda her satir bir birim.
/// Ceviri motoru "tr" alanini doldurup geri verir; "id" ile ayni yere yazilir.
/// </summary>
public sealed class TranslationUnit {
	/// <summary>tablo|kolonOffseti|satir — geri yazarken tekil anahtar</summary>
	[JsonPropertyName("id")] public string? Id { get; set; }
	[JsonPropertyName("table")] public string? Table { get; set; }
	/// <summary>Kolonun satir icindeki bayt konumu</summary>
	[JsonPropertyName("off")] public int Off { get; set; }
	[JsonPropertyName("row")] public int Row { get; set; }
	/// <summary>Kolon adi (sema varsa); sema-bagimsiz tespitte null</summary>
	[JsonPropertyName("col")] public string? Col { get; set; }
	/// <summary>Ingilizce kaynak metin</summary>
	[JsonPropertyName("en")] public string? En { get; set; }
	/// <summary>Turkce ceviri — dat-export bos birakir, dat-import bunu bekler</summary>
	[JsonPropertyName("tr")] public string? Tr { get; set; }
}
