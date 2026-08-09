using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Poe2Tr;

/// <summary>
/// Son kullanıcı için kurulum ve güncelleme akışı.
///
/// TASARIM KARARI — korpus PAKETTEN GELMEZ, oyundan üretilir.
/// Korpus birimleri satır numarası taşıyor (`mods|98|0`). Oyun güncellenip
/// tablolara satır eklendiğinde numaralar kayar; hazır korpusla yazmak
/// çevirileri YANLIŞ satırlara koyar ve hiçbir uyarı vermez. Bu yüzden hem
/// kurulumda hem güncellemede korpus, kullanıcının kendi kurulumundan
/// yeniden çıkarılıyor. Çeviri belleği İngilizce metinle anahtarlandığı için
/// sürümden bağımsız; taşınan tek şey o.
///
/// Yan fayda: pakete oyunun İngilizce metni girmiyor (~24 MB tasarruf).
///
/// Ekrana yazma ve soru sorma <see cref="IKurulumArayuzu"/> üzerinden;
/// aynı akış hem pencerede hem komut satırında çalışıyor.
/// </summary>
public static class Kurulum {
	private static readonly (string OyunIci, string Dosya)[] Fontlar = [
		("art/2dart/fonts/fontin-regular.ttf", "fontin-regular.ttf"),
		("art/2dart/fonts/fontin-bold.ttf", "fontin-bold.ttf"),
		("art/2dart/fonts/fontin-italic.ttf", "fontin-italic.ttf"),
		("art/2dart/fonts/fontin-smallcaps.ttf", "fontin-smallcaps.ttf"),
		("art/2dart/fonts/optimusprincepssemibold.ttf", "optimusprincepssemibold.ttf"),
	];

	public const string IndexAltYol = @"Bundles2\_.index.bin";

	/// <summary>
	/// Kurulum ve güncelleme AYNI akış. Oyun yaması dosyaları sıfırladığı için
	/// "güncelleme" de baştan kurulumdan ibaret; ayrı bir kod yolu yok.
	/// İkisi arasındaki fark yalnızca arayüzdeki isimlendirme.
	/// </summary>
	public static int Calistir(string[] args, IKurulumArayuzu ui) {
		try {
			var sonuc = Akis(args, ui);
			ui.Bitti(sonuc == 0);
			return sonuc;
		} catch (Exception e) {
			ui.Yaz("\nHATA: " + e.Message, KurulumRenk.Hata);
			ui.Yaz("Sorun sürerse OKUBENI.txt'deki 'Sorun giderme' bölümüne bak.", KurulumRenk.Solgun);
			ui.Bitti(false);
			return 1;
		}
	}

	private static int Akis(string[] args, IKurulumArayuzu ui) {
		var veri = VeriDizini();
		var bellek = Path.Combine(veri, "bellek.jsonl");
		if (!File.Exists(bellek)) {
			ui.Yaz($"Çeviri belleği bulunamadı: {bellek}", KurulumRenk.Hata);
			ui.Yaz("Paketi eksiksiz açtığından emin ol — 'veri' klasörü exe'nin yanında olmalı.");
			return 1;
		}

		var oyun = OyunuBul(args.FirstOrDefault(), ui);
		if (oyun is null) return 1;
		var index = Path.Combine(oyun, IndexAltYol);
		ui.Yaz($"Oyun: {oyun}", KurulumRenk.Basarili);

		if (!OodleSagla(oyun, ui)) return 1;

		if (OyunAcikMi()) {
			ui.Yaz("\nPath of Exile 2 çalışıyor. Önce oyunu kapat, sonra tekrar dene.", KurulumRenk.Hata);
			return 1;
		}

		var is_ = IsDizini();
		var kaynak = Path.Combine(is_, "kaynak.jsonl");
		var csdKaynak = Path.Combine(is_, "csd-kaynak.jsonl");

		if (Adim(ui, "Metin tabloları taranıyor", "dat-export", index, kaynak) != 0) return 1;
		if (Adim(ui, "Stat açıklamaları taranıyor", "csd-export", index, csdKaynak) != 0) return 1;

		if (!YeniIcerik(kaynak, csdKaynak, bellek, is_, ui)) return 1;

		if (Adim(ui, "Stat açıklamaları yazılıyor", "csd-import", index, csdKaynak, bellek) != 0) return 1;
		if (Adim(ui, "Metin tabloları yazılıyor", "dat-import", index, kaynak, bellek) != 0) return 1;

		var fontDizin = FontDizini(veri);
		var fontSayi = 0;
		ui.Adim("Fontlara Türkçe harfler ekleniyor");
		foreach (var (oyunIci, dosya) in Fontlar) {
			var yerel = Path.Combine(fontDizin, dosya);
			if (!File.Exists(yerel)) continue;
			if (Sessiz("write", index, oyunIci, yerel) == 0) ++fontSayi;
		}
		if (fontSayi == 0) {
			// Sessizce "0/5" yazip gecmek kabul edilemez: bu durumda oyunda
			// Turkce harfler yedek fonttan gelir ve gorunum bozulur.
			ui.Yaz($"   HİÇBİR FONT YAZILAMADI — font klasörü bulunamadı:", KurulumRenk.Hata);
			ui.Yaz($"   {fontDizin}", KurulumRenk.Hata);
			ui.Yaz("   Paket eksik açılmış olabilir. Türkçe harfler (ş, ğ, İ) tuhaf görünecek.", KurulumRenk.Uyari);
		} else {
			ui.Yaz($"   {fontSayi}/{Fontlar.Length} font yazıldı.",
				fontSayi == Fontlar.Length ? KurulumRenk.Basarili : KurulumRenk.Uyari);
		}

		Adim(ui, "Bütünlük doğrulanıyor", "csd-check", index);
		DilAyari(ui);
		SonKurulumuYaz(oyun);

		ui.Yaz("", KurulumRenk.Normal);
		ui.Yaz("BİTTİ — oyunu başlatabilirsin.", KurulumRenk.Basarili);
		ui.Yaz("Türkçe görünmüyorsa: Options → UI → Language → Português (Brasil), sonra oyunu yeniden başlat.",
			KurulumRenk.Solgun);
		return 0;
	}

	// ------------------------------------------------------- son kurulum izi

	private static string IzDosyasi => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"poe2-turkce", "son-kurulum.txt");

	/// <summary>
	/// Daha once basariyla kurulmus mu? Arayuz "Kur" mu "Guncelle" mi
	/// gosterecegine buna bakarak karar veriyor.
	///
	/// Oyunun o anki halini SORGULAMIYORUZ bilerek: oyun yamasi ceviriyi
	/// silmis olabilir ve o durumda da dogru cevap "Guncelle". Iz dosyasi
	/// "bu makinede daha once kurdum" demek, "su an kurulu" demek degil.
	/// </summary>
	public static bool DahaOnceKuruldu() => File.Exists(IzDosyasi);

	/// <summary>Son kurulumda kullanilan oyun klasoru — arama yapmadan hatirlamak icin.</summary>
	public static string? SonOyunYolu() {
		try {
			if (!File.Exists(IzDosyasi)) return null;
			var yol = File.ReadAllLines(IzDosyasi).FirstOrDefault(s => s.StartsWith("oyun=", StringComparison.Ordinal));
			return yol is null ? null : yol[5..].Trim();
		} catch { return null; }
	}

	private static void SonKurulumuYaz(string oyun) {
		try {
			Directory.CreateDirectory(Path.GetDirectoryName(IzDosyasi)!);
			File.WriteAllLines(IzDosyasi, [
				"# PoE2 Turkce yama - son basarili kurulum",
				"tarih=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
				"oyun=" + oyun,
			]);
		} catch { /* iz yazilamadi, akisi bozmaz */ }
	}

	// ---------------------------------------------------------------- oyun

	public static string? OyunuBul(string? verilen, IKurulumArayuzu ui) {
		if (!string.IsNullOrWhiteSpace(verilen)) {
			var v = verilen.Trim('"');
			if (Gecerli(v)) return v;
			ui.Yaz($"Verilen yolda oyun yok: {v}", KurulumRenk.Hata);
		}

		// Once gecen seferki klasor — diski taramaya gerek kalmadan
		var hatirlanan = SonOyunYolu();
		if (Gecerli(hatirlanan)) return hatirlanan;

		foreach (var aday in Adaylar())
			if (Gecerli(aday)) return aday;

		ui.Yaz("Oyun otomatik bulunamadı.", KurulumRenk.Uyari);
		for (var deneme = 0; deneme < 3; ++deneme) {
			var girdi = ui.YolIste("Path of Exile 2 klasörünü seç (içinde 'Bundles2' klasörü olmalı):", klasor: true);
			if (girdi is null) return null;
			if (Gecerli(girdi)) return girdi;
			ui.Yaz($"Burada {IndexAltYol} yok.", KurulumRenk.Hata);
		}
		return null;
	}

	public static bool Gecerli(string? klasor)
		=> !string.IsNullOrWhiteSpace(klasor) && File.Exists(Path.Combine(klasor, IndexAltYol));

	private static IEnumerable<string> Adaylar() {
		const string alt = @"steamapps\common\Path of Exile 2";
		foreach (var kok in SteamKutuphaneleri())
			yield return Path.Combine(kok, alt);

		foreach (var s in DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name)) {
			yield return Path.Combine(s, "SteamLibrary", alt);
			yield return Path.Combine(s, "Program Files (x86)", "Steam", alt);
			yield return Path.Combine(s, "Games", "Path of Exile 2");
			yield return Path.Combine(s, "Path of Exile 2");
		}
	}

	private static IEnumerable<string> SteamKutuphaneleri() {
		string? steam = null;
		if (OperatingSystem.IsWindows()) {
			try {
				steam = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
			} catch { /* kayit defteri okunamadi, sorun degil */ }
		}
		if (string.IsNullOrEmpty(steam)) yield break;
		steam = steam.Replace('/', '\\');
		yield return steam;

		var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
		if (!File.Exists(vdf)) yield break;
		foreach (var satir in File.ReadLines(vdf)) {
			if (satir.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase) < 0) continue;
			var yol = satir.Split('"', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
			if (!string.IsNullOrWhiteSpace(yol)) yield return yol.Replace(@"\\", @"\");
		}
	}

	public static bool OyunAcikMi() {
		try {
			return Process.GetProcesses().Any(p =>
				p.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase));
		} catch { return false; }
	}

	// --------------------------------------------------------------- oodle

	public static bool OodleHazirMi() => File.Exists(Path.Combine(AppContext.BaseDirectory, "oo2core.dll"));

	/// <summary>
	/// Oodle sıkıştırma kütüphanesi. PAKETLE GELMEZ ve gelemez: RAD/Epic'in
	/// tescilli yazılımı, dağıtım hakkımız yok. Kullanıcının kendi makinesinde
	/// arıyoruz — Oodle kullanan pek çok oyun bu dosyayı kurulumuyla getiriyor.
	/// </summary>
	private static bool OodleSagla(string oyun, IKurulumArayuzu ui) {
		var hedef = Path.Combine(AppContext.BaseDirectory, "oo2core.dll");
		if (File.Exists(hedef)) return true;

		ui.Adim("Oodle kütüphanesi aranıyor");
		var bulunan = OodleAra(oyun);
		if (bulunan is not null) {
			File.Copy(bulunan, hedef, overwrite: true);
			ui.Yaz($"   Bulundu: {bulunan}", KurulumRenk.Basarili);
			ui.Yaz("   (bir kez kopyalandı, bir daha aranmayacak)", KurulumRenk.Solgun);
			return true;
		}

		ui.Yaz("""
			Oodle kütüphanesi bulunamadı.

			Bu dosya oyunun paketlerini açmak için gerekli. Tescilli bir kütüphane
			olduğu için yamayla dağıtılamıyor — kendi makinendeki bir oyundan
			alman gerekiyor. Oodle kullanan yaygın oyunlar: EA Sports FC, Fortnite,
			Warframe, Baldur's Gate 3, Control ve pek çok Unreal Engine oyunu.
			Dosya adı "oo2core_9_win64.dll" gibi görünür.
			""", KurulumRenk.Uyari);

		var girdi = ui.YolIste("oo2core dosyasını seç:", klasor: false);
		if (girdi is not null && File.Exists(girdi)) {
			File.Copy(girdi, hedef, overwrite: true);
			ui.Yaz("Kopyalandı.", KurulumRenk.Basarili);
			return true;
		}
		ui.Yaz("oo2core olmadan devam edilemez.", KurulumRenk.Hata);
		return false;
	}

	private static string? OodleAra(string oyun) {
		var kokler = new List<string> { oyun };
		kokler.AddRange(SteamKutuphaneleri().Select(k => Path.Combine(k, "steamapps", "common")));
		foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed)) {
			kokler.Add(Path.Combine(d.Name, "Program Files"));
			kokler.Add(Path.Combine(d.Name, "Program Files (x86)"));
			kokler.Add(Path.Combine(d.Name, "Epic Games"));
			kokler.Add(Path.Combine(d.Name, "GOG Games"));
			kokler.Add(Path.Combine(d.Name, "SteamLibrary", "steamapps", "common"));
		}

		var sure = Stopwatch.StartNew();
		foreach (var kok in kokler.Distinct(StringComparer.OrdinalIgnoreCase)) {
			if (!Directory.Exists(kok)) continue;
			var bulundu = DerinlikliAra(kok, 4, sure);
			if (bulundu is not null) return bulundu;
			if (sure.Elapsed > TimeSpan.FromSeconds(45)) break;
		}
		return null;
	}

	/// <summary>Sinirli derinlikte ve sureli arama — tum diski taramak kabul edilemez.</summary>
	private static string? DerinlikliAra(string dizin, int derinlik, Stopwatch sure) {
		if (derinlik < 0 || sure.Elapsed > TimeSpan.FromSeconds(45)) return null;
		try {
			foreach (var f in Directory.EnumerateFiles(dizin, "oo2core*win64.dll"))
				return f;
			foreach (var d in Directory.EnumerateDirectories(dizin)) {
				var r = DerinlikliAra(d, derinlik - 1, sure);
				if (r is not null) return r;
			}
		} catch { /* erisim yok, atla */ }
		return null;
	}

	// ------------------------------------------------------------ yeni icerik

	private enum MotorHali { Yok, ModelYok, Hazir }

	/// <summary>
	/// Bellekte karşılığı olmayan metinler. Bunlar hem oyun güncellemesiyle
	/// gelen yeni içerik olabilir hem de baştan beri çevrilemeyen artık.
	/// </summary>
	private static bool YeniIcerik(string kaynak, string csdKaynak, string bellek, string is_, IKurulumArayuzu ui) {
		var bilinen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var satir in File.ReadLines(bellek)) {
			if (string.IsNullOrWhiteSpace(satir)) continue;
			try {
				var e = JsonSerializer.Deserialize<MemoryEntry>(satir);
				if (e is { En.Length: > 0 }) bilinen.Add(e.En);
			} catch { /* bozuk satir */ }
		}

		var eksik = new List<string>();
		var gorulen = new HashSet<string>(StringComparer.Ordinal);
		foreach (var dosya in new[] { kaynak, csdKaynak }) {
			foreach (var satir in File.ReadLines(dosya)) {
				if (string.IsNullOrWhiteSpace(satir)) continue;
				TranslationUnit? u;
				try { u = JsonSerializer.Deserialize<TranslationUnit>(satir); } catch { continue; }
				if (u?.En is null || u.En.Length == 0) continue;
				if (bilinen.Contains(u.En) || !gorulen.Add(u.En)) continue;
				eksik.Add(u.En);
			}
		}

		if (eksik.Count == 0) {
			ui.Yaz("Çevrilmemiş metin yok — bellek bu sürümü tamamen karşılıyor.", KurulumRenk.Basarili);
			return true;
		}

		ui.Yaz($"Bellekte karşılığı olmayan metin: {eksik.Count:N0}", KurulumRenk.Uyari);

		var motor = MotorDurumu();
		if (motor != MotorHali.Hazir) {
			ui.Yaz(motor == MotorHali.Yok
				? "Yerel çeviri motoru (Ollama) çalışmıyor; bu metinler İngilizce kalacak.\n"
				  + "Çevirmek istersen ollama.com'dan Ollama kur, sonra: ollama pull gemma3:12b"
				: "Ollama çalışıyor ama gemma3:12b modeli inmemiş; bu metinler İngilizce kalacak.\n"
				  + "İndirmek için: ollama pull gemma3:12b  (~8 GB)",
				KurulumRenk.Solgun);
			return true; // yama yine de kurulur
		}

		if (!ui.Sor($"{eksik.Count:N0} metin şimdi çevrilsin mi?\n\n"
				+ "Yaklaşık 15 metin/dakika. Durdurmak güvenli, kaldığı yerden devam eder.\n"
				+ "Hayır dersen bu metinler İngilizce kalır, yama yine kurulur."))
			return true;

		// Yalnizca eksikleri iceren korpus — bkz. NOTES "kosu sirasi tuzagi"
		var eksikKorpus = Path.Combine(is_, "yeni-icerik.jsonl");
		using (var w = new StreamWriter(eksikKorpus, false, new UTF8Encoding(false))) {
			var opt = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
			foreach (var en in eksik)
				w.WriteLine(JsonSerializer.Serialize(new TranslationUnit { En = en }, opt));
		}

		// Parti 1: benzer metinlerin birbirine ad bulastirmasini onler (bkz. NOTES)
		Adim(ui, "Yeni içerik çevriliyor", "translate", eksikKorpus, bellek, "gemma3:12b", "1");
		Adim(ui, "Çeviriler kalıba oturtuluyor", "stat-duzelt", bellek);
		return true;
	}

	/// <summary>
	/// Ollama'nin YANIT VERMESI yetmez, modelin de inmis olmasi gerekir.
	/// Yalnizca ucun ayakta olmasina bakan bir kontrol, modeli cekmemis
	/// kullaniciya "cevrilsin mi?" diye sorup her istekte hata veriyordu.
	/// </summary>
	private static MotorHali MotorDurumu() {
		try {
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
			var cevap = http.GetAsync("http://localhost:11434/api/tags").GetAwaiter().GetResult();
			if (!cevap.IsSuccessStatusCode) return MotorHali.Yok;
			var govde = cevap.Content.ReadAsStringAsync().GetAwaiter().GetResult();
			return govde.Contains("gemma3:12b", StringComparison.OrdinalIgnoreCase)
				? MotorHali.Hazir : MotorHali.ModelYok;
		} catch { return MotorHali.Yok; }
	}

	// --------------------------------------------------------------- yardimci

	private static void DilAyari(IKurulumArayuzu ui) {
		var ini = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
			"My Games", "Path of Exile 2", "poe2_production_Config.ini");

		// Oyun bir kez bile calistirilmadiysa ayar dosyasi henuz yok. Sessizce
		// gecmek kotu: kullanici oyunu acar, Turkce goremez ve sebebini bilmez.
		if (!File.Exists(ini)) {
			ui.Yaz("Oyun ayar dosyası henüz yok (oyun hiç açılmamış).", KurulumRenk.Uyari);
			ui.Yaz("Dili ELLE seçmen gerekecek:  Options → UI → Language → Português (Brasil)", KurulumRenk.Uyari);
			return;
		}

		var satirlar = File.ReadAllLines(ini).ToList();
		var dilBolumu = false;
		for (var i = 0; i < satirlar.Count; ++i) {
			var s = satirlar[i].Trim();
			if (s.StartsWith('[')) dilBolumu = s.Equals("[LANGUAGE]", StringComparison.OrdinalIgnoreCase);
			if (!dilBolumu || !s.StartsWith("language=", StringComparison.OrdinalIgnoreCase)) continue;
			if (s.Equals("language=pt-BR", StringComparison.OrdinalIgnoreCase)) return; // zaten dogru
			if (!ui.Sor("Oyun dili otomatik olarak ayarlansın mı?\n\n"
					+ "Yama Portekizce dil yuvasını kullanıyor; bu ayar yapılmazsa\n"
					+ "oyunu açtığında Türkçe göremezsin."))
				return;
			satirlar[i] = "language=pt-BR";
			File.WriteAllLines(ini, satirlar);
			ui.Yaz("Dil ayarı yapıldı.", KurulumRenk.Basarili);
			return;
		}
	}

	private static int Adim(IKurulumArayuzu ui, string baslik, params string[] args) {
		ui.Adim(baslik);
		var kod = CalistirAlt(args, satir => ui.Yaz("   " + satir, KurulumRenk.Solgun));
		if (kod != 0) ui.Yaz($"   (adım {kod} koduyla bitti)", KurulumRenk.Uyari);
		return kod;
	}

	private static int Sessiz(params string[] args) => CalistirAlt(args, null);

	/// <summary>
	/// Alt komutu kendi ikilimizi tekrar calistirarak isletir.
	///
	/// Cikti YAKALANIYOR: pencere surumunde konsol yok, alt surecin yazdiklari
	/// hicbir yere gitmezdi. Yakalanan satirlar arayuze aktariliyor.
	/// </summary>
	private static int CalistirAlt(string[] args, Action<string>? satirAlindi) {
		var psi = new ProcessStartInfo(Environment.ProcessPath!) {
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
		};

		// `dotnet poe2tr.dll kur ...` ile calisildiysa ProcessPath dotnet.exe'yi
		// gosterir; alt komutu oyle cagirmak "dotnet-dat-export bulunamadi"
		// hatasi verir. O durumda kendi dll'imizi ilk arguman olarak veriyoruz.
		if (Path.GetFileNameWithoutExtension(psi.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) {
			var kendiDll = System.Reflection.Assembly.GetEntryAssembly()?.Location;
			if (!string.IsNullOrEmpty(kendiDll)) psi.ArgumentList.Add(kendiDll);
		}

		foreach (var a in args) psi.ArgumentList.Add(a);

		using var p = Process.Start(psi)!;
		p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) satirAlindi?.Invoke(e.Data); };
		p.ErrorDataReceived += (_, e) => {
			// "5 dosyanin yolu cozulemedi" bilinen ve zararsiz; kullaniciya gosterme
			if (string.IsNullOrWhiteSpace(e.Data)) return;
			if (e.Data.Contains("yolu cozulemedi", StringComparison.OrdinalIgnoreCase)) return;
			satirAlindi?.Invoke(e.Data);
		};
		p.BeginOutputReadLine();
		p.BeginErrorReadLine();
		p.WaitForExit();
		return p.ExitCode;
	}

	/// <summary>
	/// Yamali fontlarin klasoru. Pakette veri/fontlar; gelistirme ortaminda
	/// work/font-yamali. Ikincisi olmadan dev testleri font adimini hic
	/// calistirmiyor ve sorunu ancak paketleyince goruyorduk.
	/// </summary>
	private static string FontDizini(string veri) {
		var f = Path.Combine(veri, "fontlar");
		if (Directory.Exists(f)) return f;
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null) {
			var p = Path.Combine(dir.FullName, "work", "font-yamali");
			if (Directory.Exists(p)) return p;
			dir = dir.Parent;
		}
		return f;
	}

	public static string VeriDizini() {
		var v = Path.Combine(AppContext.BaseDirectory, "veri");
		if (Directory.Exists(v)) return v;
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null) {
			var p = Path.Combine(dir.FullName, "work", "corpus");
			if (File.Exists(Path.Combine(p, "bellek.jsonl"))) return p;
			dir = dir.Parent;
		}
		return v;
	}

	private static string IsDizini() {
		var d = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"poe2-turkce", "is");
		Directory.CreateDirectory(d);
		return d;
	}
}
