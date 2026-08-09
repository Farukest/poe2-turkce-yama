using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Poe2Tr;

/// <summary>Bellekteki tek bir ceviri: kaynak -> hedef.</summary>
public sealed class MemoryEntry {
	[JsonPropertyName("en")] public string En { get; set; } = "";
	[JsonPropertyName("tr")] public string Tr { get; set; } = "";
}

/// <summary>
/// Ceviri bellegi. Append-only JSONL; her parti sonunda diske yazilir.
/// Program kapansa, bilgisayar kapansa bile yazilanlar kalir ve
/// bir sonraki calistirmada kaldigi yerden devam eder.
/// </summary>
public sealed class TranslationMemory : IDisposable {
	private readonly Dictionary<string, string> map = new(StringComparer.Ordinal);
	private readonly StreamWriter writer;
	private static readonly JsonSerializerOptions JsonOpts = new() {
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public int Count => map.Count;

	public TranslationMemory(string path) {
		if (File.Exists(path)) {
			var bad = 0;
			foreach (var line in File.ReadLines(path)) {
				if (string.IsNullOrWhiteSpace(line)) continue;
				try {
					var e = JsonSerializer.Deserialize<MemoryEntry>(line);
					if (e is { En.Length: > 0 }) map[e.En] = e.Tr;
				} catch { ++bad; }
			}
			if (bad != 0)
				Console.Error.WriteLine($"Uyari: bellekte {bad} bozuk satir atlandi (muhtemelen yarim yazilmis son satir)");
		} else {
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		}
		// FileShare.ReadWrite - ama dikkat: kosu surerken ilerlemeyi okumak icin
		// asil belirleyici OKUYAN taraf. File.ReadAllLines kendini FileShare.Read
		// ile acar ("baskalari yalnizca okuyabilir") ve acik bir yazici varken
		// her hâlükârda basarisiz olur. Okuyucu da FileShare.ReadWrite istemeli:
		//   [IO.File]::Open($yol, 'Open', 'Read', 'ReadWrite')
		var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
		writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
	}

	public bool TryGet(string en, out string tr) => map.TryGetValue(en, out tr!);

	public void Add(string en, string tr) {
		map[en] = tr;
		writer.WriteLine(JsonSerializer.Serialize(new MemoryEntry { En = en, Tr = tr }, JsonOpts));
	}

	/// <summary>Diske yaz. Her parti sonunda cagriliyor.</summary>
	public void Flush() => writer.Flush();

	public void Dispose() { writer.Flush(); writer.Dispose(); }
}

/// <summary>Ollama'nin /api/chat ucuna baglanan asgari istemci.</summary>
public sealed class OllamaClient(string model, Uri? baseUri = null) : IDisposable {
	private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(20) };
	private readonly Uri uri = new(baseUri ?? new Uri("http://localhost:11434"), "/api/chat");

	public async Task<string> ChatAsync(string system, string user, double temperature, CancellationToken ct) {
		var payload = new {
			model,
			stream = false,
			think = false, // dusunen modellerde akil yurutmeyi kapat
			options = new { temperature, num_ctx = 8192 },
			messages = new[] {
				new { role = "system", content = system },
				new { role = "user", content = user },
			},
		};
		using var content = new StringContent(
			JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
		using var resp = await http.PostAsync(uri, content, ct);
		resp.EnsureSuccessStatusCode();
		using var stream = await resp.Content.ReadAsStreamAsync(ct);
		using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
		var text = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
		// dusunen modellerin <think> bloklarini at
		return Regex.Replace(text, @"(?s)<think>.*?</think>", "").Trim();
	}

	public void Dispose() => http.Dispose();
}

public sealed partial class Translator {
	[GeneratedRegex(@"<(\d+)>")] private static partial Regex OpenTagRx();
	[GeneratedRegex(@"</\d+>")] private static partial Regex CloseTagRx();

	/// <summary>
	/// Modelin cevabini ayristirir. Kapanis etiketi (&lt;/1&gt;) ZORUNLU DEGIL:
	/// Gemma3 sikca yazmiyor. Bir acilis etiketinden sonraki metin, bir sonraki
	/// acilis etiketine (ya da metnin sonuna) kadar o numaranin cevirisidir.
	/// </summary>
	private static Dictionary<int, string> ParseReply(string reply, int count) {
		var result = new Dictionary<int, string>();
		var opens = OpenTagRx().Matches(reply);
		for (var i = 0; i < opens.Count; ++i) {
			var idx = int.Parse(opens[i].Groups[1].Value) - 1;
			if (idx < 0 || idx >= count) continue;
			var start = opens[i].Index + opens[i].Length;
			var end = i + 1 < opens.Count ? opens[i + 1].Index : reply.Length;
			if (end < start) continue;
			var text = reply[start..end];
			// Kapanis etiketini at. NUMARASINA BAKMADAN: model kapanisi kaydirip
			// "<7>...</8>" yazabiliyor ve eskiden yalnizca tam eslesen numara
			// kirpiliyordu. Artan "</8>" ceviride kaliyor, oyuna oyle gidiyordu -
			// 1.066 kayitta oldu. Bu konumdaki </N> her hâlükârda bizim cevap
			// bicimimizdir; oyun metninde sayidan olusan etiket yok.
			text = CloseTagRx().Replace(text, "");
			result[idx] = text.Trim('\r', '\n');
		}
		return result;
	}

	/// <summary>POE2TR_DEBUG ortam degiskeni ayarliysa ham istek/cevap yazilir.</summary>
	public static bool Debug { get; } = Environment.GetEnvironmentVariable("POE2TR_DEBUG") is not null;

	private readonly string system;
	private readonly OllamaClient client;
	/// <summary>Sozluk: Ingilizce terim -> "Ingilizce = Turkce" satiri.</summary>
	private readonly (string En, string Line)[] glossary;

	public Translator(string model, string glossaryPath) {
		client = new OllamaClient(model);
		glossary = LoadGlossary(glossaryPath);
		system = BuildSystemPrompt();
	}

	private static (string En, string Line)[] LoadGlossary(string path) {
		var list = new List<(string, string)>();
		foreach (var raw in File.ReadLines(path)) {
			var line = raw.Trim();
			if (line.Length == 0 || line.StartsWith('#')) continue;
			var p = line.Split('\t');
			if (p.Length < 2) continue;
			list.Add((p[0], $"{p[0]} = {p[1]}"));
		}
		return [.. list];
	}

	/// <summary>
	/// Partide gecen terimleri secer.
	///
	/// Sozlugun tamami ~4.000 token ve her istekte yeniden isleniyordu; basarisiz
	/// bir metin 3 istek demek oldugu icin uretim degil ISTEM ISLEME darbogaz
	/// oluyordu. Yalnizca o partide gercekten gecen terimleri gondermek istemi
	/// birkac yuz tokene indiriyor.
	/// </summary>
	private string RelevantGlossary(IReadOnlyList<string> sources) {
		var blob = string.Join('\n', sources);
		var hits = glossary
			.Where(g => blob.Contains(g.En, StringComparison.OrdinalIgnoreCase))
			.Select(g => g.Line);
		return string.Join('\n', hits);
	}

	private static string BuildSystemPrompt() {
		return $"""
			Sen Path of Exile 2 adlı oyunun metinlerini İngilizceden Türkçeye çeviren bir çevirmensin.

			TEKNİK KODLARI AYNEN KORU — bunlar oyunun kodudur, çevrilmez:
			- {0}, {1} gibi süslü parantez içindeki SAYILAR yer tutucudur. Aynen kopyala,
			  silme, sayısını değiştirme. Cümlenin akışına göre yerini kaydırabilirsin.
			- <i>, <continue>, <white>, <smaller>, <rgb(1,2,3)> gibi açılı etiketler aynen kalır.
			- [Köşeli parantez] içindeki metni ÇEVİR ama parantezleri koru:
			  [Chills] -> [Üşütür]. Parantez sayısı değişmemeli.

			  EN SIK YAPTIĞIN İKİNCİ HATA — köşeli parantezli terimi yutuyorsun.
			  İngilizcede parantezli terim fiil olabiliyor; Türkçede fiil sona
			  gidince onu parantezden çıkarıyorsun. ÇIKARMA, parantezi taşı:
			    [Consume] [Freeze] on Hit   ->  Vuruşta [Freeze|Dondurma] [Consume|tüketir]
			    YANLIŞ: "Vuruşta [Dondurma] tüketir"   (bir parantez kayboldu)
			  Kaynakta kaç tane [...] varsa çeviride de TAM O KADAR olmalı.
			  Türkçe ekleri parantezin dışına yaz: [Mermiler]e, [Zırh]ın.
			- Satır sonlarını olduğu yerde koru.

			STAT SATIRI KALIBI — bunlar cümle değil, KALIPTIR. Oyuncu eşya panelinde
			onları okumaz, tarar; aynı bilgi her satırda aynı yerde durmalı. Sıfat
			Türkçede nitelediği ismin ÖNÜNE gelir, sona atılmaz:

			  {0}% increased Armour                    -> %{0} artırılmış Zırh
			  {0}% reduced Armour                      -> %{0} azaltılmış Zırh
			  {0}% increased Attack Speed              -> %{0} artırılmış Saldırı Hızı
			  {0}% increased Damage per [Power Charge] -> [Power Charge] başına %{0} artırılmış Hasar
			  {0:+d} to Dexterity                      -> {0:+d} Çeviklik
			  {0:+d} to maximum Life                   -> {0:+d} Azami Can
			  {0:+d}% to Fire Resistance               -> {0:+d}% Ateş Direnci

			  YANLIŞ: "Zırh %{0} artırılmış"        (sıfat sona atılmış)
			  YANLIŞ: "Azami Can'a {0:+d} eklenmiş" (kalıp bozulmuş)
			  YANLIŞ: "{0:+d} ila Çeviklik"         ("ila" ARALIK demektir)

			EN SIK YAPTIĞIN HATA — yüzdeyi öne alırken süslü parantezi yutuyorsun:
			  {0}% increased Armour  ->  %0 artırılmış Zırh     ❌ YER TUTUCU YOK OLDU
			  {0}% increased Armour  ->  %{0} artırılmış Zırh   ✅ DOĞRU
			{0} bölünmez bir bütündür. Yüzde işareti süslü parantezin SOLUNA yazılır,
			parantezler olduğu gibi durur. "%0", "%1" diye bir şey YOK.

			"ila" YALNIZCA iki sayı arasında kullanılır:
			  {0} to {1} Fire Damage  -> {0} ila {1} Ateş Hasarı   (DOĞRU, aralık)
			  {0:+d} to Dexterity     -> {0:+d} Çeviklik           ("ila" YOK)

			DİL KURALLARI:
			- Yüzde işareti Türkçede sayıdan ÖNCE gelir: "{0}% increased" -> "%{0} artırılmış"
			- "increased/reduced" ile "more/less" oyunda FARKLI çarpanlardır, farklı çevrilmeli.
			- Oyuncuya doğrudan hitap et: "Kazanırsın" de, "Kazanırsınız" deme.
			- Anlatı ve diyalog metinlerinde doğal, akıcı Türkçe kullan; birebir çeviri yapma.
			- Özel adları (kişi, yer, eşya, yetenek adları) İngilizce bırak.

			DİKKAT — sık karıştırılan silahlar. Bunlar oyunda AYRI silah türleridir,
			karıştırırsan oyuncu yanlış eşya arar:
			  Mace = Topuz          (Spear/Mızrak DEĞİL, Hammer/Çekiç DEĞİL)
			  Spear = Mızrak
			  Wand = Değnek         (Staff/Asa DEĞİL)
			  Staff = Asa
			  Quarterstaff = Uzun Asa
			  Sceptre = Hükümdar Asası
			  Crossbow = Tatar Yayı (Bow/Yay DEĞİL)
			  Flail = Gürz
			Sözlük büyük/küçük harf ayırmaz: "mace" de "Mace" de Topuz'dur.

			Her istekte, o metinlerde geçen terimlerin ZORUNLU karşılıkları verilecek.
			Sözlükte İngilizcesiyle Türkçesi AYNI yazılmış terimler eşya/yetenek/para birimi ADIDIR;
			onları asla çevirme, olduğu gibi bırak.

			ÇIKTI BİÇİMİ:
			Sana <n>metin</n> biçiminde numaralı satırlar verilecek. Tam olarak aynı numaralarla,
			aynı biçimde SADECE çeviriyi döndür. Açıklama yazma, yorum ekleme, başka hiçbir şey yazma.
			""";
	}

	/// <summary>
	/// Bir partiyi cevirir. Donen sozlukte yalnizca dogrulamayi gecen ceviriler olur.
	/// </summary>
	public async Task<Dictionary<string, string>> TranslateBatchAsync(
			IReadOnlyList<string> sources, double temperature, CancellationToken ct) {
		var masks = sources.Select(_ => new Masking()).ToArray();
		var sb = new StringBuilder();
		for (var i = 0; i < sources.Count; ++i)
			sb.AppendLine($"<{i + 1}>{masks[i].Prepare(sources[i])}</{i + 1}>");

		var terms = RelevantGlossary(sources);
		var request = terms.Length == 0
			? sb.ToString().TrimEnd()
			: $"ZORUNLU TERİMLER:\n{terms}\n\nÇEVİRİLECEK METİNLER:\n{sb.ToString().TrimEnd()}";
		var reply = await client.ChatAsync(system, request, temperature, ct);

		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		var blocks = ParseReply(reply, sources.Count);
		foreach (var (idx, raw) in blocks) {
			var candidate = masks[idx].Repair(raw);
			var problem = masks[idx].Validate(candidate);
			if (problem is not null) {
				if (Debug) Console.Error.WriteLine($"    [{idx + 1}] dogrulama: {problem}");
				continue; // bozuk, yeniden denenecek
			}
			result[sources[idx]] = masks[idx].Restore(candidate);
		}

		if (Debug && result.Count == 0) {
			Console.Error.WriteLine("--- ISTEK ---");
			Console.Error.WriteLine(request);
			Console.Error.WriteLine($"--- CEVAP ({blocks.Count} blok ayristirildi) ---");
			Console.Error.WriteLine(reply);
			Console.Error.WriteLine("--- SON ---");
		}
		return result;
	}

	/// <summary>Tum kaynaklari, bellekte olmayanlari cevirerek isler.</summary>
	public async Task RunAsync(
			IReadOnlyList<string> allSources, TranslationMemory memory,
			int batchSize, int maxAttempts, CancellationToken ct) {
		var pending = allSources.Where(s => !memory.TryGet(s, out _)).ToList();
		Console.WriteLine($"Toplam benzersiz metin : {allSources.Count:N0}");
		Console.WriteLine($"Bellekte hazir         : {allSources.Count - pending.Count:N0}");
		Console.WriteLine($"Cevrilecek             : {pending.Count:N0}");
		Console.WriteLine();
		if (pending.Count == 0) return;

		var sw = Stopwatch.StartNew();
		int done = 0, failed = 0;

		for (var i = 0; i < pending.Count && !ct.IsCancellationRequested; i += batchSize) {
			var batch = pending.Skip(i).Take(batchSize).ToList();
			var remaining = new List<string>(batch);

			for (var attempt = 1; attempt <= maxAttempts && remaining.Count > 0 && !ct.IsCancellationRequested; ++attempt) {
				// Ilk deneme tum partiyi birlikte gonderir. Sonraki denemelerde
				// kalanlar TEK TEK gonderilir: modelin uzun partide isaretci
				// dusurme egilimi boylece buyuk olcude ortadan kalkiyor.
				// Sicakligi da biraz artiriyoruz ki ayni hatayi tekrarlamasin.
				var groups = attempt == 1
					? [remaining]
					: remaining.Select(s => (IReadOnlyList<string>)new[] { s }).ToList();
				var temperature = attempt == 1 ? 0.2 : 0.45;

				var succeeded = new HashSet<string>(StringComparer.Ordinal);
				foreach (var group in groups) {
					if (ct.IsCancellationRequested) break;
					Dictionary<string, string> got;
					try {
						got = await TranslateBatchAsync(group, temperature, ct);
					} catch (OperationCanceledException) {
						break;
					} catch (Exception e) {
						Console.Error.WriteLine($"  istek hatasi (deneme {attempt}): {e.Message}");
						await Task.Delay(2000, CancellationToken.None);
						continue;
					}
					foreach (var (en, tr) in got) {
						memory.Add(en, tr);
						succeeded.Add(en);
						++done;
					}
				}
				remaining = remaining.Where(s => !succeeded.Contains(s)).ToList();
			}

			failed += remaining.Count;
			memory.Flush(); // her parti sonunda diske yaz - kapatma guvenli

			var processed = i + batch.Count;
			var rate = processed / Math.Max(sw.Elapsed.TotalSeconds, 1);
			var eta = TimeSpan.FromSeconds((pending.Count - processed) / Math.Max(rate, 0.001));
			Console.WriteLine(
				$"  {processed,7:N0}/{pending.Count:N0}  " +
				$"basarili {done,7:N0}  atlanan {failed,5:N0}  " +
				$"{rate * 60:F0}/dk  kalan ~{eta:d\\g\\ hh\\:mm}");
		}

		memory.Flush();
		Console.WriteLine();
		if (ct.IsCancellationRequested)
			Console.WriteLine("Durduruldu. Yazilanlar bellekte; ayni komutu tekrar calistirinca kaldigi yerden devam eder.");
		Console.WriteLine($"Bu oturumda cevrilen: {done:N0}   dogrulamayi gecemeyen: {failed:N0}");
	}
}
