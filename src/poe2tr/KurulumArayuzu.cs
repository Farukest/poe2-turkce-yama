namespace Poe2Tr;

public enum KurulumRenk { Normal, Basarili, Uyari, Hata, Baslik, Solgun }

/// <summary>
/// Kurulum akisinin disari konustugu tek nokta.
///
/// NEDEN VAR: ayni akis hem pencerede hem komut satirinda calisiyor.
/// Kurulum.cs'in icine Console.WriteLine serpistirilseydi pencere surumu
/// icin kodu ikiye ayirmak gerekirdi ve ikisi zamanla birbirinden kayardi.
/// </summary>
public interface IKurulumArayuzu {
	void Yaz(string metin, KurulumRenk renk = KurulumRenk.Normal);
	/// <summary>Evet/hayir sorusu. Iptal = false.</summary>
	bool Sor(string soru);
	/// <summary>Kullanicidan bir dosya/klasor yolu ister. Vazgecerse null.</summary>
	string? YolIste(string mesaj, bool klasor);
	/// <summary>Uzun surecek bir adima girildi/cikildi.</summary>
	void Adim(string? baslik);
	/// <summary>Akis bitti. <paramref name="basarili"/> false ise hata ile bitti.</summary>
	void Bitti(bool basarili);
}

/// <summary>Komut satiri surumu — `poe2tr kur` / `poe2tr guncelle`.</summary>
public sealed class KonsolArayuzu : IKurulumArayuzu {
	public void Yaz(string metin, KurulumRenk renk = KurulumRenk.Normal) {
		var eski = Console.ForegroundColor;
		Console.ForegroundColor = renk switch {
			KurulumRenk.Basarili => ConsoleColor.Green,
			KurulumRenk.Uyari => ConsoleColor.Yellow,
			KurulumRenk.Hata => ConsoleColor.Red,
			KurulumRenk.Baslik => ConsoleColor.Cyan,
			KurulumRenk.Solgun => ConsoleColor.DarkGray,
			_ => eski,
		};
		Console.WriteLine(metin);
		Console.ForegroundColor = eski;
	}

	public bool Sor(string soru) {
		Console.Write($"{soru} [E/h] ");
		var c = Console.ReadLine()?.Trim();
		return string.IsNullOrEmpty(c)
			|| c.StartsWith("e", StringComparison.OrdinalIgnoreCase)
			|| c.StartsWith("y", StringComparison.OrdinalIgnoreCase);
	}

	public string? YolIste(string mesaj, bool klasor) {
		Yaz(mesaj);
		Console.Write("> ");
		var g = Console.ReadLine()?.Trim().Trim('"');
		return string.IsNullOrWhiteSpace(g) ? null : g;
	}

	public void Adim(string? baslik) {
		if (baslik is not null) Yaz($"\n▶ {baslik}", KurulumRenk.Baslik);
	}

	public void Bitti(bool basarili) {
		Console.WriteLine("\nKapatmak için Enter'a bas...");
		Console.ReadLine();
	}
}
