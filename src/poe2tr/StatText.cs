using System.Text.RegularExpressions;

namespace Poe2Tr;

/// <summary>
/// Stat/mod satirlarinin Turkce bicimini tek kaliba oturtur.
///
/// NEDEN VAR: bu satirlar duz cumle degil, kalip. Oyuncu onlari okumuyor,
/// TARIYOR - ayni bilgi her eşyada ayni yerde durmali. Model ise ayni kalibi
/// her seferinde baska kurmustu; olcum:
///
///   {0}% increased ...  -> 2.874 satirda "{0}%", 2.843 satirda "%{0}"
///                          1.121 satirda sifat sona atilmis ("Zirh %{0} artirilmis")
///   {0:+d} to X         -> 26 satirda "ila" ("+10 ila Ceviklik")
///
/// "ila" ARALIK demek. "{0} to {1} Fire Damage" -> "{0} ila {1} Ates Hasari"
/// DOGRU; "+10 to Dexterity" -> "+10 ila Ceviklik" YANLIS. Ayrim: "to"nun
/// sagindaki sey bir yer tutucu mu, degil mi.
///
/// Kanonik bicim:
///   {0}% increased Armour   -> %{0} artirilmis Zirh
///   {0}% reduced Armour     -> %{0} azaltilmis Zirh
///   {0:+d} to Dexterity     -> {0:+d} Ceviklik
///   {0:+d}% to Fire Res     -> {0:+d}% Ates Direnci
///
/// Direnclerde yuzde sayidan SONRA kaliyor, cunku isareti yer tutucunun
/// kendisi basiyor: "%{0:+d}" ekranda "%+35" olur. "+35%" daha az kotu.
/// </summary>
public static partial class StatText {
	// DIKKAT — yer tutucunun INDEKSI OLMAYABILIR.
	//
	// Kaynakta iki bicim var: "{0:+d}" ve "{:+d}". Bu regexler once yalnizca
	// \d+ kabul ediyordu; indekssiz olanlar hem duzeltmeden hem DOGRULAMADAN
	// kaciyordu. Sonuc: oyuna "{%+d}" diye bozulmus bir yer tutucu gitti ve
	// hicbir uyari vermedi. Bu yuzden her yerde \d* (indeks istege bagli).
	//
	// Gruplar isimli: yakalanan sey indeks degil, YER TUTUCUNUN TAMAMI.
	// Indeksten regex kurmak indekssiz bicimde bos string uretip patliyordu.

	/// <summary>Yer tutucudan sonra gelen yuzde: "{0}%", "{:d}%".</summary>
	[GeneratedRegex(@"(?<ph>\{(?<idx>\d*)(?::(?<spec>[^}]*))?\})%")] private static partial Regex TrailingPercentRx();

	/// <summary>Kaynakta "to"nun ARALIK olmadigi yer tutucular: "{0:+d} to X", "{:+d} to X".</summary>
	[GeneratedRegex(@"(?<ph>\{\d*(?::[^}]*)?\})(?<pct>%?)\s+to\s+(?!\{)")] private static partial Regex NonRangeToRx();

	/// <summary>"{0}% increased/reduced ..." ile BASLAYAN tek satirlik stat.</summary>
	[GeneratedRegex(@"^(?<ph>\{\d*\})%\s+(?<yon>increased|reduced)\b")] private static partial Regex IncReducedRx();

	/// <summary>"{0:+d} to X" / "{:+d}% to X" ile baslayan stat.</summary>
	[GeneratedRegex(@"^(?<ph>\{\d*:\+d\})(?<pct>%?)\s+to\s")] private static partial Regex PlusToRx();

	/// <summary>Modelin cevap bicimi: &lt;1&gt; ... &lt;/1&gt;. Oyun metninde asla bulunmaz.</summary>
	[GeneratedRegex(@"</?\d+>")] private static partial Regex BlokEtiketRx();

	/// <summary>Bicim etiketi: &lt;i&gt;, &lt;white&gt;, &lt;rgb(1,2,3)&gt;...</summary>
	[GeneratedRegex(@"<[^>]+>")] private static partial Regex TagRx();

	/// <summary>Kaynak bastan sona tek bir koseli parantezden ibaret: "[Sustained]".</summary>
	[GeneratedRegex(@"^\[([^\]\|]+)\]$")] private static partial Regex TekEtiketRx();

	/// <summary>[Anahtar|Gorunen]. Masking'dekiyle ayni daraltmalar.</summary>
	[GeneratedRegex(@"\[([^\]\|{}<>]+)\|([^\]]*)\]")] private static partial Regex AnahtarRx();

	/// <summary>
	/// Sifat sona atilinca cumleyi bitiren kaliplar. Anlam yonune gore ayri:
	/// "increased" satirinda "azalir" gorursek bu dizim degil ANLAM hatasidir,
	/// makineyle duzeltilmez, modele geri gitmeli.
	/// </summary>
	private static readonly string[] ArtiranSon = [
		"artırılmış", "arttırılmış", "artırıldı", "artırılır", "arttırılır", "artar", "artış",
	];
	private static readonly string[] AzaltanSon = [
		"azaltılmış", "azaltıldı", "azaltılır", "azalır", "azalış", "düşürülmüş",
	];

	private static string[] SonEkleri(string english) =>
		english == "increased" ? ArtiranSon : AzaltanSon;

	private static string Verb(string english) => english switch {
		"increased" => "artırılmış",
		_ => "azaltılmış",
	};

	/// <summary>
	/// Guvenle otomatik duzeltilebilenleri duzeltir. Anlama dokunmaz, yalnizca
	/// yazim/dizim: yuzde konumu ve yanlis "ila".
	/// </summary>
	public static string Normalize(string en, string tr) {
		if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(tr)) return tr;

		// 0a) Satir sonu KACISI gercek satir sonuna donmus mu?
		//
		// .csd icinde bir aciklama tek satirdir; icindeki satir sonu iki
		// KARAKTERLIK "\n" kacisiyla yazilir. Model bunu gercek satir sonuna
		// cevirdiginde dosyada o aciklama parcalaniyor. csd-check bunu
		// "gidis-donusta 6 bayt buyudu" diye gosterdi (LF -> CRLF).
		//
		// Masking.RepairLineBreaks bunu yakalayamaz: o GERCEK satir sonlarini
		// sayiyor ve kaynakta hic yok, dolayisiyla "onarilacak bir sey yok" diyor.
		// Iki ayri durum var ve ikincisi ilk turda gozden kacti:
		//   a) kaynakta "\n" KACISI var -> ceviridekini kacisa geri cevir
		//   b) kaynakta hicbir satir sonu YOK -> ceviridekini tamamen kaldir
		// (b)'yi Masking.RepairLineBreaks yakalayamiyor: kaynakta satir sonu
		// olmayinca "onarilacak bir sey yok" deyip cikiyor, modelin ekledigi
		// satir sonu oldugu gibi geciyor.
		if (!en.Contains('\n') && !en.Contains('\r') && (tr.Contains('\n') || tr.Contains('\r')))
			tr = en.Contains(@"\n", StringComparison.Ordinal)
				? Regex.Replace(tr, @"\r\n|\n|\r", @"\n")
				: Regex.Replace(tr, @"[ \t]*(?:\r\n|\n|\r)[ \t]*", " ").Trim();

		// 0a2) INDEKSSIZ VE BICIMSIZ yer tutucu: "{}"
		//
		// Ucuncu bir yer tutucu bicimi daha var ve en kirilgani bu: "Take {}% more
		// Damage". Icinde tutunacak hicbir karakter olmadigi icin model onu
		// "{ }" (bosluk eklemis) ya da "{%0}" (yuzdeyi iceri almis) yapiyor.
		// Ikisi de oyunda birebir oyle gorunur - sayi hic basilmaz.
		if (en.Contains("{}", StringComparison.Ordinal) && !tr.Contains("{}", StringComparison.Ordinal)) {
			tr = Regex.Replace(tr, @"\{\s+\}", "{}");                    // "{ }"  -> "{}"
			tr = Regex.Replace(tr, @"\{%\d*\}", en.Contains("{}%", StringComparison.Ordinal) ? "%{}" : "{}");
		}

		// 0a3) Modelin uydurdugu bicim etiketi
		//
		// Kaynakta hic etiket yokken ceviride "<span>", "<span class=...>" gibi
		// HTML kaliplari cikiyor. Oyun bunlari tanimiyor, ekrana oldugu gibi
		// yaziyor. Kaynakta yoksa ceviride de olmamali.
		if (!TagRx().IsMatch(en) && TagRx().IsMatch(tr))
			tr = TagRx().Replace(tr, "");

		// 0a4) Kaynak TAMAMEN tek bir koseli parantezse, ceviri de oyle olmali.
		//
		//   kaynak  [Sustained]
		//   ceviri  Sürekli[Sustained|Sustained]     <- ceviri parantezin DISINDA
		//   ceviri  [Staff|Asa].                     <- sonda uydurma nokta
		//
		// Bu alanlar (ornegin gemtags|Name) yalnizca bir etiket adi tasiyor;
		// oyun parantezin GORUNEN kismini basiyor, disarida kalan her sey
		// ekranda da oyle goruunuyor ("SürekliSustained").
		// Dogrulama bunu yakalayamiyor: parantez sayisi tutuyor.
		var tekEtiket = TekEtiketRx().Match(en);
		if (tekEtiket.Success) {
			var m = Regex.Match(tr, @"^(?<on>[^\[]*)\[(?<anahtar>[^\]\|]+)(?:\|(?<gorunen>[^\]]*))?\](?<son>.*)$");
			if (m.Success) {
				var disarida = (m.Groups["on"].Value + m.Groups["son"].Value).Trim(' ', '.', ',', ':', '\t');
				var icerik = m.Groups["gorunen"].Success ? m.Groups["gorunen"].Value : m.Groups["anahtar"].Value;
				// Icerik anahtarla ayniysa cevrilmemis demektir; disarida bir sey
				// varsa gercek ceviri odur.
				if (string.Equals(icerik, tekEtiket.Groups[1].Value, StringComparison.Ordinal) && disarida.Length > 0)
					icerik = disarida;
				tr = $"[{tekEtiket.Groups[1].Value}|{icerik}]";
			}
		}

		// 0b) Modelin CEVAP BICIMI ceviriye sizmis mi?
		//
		// Modele "<1>metin</1>" biçiminde numarali bloklar gonderiyoruz. Model
		// bazen kapanis numarasini kaydiriyor ("<7>...</8>"); ParseReply yalnizca
		// tam eslesen kapanisi kirptigi icin artik metnin icinde kaliyor ve
		// oyunda "</8>" diye gorunuyor. Olcum: 1.066 kayit.
		// Kaynakta boyle bir sey asla yok, o yuzden silmek guvenli.
		if (BlokEtiketRx().IsMatch(tr) && !BlokEtiketRx().IsMatch(en))
			tr = BlokEtiketRx().Replace(tr, "").TrimEnd();

		// 0c) Anahtar/gorunen metin takasi
		tr = AnahtarTakasiniDuzelt(tr);

		// 0d) sozlukten sapmis terimleri geri cek
		tr = TerimDuzelt(en, tr);

		// 1) "{0}%" -> "%{0}"
		//
		// ISARETLI yer tutucuda yapilmaz: "{0:+d}" ekrana "+35" basiyor, onune
		// yuzde koyunca "%+35" oluyor. "+35%" daha az kotu, oyle birakiliyor.
		// Isaretsizde ("{0}", "{:d}") Turkce yazim yuzdeyi one aliyor.
		tr = TrailingPercentRx().Replace(tr, m => {
			var ph = m.Groups["ph"].Value;
			if (m.Groups["spec"].Value.Contains('+')) return m.Value;
			return en.Contains(ph + "%", StringComparison.Ordinal) ? "%" + ph : m.Value;
		});

		// 2) aralik olmayan "to" icin uydurulmus "ila"yi at
		foreach (Match m in NonRangeToRx().Matches(en)) {
			var jeton = m.Groups["ph"].Value + m.Groups["pct"].Value;
			// "{0:+d} ila X" -> "{0:+d} X" ; sagda yer tutucu varsa gercek aralik, dokunma
			tr = Regex.Replace(tr, Regex.Escape(jeton) + @"\s+ila\s+(?!\{)", jeton + " ");
		}

		// 3) yer tutucuya yapismis hâl eki: "{0:+d}%'e Soguk Direnc" -> "{0:+d}% Soguk Direnc"
		//    Sayi ek almaz; model Ingilizce "to"yu Turkce hâl ekiyle karsilamaya
		//    calisirken onu yer tutucunun uzerine yapistiriyor.
		var plus = PlusToRx().Match(en);
		if (plus.Success) {
			var bas = plus.Groups["ph"].Value + plus.Groups["pct"].Value;
			if (tr.StartsWith(bas, StringComparison.Ordinal))
				tr = Regex.Replace(tr, @"^" + Regex.Escape(bas) + @"['’]\p{L}{1,4}(?=\s|$)", bas);
		}

		// 4) sifati onune al: "%{0} Etki Alani artirilmis" -> "%{0} artirilmis Etki Alani"
		tr = SifatiOneAl(en, tr);

		// 5) Stat satiri bir ISIM OBEGIDIR, cumle degil - hâl ekiyle bitmez.
		//    "%{0} artirilmis [Evasion|Kacinma Degeri]'ne" -> "... Degeri"
		if (IncReducedRx().IsMatch(en) || plus.Success)
			tr = Regex.Replace(tr, @"['’]\p{L}{1,4}(?=[.\s]*$)", "");

		return tr;
	}

	/// <summary>
	/// Turkcede sifat ismin ONUNE gelir. Model sikca Ingilizce sozdizimini birebir
	/// izleyip "artirilmis"i sona atiyor. Tek satirlik stat kaliplarinda bu tamamen
	/// mekanik bir takas: aradaki isim obegi oldugu gibi kayiyor, kelime cevrilmiyor.
	///
	/// Yalnizca EN'in fiiliyle TR'nin fiili AYNI YONDE ise dokunulur; "increased"
	/// satirinda "azalir" gormek dizim degil anlam hatasidir ve modele geri gider.
	/// </summary>
	private static string SifatiOneAl(string en, string tr) {
		var inc = IncReducedRx().Match(en);
		if (!inc.Success) return tr;
		if (tr.Contains('\n') || tr.Contains('\r')) return tr; // cok satirli, riskli

		var ph = inc.Groups["ph"].Value;          // "{0}" ya da "{}"
		var yon = inc.Groups["yon"].Value;
		var kanonik = Verb(yon);
		var head = $"%{ph} {kanonik}";
		// DIKKAT: devrik bicim de bu kalibi ICERIR ("Hasar %{0} artirilmis"), o yuzden
		// tek basina "iceriyor mu" yetmiyor - sonu fiille bitiyorsa hâlâ devriktir.
		if (tr.Contains(head, StringComparison.Ordinal) && !FiilleBitiyor(tr))
			return tr; // zaten kalipta

		var phRx = Regex.Escape("%" + ph);
		foreach (var son in SonEkleri(yon)) {
			// A) <on>%{N} <isim obegi> <fiil><noktalama>
			//    "Olu iken %{0} Etki Alani artirilmis" -> "Olu iken %{0} artirilmis Etki Alani"
			var m = Regex.Match(tr,
				@"^(?<on>.*?)" + phRx + @"\s+(?<orta>\S.*?)\s+" + Regex.Escape(son) + @"(?<son>[.\s]*)$");
			if (m.Success) {
				var orta = m.Groups["orta"].Value;
				// Ortada baska bir yer tutucu varsa cumle sandigimizdan karmasik
				if (!orta.Contains('{'))
					return $"{m.Groups["on"].Value}%{ph} {kanonik} {EkiAt(orta)}{m.Groups["son"].Value}";
			}

			// B) <isim obegi> %{N} <fiil><noktalama>
			//    "Hasar %{0} artirilmis" -> "%{0} artirilmis Hasar"
			//    Burada onde bir kosul cumlesi olsaydi ("Olu iken ...") onu da isim
			//    sanip sona atardik. O yuzden yalnizca kaynagi DUZ ISIM OBEGI olan
			//    satirlarda uygulaniyor.
			if (!DuzIsimObegi(en, inc.Length)) continue;
			m = Regex.Match(tr,
				@"^(?<isim>\S.*?)\s+" + phRx + @"\s+" + Regex.Escape(son) + @"(?<son>[.\s]*)$");
			if (!m.Success) continue;
			var isim = m.Groups["isim"].Value;
			if (isim.Contains('{')) continue;
			return $"%{ph} {kanonik} {EkiAt(isim)}{m.Groups["son"].Value}";
		}
		return tr;
	}

	/// <summary>
	/// terim-duzeltme.tsv'den yuklenen kurallar: (kaynak kosulu, aranan, yerine).
	/// Bos kalirsa hicbir sey yapmaz - dosya istege bagli.
	/// </summary>
	private static (Regex Kaynak, Regex Aranan, string Yerine)[] terimKurallari = [];

	public static int TerimKuraliSayisi => terimKurallari.Length;

	public static void TerimKurallariniYukle(string? yol) {
		if (yol is null || !File.Exists(yol)) { terimKurallari = []; return; }
		var liste = new List<(Regex, Regex, string)>();
		foreach (var ham in File.ReadLines(yol)) {
			var satir = ham.TrimEnd();
			if (satir.Length == 0 || satir.StartsWith('#')) continue;
			var p = satir.Split('\t');
			if (p.Length < 3) continue;
			liste.Add((new Regex(p[0], RegexOptions.Compiled), new Regex(p[1], RegexOptions.Compiled), p[2]));
		}
		terimKurallari = [.. liste];
	}

	/// <summary>
	/// Sozlukten sapmis terimleri geri ceker. Yalnizca KAYNAK kosulu tutuyorsa
	/// uygulanir; yoksa dogru cevrilmis baska bir baglami ezme riski var.
	/// </summary>
	public static string TerimDuzelt(string en, string tr) {
		foreach (var (kaynak, aranan, yerine) in terimKurallari) {
			if (!kaynak.IsMatch(en)) continue;
			tr = aranan.Replace(tr, yerine);
		}
		return tr;
	}

	/// <summary>sozluk.tsv: Ingilizce anahtar -> Turkce karsilik.</summary>
	private static Dictionary<string, string> sozluk = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Ayni sozluk, anahtarlari NORMALLESTIRILMIS hâliyle.
	///
	/// Oyunun koseli parantez anahtarlari bitisik yazilir, sozluk ise dogal
	/// yaziliyla tutuluyor: "[EnergyShield|...]" ama sozlukte "Energy Shield".
	/// Bu tek basina 22 anahtari (1.352 gecis) sozluk disi gosteriyordu.
	///
	/// Sozluge kopya girdi eklemek yerine ARAMAYI normallestirmek dogru cozum:
	/// elle onaylanmis sozluk sismiyor ve ileride cikacak yeni yazim varyantlari
	/// da kendiliginden eslesiyor.
	/// </summary>
	private static Dictionary<string, string> sozlukNorm = new(StringComparer.Ordinal);

	private static string AnahtarNormu(string s) {
		var sb = new System.Text.StringBuilder(s.Length);
		foreach (var c in s)
			if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
		return sb.ToString();
	}

	public static void SozlukYukle(string? yol) {
		sozluk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		sozlukNorm = new Dictionary<string, string>(StringComparer.Ordinal);
		if (yol is null || !File.Exists(yol)) return;
		foreach (var ham in File.ReadLines(yol)) {
			var satir = ham.Trim();
			if (satir.Length == 0 || satir.StartsWith('#')) continue;
			var p = satir.Split('\t');
			if (p.Length < 2 || p[0].Length == 0) continue;
			sozluk[p[0]] = p[1];
			var n = AnahtarNormu(p[0]);
			// Ilk giren kazanir: sozlukte once gelen dogal yazim asil kabul edilir
			if (n.Length > 0 && !sozlukNorm.ContainsKey(n)) sozlukNorm[n] = p[1];
		}
	}

	/// <summary>Once birebir, sonra normallestirilmis eslesme.</summary>
	private static bool Karsilik(string anahtar, out string karsilik) {
		if (sozluk.TryGetValue(anahtar, out karsilik!) && karsilik.Length > 0) return true;
		return sozlukNorm.TryGetValue(AnahtarNormu(anahtar), out karsilik!) && karsilik.Length > 0;
	}

	/// <summary>
	/// Isme eklenebilen Turkce ekler. Yalnizca AD CEKIMI; yapim eki YOK.
	///
	/// Neden liste, neden "onekse yeter" degil: Turkce eklemeli bir dil, gorunen
	/// metin cumledeki yerine gore ek aliyor ("Vurus", "Vuruslar", "Vuruslari").
	/// Ama duz onek karsilastirmasi "Guc" ile "Guclendirme"yi de esler - biri
	/// Strength, oteki Buff. Ek listesi bu carpismayi kesiyor: "lendirme" burada
	/// yok. Olcum: HitDamage tek basina 1.252 gecis ve karsiliklarinin %61'i
	/// cekimli oldugu icin birebir karsilastirmayla gorunmuyordu.
	/// </summary>
	private static readonly string[] AdCekimEkleri = [
		"", "lar", "ler",
		"ı", "i", "u", "ü", "yı", "yi", "yu", "yü", "sı", "si", "su", "sü",
		"ın", "in", "un", "ün", "nın", "nin", "nun", "nün",
		"a", "e", "ya", "ye", "na", "ne",
		"da", "de", "ta", "te", "nda", "nde",
		"dan", "den", "tan", "ten", "ndan", "nden",
		"nı", "ni", "nu", "nü", "la", "le", "yla", "yle",
		"ları", "leri", "lara", "lere", "larda", "lerde", "lardan", "lerden",
		"ların", "lerin", "larını", "lerini", "ları'", "leri'",
	];

	/// <summary>Gorunen metin, beklenen karsiligin cekimli bir hâli mi?</summary>
	private static bool AyniTerim(string gorunen, string beklenen) {
		if (string.Equals(gorunen, beklenen, StringComparison.Ordinal)) return true;
		if (gorunen.Length <= beklenen.Length) return false;
		if (!gorunen.StartsWith(beklenen, StringComparison.Ordinal)) return false;
		var ek = gorunen[beklenen.Length..];
		if (ek.Length > 7) return false;
		return AdCekimEkleri.Contains(ek, StringComparer.Ordinal);
	}

	/// <summary>
	/// Anahtar ile gorunen metnin yer degistirmesini onarir.
	///
	///   EN  Trigger this Spell on [Melee] Hit while [Curse|Cursed]
	///   TR  [Melee|Lanetli] iken bir [Curse|Yakin Dovus] Vurusu ile ...
	///
	/// Gorunen metinler dogru cevrilmis ama YANLIS anahtara baglanmis. Yapisal
	/// dogrulama bunu goremiyor (sayilar tutuyor); oyunda yanlis terime baglanti
	/// verir. Sebebi Masking.Restore'un anahtarlari KONUMA gore geri koymasi:
	/// model cumleyi Turkce soz dizimine gore yeniden siraladiginda konumlar
	/// kayiyor. Olcum: 494 kayit.
	///
	/// Yalnizca TAM PERMUTASYON durumunda dokunuluyor: metindeki anahtarlarin
	/// sozlukteki karsiliklari, gorunen metinlerin kumesiyle birebir ayni olmali.
	/// Boylece "sozluk disi ama dogru" bir ceviri yanlislikla ezilmiyor.
	/// </summary>
	public static string AnahtarTakasiniDuzelt(string tr) {
		if (sozluk.Count == 0) return tr;
		var eslesmeler = AnahtarRx().Matches(tr);
		if (eslesmeler.Count < 2) return tr;

		var n = eslesmeler.Count;
		var anahtarlar = new string[n];
		var gorunenler = new string[n];
		for (var i = 0; i < n; ++i) {
			anahtarlar[i] = eslesmeler[i].Groups[1].Value;
			gorunenler[i] = eslesmeler[i].Groups[2].Value;
		}

		// IKILI TAKAS - kasten dar.
		//
		// Once "her gorunen metni sozlukteki anahtarina bagla, kalanlari sirayla
		// doldur" denendi. Bu, DOGRU duran ciftleri de kaydiriyordu: bir eslesme
		// bulununca kalanlar kuyrugu kayiyor ve yanindaki dogru cift bozuluyordu.
		// Ornekte [HitDamage|Vuruslarinla] dogruydu, [Affinity|Vuruslarinla] oldu.
		//
		// Bu yuzden yalnizca su kosulda dokunuluyor: i ve j'nin IKISI de yanlis,
		// ve i'nin bekledigi metin j'de duruyor. Takas en az birini dogru yapar,
		// otekini bozamaz - zaten yanlisti. Baska hicbir konuma dokunulmuyor.
		var yeni = (string[])gorunenler.Clone();
		var sabitlendi = new bool[n];

		for (var i = 0; i < n; ++i) {
			if (sabitlendi[i]) continue;
			if (!Karsilik(anahtarlar[i], out var bekliI)) continue;
			if (AyniTerim(yeni[i], bekliI)) { sabitlendi[i] = true; continue; }  // zaten dogru

			for (var j = 0; j < n; ++j) {
				if (j == i || sabitlendi[j]) continue;
				// Ayni anahtarin iki gecisi arasinda takas anlamsiz churn
				if (string.Equals(anahtarlar[j], anahtarlar[i], StringComparison.OrdinalIgnoreCase)) continue;
				if (!AyniTerim(yeni[j], bekliI)) continue;
				// j de yanlis olmali; dogruysa onu bozmayalim
				if (Karsilik(anahtarlar[j], out var bekliJ) && AyniTerim(yeni[j], bekliJ)) continue;

				(yeni[i], yeni[j]) = (yeni[j], yeni[i]);
				sabitlendi[i] = sabitlendi[j] = true;
				break;
			}
		}

		if (yeni.SequenceEqual(gorunenler, StringComparer.Ordinal)) return tr;

		var sira = -1;
		return AnahtarRx().Replace(tr, m => $"[{anahtarlar[++sira]}|{yeni[sira]}]");
	}

	/// <summary>Ceviri, sifat/fiil kalibiyla mi bitiyor? (devrik isaretcisi)</summary>
	private static bool FiilleBitiyor(string tr) {
		var kuyruk = tr.TrimEnd(' ', '.', '\r', '\n');
		return ArtiranSon.Concat(AzaltanSon).Any(v => kuyruk.EndsWith(v, StringComparison.Ordinal));
	}

	/// <summary>
	/// Kaynak, fiilden sonra duz bir isim obegi mi? Yan cumle baglaci varsa
	/// Turkce onu basa alir ve isim obegini sona atma kurali guvenli olmaz.
	/// </summary>
	private static bool DuzIsimObegi(string en, int basUzunlugu) {
		var kalan = en[basUzunlugu..];
		return !Regex.IsMatch(kalan, @"\b(while|when|if|per|during|each|unless|after|before|as)\b",
			RegexOptions.IgnoreCase) && !kalan.Contains('\n');
	}

	/// <summary>
	/// Isim obegi cumlenin nesnesi/tumleciyken hâl eki almis olabilir
	/// ("...Nadirligini artirilmis", "[Evasion|Kacinma Degeri]'ne %{0} artirilmis").
	/// Basa alinca o ek dusmeli, yoksa "artirilmis Kacinma Degeri'ne" gibi
	/// yarim kalmis bir obek cikiyor.
	/// </summary>
	private static string EkiAt(string isim) {
		// Kesme isaretiyle baglanan hâl eki: bu konumda her zaman ektir.
		isim = Regex.Replace(isim, @"['’]\p{L}{1,4}$", "");
		// Iyelikten sonra gelen belirtme eki -nI. Tek basina -I belirsiz
		// (iyelik eki de olabilir), ona dokunulmuyor.
		var m = Regex.Match(isim, @"\w{4,}(nı|ni|nu|nü)$");
		return m.Success ? isim[..^2] : isim;
	}

	/// <summary>
	/// Kalipa uymayan cevirileri bildirir. <c>null</c> = sorun yok.
	/// Yalnizca MAKINEYLE duzeltilemeyen dizim hatalarina bakar; yazim
	/// hatalari icin once <see cref="Normalize"/> uygulanmis olmali.
	/// </summary>
	public static string? Problem(string en, string tr) {
		if (string.IsNullOrEmpty(en) || string.IsNullOrWhiteSpace(tr)) return null;

		// 0) Susly parantez SAYISI birebir tutmali.
		//
		// Oyunda suslu parantezin iki isi var: yer tutucu ({0}) ve bicim
		// etiketinin icerigi (<red>{metin}). Ikincisi denetlenmiyordu; model
		// ya parantezleri tamamen dusuruyor (<red>{...} -> <red> ...) ya da
		// olmayan yere ekliyor. Ikisi de oyunda bicimlendirmeyi bozuyor.
		// Yer tutucu denetimi bunu yakalamiyor cunku {0} sayilari tutuyor.
		int EnAc = en.Count(c => c == '{'), EnKa = en.Count(c => c == '}');
		int TrAc = tr.Count(c => c == '{'), TrKa = tr.Count(c => c == '}');
		if (EnAc != TrAc || EnKa != TrKa)
			return $"suslu parantez: kaynak {EnAc}/{EnKa}, ceviri {TrAc}/{TrKa}";

		// A) uydurulmus "ila"
		foreach (Match m in NonRangeToRx().Matches(en)) {
			var jeton = m.Groups["ph"].Value + m.Groups["pct"].Value;
			if (Regex.IsMatch(tr, Regex.Escape(jeton) + @"\s+ila\s"))
				return "aralik olmayan 'to' icin 'ila' kullanilmis";
		}

		// B) yer tutucudan sonra yuzde (isaretli olanlar haric, bkz. Normalize)
		foreach (Match m in TrailingPercentRx().Matches(tr)) {
			var ph = m.Groups["ph"].Value;
			if (m.Groups["spec"].Value.Contains('+')) continue;
			if (en.Contains(ph + "%", StringComparison.Ordinal))
				return $"yuzde sayidan sonra: {m.Value} yerine %{ph}";
		}

		// C) "{0}% increased/reduced X" -> "%{0} artirilmis X"
		var inc = IncReducedRx().Match(en);
		if (inc.Success) {
			var head = $"%{inc.Groups["ph"].Value} {Verb(inc.Groups["yon"].Value)}";
			if (!tr.Contains(head, StringComparison.Ordinal))
				return $"kalip disi: \"{head} ...\" bekleniyordu";

			var kuyruk = tr.TrimEnd(' ', '.', '\r', '\n');
			foreach (var yasak in ArtiranSon.Concat(AzaltanSon))
				if (kuyruk.EndsWith(yasak, StringComparison.Ordinal))
					return $"sifat sona atilmis: \"...{yasak}\"";
		}

		// D) "{0:+d} to X" -> "{0:+d} X"
		var plus = PlusToRx().Match(en);
		if (plus.Success) {
			var bas = plus.Groups["ph"].Value + plus.Groups["pct"].Value;
			if (!tr.TrimStart().StartsWith(bas, StringComparison.Ordinal))
				return $"kalip disi: \"{bas} ...\" ile baslamaliydi";
		}

		return null;
	}
}
