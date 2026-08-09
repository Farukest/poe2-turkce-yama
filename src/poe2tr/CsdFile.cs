using System.Text;
using System.Text.RegularExpressions;

namespace Poe2Tr;

/// <summary>
/// data/statdescriptions/*.csd — stat ve mod metinleri.
///
/// .datc64 tablolarindan tamamen farkli: duz metin (UTF-16LE) ve TUM DILLER
/// AYNI DOSYADA. Yapisi:
///
///     description
///         1 melee_physical_damage_taken_%_to_deal_to_attacker
///         1
///             # "{0}% of [Melee] Physical Damage taken reflected to Attacker"
///         lang "Portuguese"
///         1
///             # "{0}% do dano fisico [Melee|corpo a corpo] sofrido..."
///         lang "Traditional Chinese"
///         ...
///
/// Ilk (lang'siz) bolum Ingilizce varsayilandir. Her dil bolumu ayni sayida ve
/// ayni sirada varyant icerir, bu yuzden Ingilizce varyant i ile Portekizce
/// varyant i birebir eslesir.
/// </summary>
public static partial class CsdFile {
	[GeneratedRegex(@"^\s*description\s*$")] private static partial Regex DescriptionRx();
	[GeneratedRegex(@"^\s*lang\s+""([^""]+)""\s*$")] private static partial Regex LangRx();
	/// <summary>Varyant satiri: onek + "metin" + kalan (negate 1 gibi degistiriciler).</summary>
	[GeneratedRegex(@"^(?<pre>[^""]*"")(?<txt>(?:[^""\\]|\\.)*)(?<post>"".*)$", RegexOptions.Singleline)]
	private static partial Regex VariantRx();

	private const string Localized = "Portuguese";

	/// <summary>Bir cevrilebilir birim: Ingilizce kaynak ve karsiligi olan yerel satir.</summary>
	public sealed record Entry(int DescIndex, int VariantIndex, string English);

	/// <summary>UTF-16LE okur — bu dosyalarin kodlamasi.</summary>
	public static string[] ReadLines(byte[] raw)
		=> Encoding.Unicode.GetString(raw).Split(["\r\n", "\n"], StringSplitOptions.None);

	public static byte[] WriteLines(string[] lines, bool hadBom)
		=> Encoding.Unicode.GetBytes(string.Join("\r\n", lines));

	/// <summary>
	/// Ingilizce varyantlari cikarir. Yalnizca Portekizce karsiligi OLAN
	/// varyantlar dondurulur — karsiligi yoksa yazacak yer de yok.
	/// </summary>
	public static List<Entry> Parse(string[] lines) {
		var english = new Dictionary<(int, int), string>();
		var localizedCounts = new Dictionary<int, int>();

		var descIndex = -1;
		var lang = "";           // bos = Ingilizce varsayilan
		var variantIndex = 0;

		foreach (var line in lines) {
			if (DescriptionRx().IsMatch(line)) {
				++descIndex; lang = ""; variantIndex = 0;
				continue;
			}
			var lm = LangRx().Match(line);
			if (lm.Success) { lang = lm.Groups[1].Value; variantIndex = 0; continue; }
			if (descIndex < 0) continue;

			var vm = VariantRx().Match(line);
			if (!vm.Success) continue;

			if (lang.Length == 0)
				english[(descIndex, variantIndex)] = vm.Groups["txt"].Value;
			else if (lang == Localized)
				localizedCounts[descIndex] = Math.Max(localizedCounts.GetValueOrDefault(descIndex), variantIndex + 1);
			++variantIndex;
		}

		var result = new List<Entry>();
		foreach (var ((d, v), text) in english) {
			if (text.Length == 0) continue;
			if (localizedCounts.GetValueOrDefault(d) <= v) continue; // Portekizce karsiligi yok
			result.Add(new Entry(d, v, text));
		}
		return result;
	}

	/// <summary>
	/// Portekizce bolumlerdeki metinleri cevirilerle degistirir.
	/// Onek, kapanis tirnagi ve sonraki degistiriciler (negate 1 gibi) korunur.
	/// </summary>
	/// <returns>Degistirilen satir sayisi.</returns>
	public static int Rewrite(string[] lines, IReadOnlyDictionary<(int, int), string> translations) {
		var descIndex = -1;
		var lang = "";
		var variantIndex = 0;
		var changed = 0;

		for (var i = 0; i < lines.Length; ++i) {
			var line = lines[i];
			if (DescriptionRx().IsMatch(line)) { ++descIndex; lang = ""; variantIndex = 0; continue; }
			var lm = LangRx().Match(line);
			if (lm.Success) { lang = lm.Groups[1].Value; variantIndex = 0; continue; }
			if (descIndex < 0) continue;

			var vm = VariantRx().Match(line);
			if (!vm.Success) continue;

			if (lang == Localized && translations.TryGetValue((descIndex, variantIndex), out var tr)) {
				lines[i] = vm.Groups["pre"].Value + Escape(tr) + vm.Groups["post"].Value;
				++changed;
			}
			++variantIndex;
		}
		return changed;
	}

	/// <summary>Metin tirnak icine gomulecek; tirnak ve ters bolu kacisli olmali.</summary>
	private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
