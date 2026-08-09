using System.Text;
using System.Text.RegularExpressions;

namespace Poe2Tr;

/// <summary>
/// Ceviri oncesi hazirlik ve sonrasi onarim.
///
/// ÖNEMLİ TASARIM DERSİ: eskiden {0}, &lt;etiket&gt; ve satir sonlari da
/// ⟦0⟧ gibi isaretcilere cevriliyordu. Olcum bunun ZARARLI oldugunu gosterdi:
/// model ⟦⟧ karakterlerini anlamli icerik saymayip siliyor. Ayni 15 metinde
/// maskelemeli 0/15, maskelemesiz 10/15 basari alindi.
///
/// Modeller {0} ve &lt;i&gt; gibi yapilari yazilim yerellestirmesinden zaten
/// taniyor ve koruyor. O yuzden artik SADECE anahtar kelimeler ele aliniyor.
/// </summary>
public sealed partial class Masking {
	/// <summary>
	/// [Anahtar] veya [Anahtar|Gorunen].
	///
	/// Anahtar kisminda suslu/acili parantez OLAMAZ: oyunda "Open Temple Map [{0}]"
	/// gibi tus gostergeleri var ve bunlar anahtar degil. Eskiden regex onlari da
	/// yakaliyor, geri koyarken [{0}] -> [{0}|{0}] yapip yer tutucuyu ikiye
	/// katliyordu.
	/// </summary>
	[GeneratedRegex(@"\[([^\]\|{}<>]+)(?:\|([^\]]*))?\]")] private static partial Regex KeywordRx();
	/// <summary>
	/// {0} ve bicimli hâlleri: {0:+d}, {1:.1f} gibi. Eskiden yalnizca \{\d+\}
	/// araniyordu ve {0:+d} hic denetlenmiyordu.
	///
	/// INDEKS ISTEGE BAGLI (\d*): kaynakta "{:+d}" bicimi de var. \d+ sart
	/// kosulunca o yer tutucular denetim disi kaliyordu ve model onlari bozup
	/// oyuna gonderebiliyordu - gercekten de "{%+d}" diye bir kayit gecti.
	/// Suslu parantez icinde ya rakam ya da ":" ile baslayan bir bicim olmali;
	/// "{Metin}" (bicim etiketi icerigi) bilerek disarida, o CEVRILIR.
	/// </summary>
	[GeneratedRegex(@"\{\d*(?::[^}]*)?\}")] private static partial Regex PlaceholderRx();
	[GeneratedRegex(@"<[^>]+>")] private static partial Regex TagRx();
	[GeneratedRegex(@"\r\n|\n|\r")] private static partial Regex NewlineRx();

	/// <summary>Kaynaktaki anahtarlar, gectikleri sirayla.</summary>
	private string[] keys = [];
	private string[] sourcePlaceholders = [];
	private string[] sourceTags = [];
	private string[] sourceLineBreaks = [];
	private string source = "";

	/// <summary>
	/// Modele gonderilecek hâli hazirlar.
	///
	/// Anahtarin SOL tarafi tamamen cikarilir: [Chill|Chills] -> [Chills].
	/// Model boylece tertemiz bir koseli parantez goruyor. Sol taraflar
	/// <see cref="Restore"/> ile konuma gore geri konuyor - basarisiz
	/// metinlerin %96,9'unda en fazla 1 anahtar var, siralama riski dusuk.
	/// </summary>
	public string Prepare(string source) {
		this.source = source;
		keys = KeywordRx().Matches(source).Select(m => m.Groups[1].Value).ToArray();
		sourcePlaceholders = PlaceholderRx().Matches(source).Select(m => m.Value).Order(StringComparer.Ordinal).ToArray();
		sourceTags = TagRx().Matches(source).Select(m => m.Value).Order(StringComparer.Ordinal).ToArray();
		sourceLineBreaks = NewlineRx().Matches(source).Select(m => m.Value).ToArray();

		// [Key|Display] -> [Display] ; [Key] -> [Key]  (gorunen metin anahtarin kendisi)
		return KeywordRx().Replace(source,
			m => "[" + (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value) + "]");
	}

	/// <summary>
	/// Modelin TEK BICIMLI bir hatasini geri alir: yuzde isaretini Turkce
	/// sozdizimine gore one alirken susluları yutuyor.
	///
	///   kaynak  {0}% increased Armour
	///   cevap   %0 artirilmis Zirh        &lt;- yer tutucu gitti, oyunda "%0" yazar
	///
	/// Olcum: dogrulamayi gecemeyen stat satirlarinin neredeyse tamami buydu.
	/// Tahmin degil cikarim: kaynakta "{N}%" varsa ve ceviride "{N}" hic yoksa,
	/// ceviride tek basina duran "%N" o yer tutucunun kendisidir.
	/// </summary>
	public string Repair(string translated) {
		foreach (Match m in PlaceholderRx().Matches(source)) {
			var token = m.Value;                       // "{0}"
			if (!token.EndsWith("}", StringComparison.Ordinal)) continue;
			var inner = token[1..^1];                  // "0" ya da "0:+d"
			if (inner.Contains(':')) continue;         // bicimli olanlarda bu hata gorulmedi
			// "{}" bicimi: inner bos. Asagidaki regex "%" + "" olur ve rakam
			// gelmeyen HER yuzde isaretini yer tutucu sanip bozar. Onarimi
			// StatText.Normalize'daki hedefli kural yapiyor.
			if (inner.Length == 0) continue;
			if (!source.Contains(token + "%", StringComparison.Ordinal)) continue;
			if (translated.Contains(token, StringComparison.Ordinal)) continue;

			translated = Regex.Replace(translated, @"%" + Regex.Escape(inner) + @"(?!\d)", token switch {
				_ => "%" + token,
			});
		}
		return translated;
	}

	/// <summary>
	/// Cevirinin gecerli olup olmadigini soyler. Satir sonu farki HATA DEGIL -
	/// onarilabiliyor, bkz. <see cref="Restore"/>.
	/// </summary>
	/// <returns>Sorun yoksa null.</returns>
	public string? Validate(string translated) {
		var ph = PlaceholderRx().Matches(translated).Select(m => m.Value).Order(StringComparer.Ordinal).ToArray();
		if (!ph.SequenceEqual(sourcePlaceholders, StringComparer.Ordinal))
			return $"yer tutucu: [{string.Join(",", sourcePlaceholders)}] -> [{string.Join(",", ph)}]";

		var tg = TagRx().Matches(translated).Select(m => m.Value).Order(StringComparer.Ordinal).ToArray();
		if (!tg.SequenceEqual(sourceTags, StringComparer.Ordinal))
			return $"etiket: [{string.Join(",", sourceTags)}] -> [{string.Join(",", tg)}]";

		var brackets = KeywordRx().Matches(translated).Count;
		if (brackets != keys.Length)
			return $"anahtar sayisi: beklenen {keys.Length}, gelen {brackets}";

		// Yuzde isareti duz karakter oldugu icin eskiden denetlenmiyordu; model
		// stat metinlerinin %5,8'inde onu dusuruyordu ("{0}% reduced" -> "{0} azaltilmis").
		// Oyun mekanigi acisindan yanlis bilgi demek.
		var srcPercent = source.Count(c => c == '%');
		var trPercent = translated.Count(c => c == '%');
		if (trPercent < srcPercent)
			return $"yuzde isareti: kaynakta {srcPercent}, ceviride {trPercent}";

		if (translated.Trim().Length == 0)
			return "bos ceviri";

		// Stat satirlarinin kalibi. Once makineyle duzeltilebilenleri uygula,
		// kalan gercek dizim hatasiysa yeniden denemeye birak.
		var stat = StatText.Problem(source, StatText.Normalize(source, translated));
		if (stat is not null)
			return "stat kalibi: " + stat;

		return null;
	}

	/// <summary>
	/// Anahtarlarin sol tarafini geri koyar ve gerekiyorsa satir sonlarini onarir.
	/// <see cref="Validate"/> gectikten sonra cagrilmali.
	/// </summary>
	public string Restore(string translated) {
		var i = 0;
		var withKeys = KeywordRx().Replace(translated, m => {
			var display = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value;
			var key = i < keys.Length ? keys[i] : display;
			++i;
			return $"[{key}|{display}]";
		});

		return StatText.Normalize(source, RepairLineBreaks(withKeys));
	}

	/// <summary>
	/// Model satir sonlarini sikca yutuyor (iki satiri birlestiriyor). Bunu
	/// modelden istemek yerine kendimiz onariyoruz: ceviriyi kaynaktakiyle
	/// AYNI SAYIDA satira, kelime sinirlarindan bolerek yeniden sariyoruz.
	///
	/// Bu metinler cogunlukla gorsel olarak sabit genislige sarilmis "flavour
	/// text"; nerede bolundugu anlami degistirmiyor.
	/// </summary>
	private string RepairLineBreaks(string text) {
		if (sourceLineBreaks.Length == 0)
			return text;

		var existing = NewlineRx().Matches(text).Count;
		if (existing == sourceLineBreaks.Length)
			return text; // model dogru sayida birakmis, dokunma

		// Tek satira indirgeyip kaynaktaki satir sayisina yeniden bol
		var flat = NewlineRx().Replace(text, " ").Trim();
		var words = flat.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var lineCount = sourceLineBreaks.Length + 1;
		if (words.Length < lineCount)
			return text; // bolecek kadar kelime yok, oldugu gibi birak

		var perLine = (int)Math.Ceiling(words.Length / (double)lineCount);
		var sb = new StringBuilder();
		var w = 0;
		for (var line = 0; line < lineCount; ++line) {
			if (line > 0)
				sb.Append(sourceLineBreaks[Math.Min(line - 1, sourceLineBreaks.Length - 1)]);
			var take = line == lineCount - 1 ? words.Length - w : Math.Min(perLine, words.Length - w);
			for (var k = 0; k < take; ++k) {
				if (k > 0) sb.Append(' ');
				sb.Append(words[w++]);
			}
		}
		return sb.ToString();
	}
}
