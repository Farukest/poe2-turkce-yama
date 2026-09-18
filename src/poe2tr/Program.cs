using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LibBundle3;
using LibBundle3.Records;
using Poe2Tr;
using Index = LibBundle3.Index;

// Konsol yoksa (pencere olarak baslatildiysa) bu cagri patlar; onemli degil.
try { Console.OutputEncoding = Encoding.UTF8; } catch { /* konsol yok */ }

// Argumansiz calistirildiysa kullanici exe'ye cift tiklamistir -> PENCERE.
// Kurulum ve guncelleme AYNI islem oldugu icin tek program var; pencere
// hangi ismi gosterecegine daha once kurulup kurulmadigina bakarak karar veriyor.
if (args.Length == 0) {
	// ARAYUZ ACIK BIR STA IS PARCACIGINDA CALISMALI.
	//
	// WinForms'un klasor ve dosya secicileri COM tabanli (IFileDialog) ve
	// STA apartman sart kosuyor. Ust duzey deyimlerle uretilen giris
	// noktasina [STAThread] konulamiyor; varsayilan MTA'da pencere acilir,
	// duzgun gorunur, ama "Degistir..." dugmesine basildiginda secici
	// donmez ve program kilitlenir. Testte birebir bu yasandi.
	var arayuz = new Thread(() => {
		ApplicationConfiguration.Initialize();
		Application.Run(new Pencere());
	});
	arayuz.SetApartmentState(ApartmentState.STA);
	arayuz.Start();
	arayuz.Join();
	return 0;
}

// "kur" / "guncelle" argumaniyla: konsol surumu (betikten/zamanlanmis gorevden)
if (args[0] is "kur" or "guncelle")
	return Kurulum.Calistir(args[1..], new KonsolArayuzu());

if (args.Length < 2) {
	Usage();
	return 1;
}

var cmd = args[0].ToLowerInvariant();
var indexPath = Path.GetFullPath(args[1]);
if (!File.Exists(indexPath)) {
	Console.Error.WriteLine("Index bulunamadi: " + indexPath);
	return 1;
}

// Terim tutarlilik kurallari (istege bagli dosya). Ceviri, normalize ve
// dogrulama yollarinin hepsi ayni kurallari gormeli, o yuzden burada bir kez.
{
	var kok = Exclusions.Locate();
	if (kok is not null) {
		var dizin = Path.GetDirectoryName(kok)!;
		StatText.TerimKurallariniYukle(Path.Combine(dizin, "terim-duzeltme.tsv"));
		StatText.SozlukYukle(Path.Combine(dizin, "sozluk.tsv"));
	}
}

switch (cmd) {
	case "list": {
		// list <index> [filtre] [cikti.txt]
		var filter = args.Length > 2 ? args[2] : null;
		var outFile = args.Length > 3 ? Path.GetFullPath(args[3]) : null;

		using var index = OpenIndex(indexPath);
		var paths = index.Files.Values
			.Select(f => f.Path)
			.Where(p => !string.IsNullOrEmpty(p));
		if (!string.IsNullOrEmpty(filter))
			paths = paths.Where(p => p.Contains(filter, StringComparison.OrdinalIgnoreCase));

		var sorted = paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
		if (outFile is null) {
			foreach (var p in sorted)
				Console.WriteLine(p);
		} else {
			Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
			File.WriteAllLines(outFile, sorted, new UTF8Encoding(false));
			Console.WriteLine($"{sorted.Length} dosya yazildi -> {outFile}");
		}
		Console.Error.WriteLine($"Eslesen: {sorted.Length} / Toplam: {index.Files.Count}");
		return 0;
	}

	case "extract": {
		// extract <index> <onEk> <hedefKlasor>
		if (args.Length < 4) { Usage(); return 1; }
		var prefix = args[2].Replace('\\', '/');
		var outDir = Path.GetFullPath(args[3]);

		using var index = OpenIndex(indexPath);
		var targets = index.Files.Values
			.Where(f => !string.IsNullOrEmpty(f.Path)
				&& f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			.ToArray();

		if (targets.Length == 0) {
			Console.Error.WriteLine("Bu on eke uyan dosya yok: " + prefix);
			return 1;
		}

		var written = 0;
		Index.Extract(targets, (FileRecord record, ReadOnlyMemory<byte>? content) => {
			if (content is null)
				return false;
			var dest = Path.Combine(outDir, record.Path.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
			File.WriteAllBytes(dest, content.Value.ToArray());
			++written;
			return false;
		});
		Console.WriteLine($"{written} / {targets.Length} dosya cikarildi -> {outDir}");
		return 0;
	}

	case "write": {
		// write <index> <oyunIciYol> <yerelDosya>
		if (args.Length < 4) { Usage(); return 1; }
		var gamePath = args[2].Replace('\\', '/');
		var srcFile = Path.GetFullPath(args[3]);
		if (!File.Exists(srcFile)) {
			Console.Error.WriteLine("Kaynak dosya yok: " + srcFile);
			return 1;
		}

		using var index = OpenIndex(indexPath);
		if (!index.TryGetFile(gamePath, out var file)) {
			Console.Error.WriteLine("Index icinde bulunamadi: " + gamePath);
			return 1;
		}
		var bytes = File.ReadAllBytes(srcFile);
		var oldSize = file.Size;
		file.Write(bytes, saveIndex: true);
		Console.WriteLine($"Yazildi: {gamePath}  ({oldSize} -> {bytes.Length} bayt)");
		return 0;
	}

	case "info": {
		using var index = OpenIndex(indexPath);
		Console.WriteLine($"Toplam dosya : {index.Files.Count}");
		Console.WriteLine($"Yolu bilinen : {index.Files.Values.Count(f => !string.IsNullOrEmpty(f.Path))}");
		var dirs = index.Files.Values
			.Select(f => f.Path)
			.Where(p => !string.IsNullOrEmpty(p) && p.Contains('/'))
			.Select(p => p[..p.IndexOf('/')])
			.GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
			.OrderByDescending(g => g.Count());
		Console.WriteLine();
		Console.WriteLine("Kok klasorler:");
		foreach (var g in dirs)
			Console.WriteLine($"  {g.Key,-24} {g.Count(),8}");
		return 0;
	}

	case "dat-dump": {
		// dat-dump <index> <TabloAdi> [satirSayisi]
		if (args.Length < 3) { Usage(); return 1; }
		var tableName = args[2];
		var max = args.Length > 3 ? int.Parse(args[3]) : 10;

		var schema = SchemaRoot.Load(SchemaRoot.Locate()).Poe2ByName();
		if (!schema.TryGetValue(tableName, out var st)) {
			Console.Error.WriteLine("Semada yok: " + tableName);
			return 1;
		}
		var cols = st.Columns
			.Select((c, i) => (Col: c, Index: i))
			.Where(x => x.Col.IsTranslatable)
			.ToArray();
		if (cols.Length == 0) {
			Console.Error.WriteLine($"{tableName} tablosunda cevrilebilir kolon yok");
			return 1;
		}

		using var index = OpenIndex(indexPath);
		var lower = tableName.ToLowerInvariant();
		var enBytes = ReadFromIndex(index, $"data/balance/{lower}.datc64");
		var ptBytes = ReadFromIndex(index, $"data/balance/portuguese/{lower}.datc64");
		if (enBytes is null) { Console.Error.WriteLine("Ingilizce tablo indexte yok"); return 1; }

		var en = DatFile.Load(enBytes, st.RowLength, out var enWarn);
		var pt = ptBytes is null ? null : DatFile.Load(ptBytes, st.RowLength, out _);
		Console.WriteLine($"{tableName}: {en.RowCount} satir, satir uzunlugu {en.RowLength}"
			+ (enWarn is null ? "  [sema uyumlu]" : $"  [UYARI: {enWarn}]"));
		Console.WriteLine($"Cevrilebilir kolonlar: {string.Join(", ", cols.Select(c => c.Col.Name ?? "(isimsiz)"))}");
		Console.WriteLine();

		foreach (var (col, idx) in cols) {
			var off = st.OffsetOf(idx);
			Console.WriteLine($"--- {col.Name ?? "(isimsiz)"} ---");
			for (var r = 0; r < Math.Min(max, en.RowCount); ++r) {
				var e = en.ReadString(r, off);
				if (string.IsNullOrEmpty(e))
					continue;
				var p = pt?.ReadString(r, off);
				Console.WriteLine($"  [{r}] EN: {Trim(e)}");
				if (p is not null)
					Console.WriteLine($"       PT: {Trim(p)}");
			}
			Console.WriteLine();
		}
		return 0;
	}

	case "dat-tables": {
		// dat-tables <index> [cikti.csv]  -- cevrilebilir tum tablolari tarar
		var outCsv = args.Length > 2 ? Path.GetFullPath(args[2]) : null;
		var schemaAll = SchemaRoot.Load(SchemaRoot.Locate()).Poe2ByName();
		using var index = OpenIndex(indexPath);

		// Semadaki @localized isaretleri eksik oldugu icin ona guvenmiyoruz:
		// her metin kolonunda Ingilizce ile Portekizce degerleri karsilastirip
		// gercekten cevrilmis olanlari ampirik olarak buluyoruz.
		const string ptDir = "data/balance/portuguese/";
		var ptPaths = index.Files.Values
			.Select(f => f.Path)
			.Where(p => !string.IsNullOrEmpty(p) && p.StartsWith(ptDir, StringComparison.Ordinal))
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		var report = new List<(string Table, int Rows, int Cells, long Chars, string Cols, string Status)>();
		foreach (var ptPath in ptPaths) {
			var name = Path.GetFileNameWithoutExtension(ptPath);
			if (!schemaAll.TryGetValue(name, out var st2)) {
				report.Add((name, 0, 0, 0, "", "semada yok"));
				continue;
			}

			int rowLen;
			try { rowLen = st2.RowLength; } catch (NotSupportedException e) {
				report.Add((st2.Name, 0, 0, 0, "", "sema tipi desteklenmiyor: " + e.Message));
				continue;
			}

			var ptData = ReadFromIndex(index, ptPath);
			var enData = ReadFromIndex(index, $"data/balance/{name}.datc64");
			if (ptData is null || enData is null) {
				report.Add((st2.Name, 0, 0, 0, "", "dosya okunamadi"));
				continue;
			}

			try {
				var pt = DatFile.Load(ptData, rowLen, out var warn);
				if (warn is not null) {
					report.Add((st2.Name, pt.RowCount, 0, 0, "", "ATLANDI - " + warn));
					continue;
				}
				// Gidis-donus kontrolu: dokunulmamis tablo bayt bayt ayni cikmali
				if (!pt.Save().AsSpan().SequenceEqual(ptData)) {
					report.Add((st2.Name, pt.RowCount, 0, 0, "", "ATLANDI - gidis-donus bozuldu"));
					continue;
				}
				var en = DatFile.Load(enData, rowLen, out _);
				if (en.RowCount != pt.RowCount) {
					report.Add((st2.Name, pt.RowCount, 0, 0, "", $"ATLANDI - satir sayisi farkli (EN {en.RowCount} / PT {pt.RowCount})"));
					continue;
				}

				var cells = 0; long chars = 0; var colNames = new List<string>();
				for (var i = 0; i < st2.Columns.Count; ++i) {
					var c = st2.Columns[i];
					if (c.Array || c.Type != "string") continue;
					var off2 = st2.OffsetOf(i);
					var diff = 0; long dchars = 0;
					for (var r = 0; r < pt.RowCount; ++r) {
						var ev = en.ReadString(r, off2);
						if (string.IsNullOrEmpty(ev)) continue;
						if (!string.Equals(ev, pt.ReadString(r, off2), StringComparison.Ordinal)) {
							++diff; dchars += ev.Length;
						}
					}
					if (diff > 0) {
						cells += diff; chars += dchars;
						// yildiz: semada @localized isaretli degil ama pratikte cevriliyor
						colNames.Add((c.Name ?? $"#{i}") + (c.Localized ? "" : "*"));
					}
				}
				report.Add((st2.Name, pt.RowCount, cells, chars, string.Join(" ", colNames), "tamam"));
			} catch (Exception e) {
				report.Add((st2.Name, 0, 0, 0, "", "HATA: " + e.Message));
			}
		}

		var ok = report.Where(r => r.Status == "tamam").ToArray();
		Console.WriteLine($"Portekizce dosya              : {report.Count}");
		Console.WriteLine($"Sorunsuz okunan               : {ok.Length}");
		Console.WriteLine($"Metin iceren                  : {ok.Count(r => r.Cells > 0)}");
		Console.WriteLine($"Toplam cevrilecek hucre       : {ok.Sum(r => (long)r.Cells):N0}");
		Console.WriteLine($"Toplam karakter               : {ok.Sum(r => r.Chars):N0}");
		Console.WriteLine();
		Console.WriteLine("En buyuk 25 tablo:");
		foreach (var r in ok.OrderByDescending(r => r.Chars).Take(25))
			Console.WriteLine($"  {r.Table,-34} {r.Cells,7:N0} hucre {r.Chars,10:N0} kar.  {r.Cols}");

		var bad = report.Where(r => r.Status != "tamam").ToArray();
		if (bad.Length != 0) {
			Console.WriteLine();
			Console.WriteLine($"Okunamayan {bad.Length} tablo:");
			foreach (var r in bad)
				Console.WriteLine($"  {r.Table,-34} {r.Status}");
		}

		if (outCsv is not null) {
			Directory.CreateDirectory(Path.GetDirectoryName(outCsv)!);
			File.WriteAllLines(outCsv,
				new[] { "Tablo;Satir;Hucre;Karakter;Kolonlar;Durum" }
					.Concat(report.OrderByDescending(r => r.Chars)
						.Select(r => $"{r.Table};{r.Rows};{r.Cells};{r.Chars};{r.Cols};{r.Status}")),
				new UTF8Encoding(true));
			Console.WriteLine();
			Console.WriteLine("CSV yazildi -> " + outCsv);
		}
		return 0;
	}

	case "dat-sniff": {
		// dat-sniff <index> <tabloAdi> [ornekSayisi]  -- sema kullanmadan kolon tespiti
		if (args.Length < 3) { Usage(); return 1; }
		var sniffName = args[2].ToLowerInvariant();
		var samples = args.Length > 3 ? int.Parse(args[3]) : 3;

		using var index = OpenIndex(indexPath);
		var enRaw = ReadFromIndex(index, $"data/balance/{sniffName}.datc64");
		var ptRaw = ReadFromIndex(index, $"data/balance/portuguese/{sniffName}.datc64");
		if (enRaw is null || ptRaw is null) {
			Console.Error.WriteLine("Ingilizce veya Portekizce dosya indexte yok: " + sniffName);
			return 1;
		}

		var sniffSchema = SchemaRoot.Load(SchemaRoot.Locate()).Poe2ByName();
		sniffSchema.TryGetValue(sniffName, out var sniffTable);
		var resolved = TableResolver.Resolve(sniffName, enRaw, ptRaw, sniffTable, out var problem);
		if (resolved is null) {
			Console.Error.WriteLine("Cozumlenemedi: " + problem);
			return 1;
		}

		Console.WriteLine($"{sniffName}");
		Console.WriteLine($"  {resolved.Localized.RowCount} satir x {resolved.Localized.RowLength} bayt");
		Console.WriteLine($"  yontem: {(resolved.UsedSchema ? "sema" : "sema-bagimsiz tespit")}");
		Console.WriteLine($"  cevrilebilir kolon: {resolved.Columns.Count}");
		Console.WriteLine();

		foreach (var c in resolved.Columns) {
			Console.WriteLine($"  bayt {c.Offset,5} {c.Name ?? "(isimsiz)",-24}: {c.Cells,6:N0} hucre, {c.Chars,9:N0} karakter");
			var shown = 0;
			for (var r = 0; r < resolved.Localized.RowCount && shown < samples; ++r) {
				var e = resolved.English.ReadString(r, c.Offset);
				var p = resolved.Localized.ReadString(r, c.Offset);
				if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p)) continue;
				if (string.Equals(e, p, StringComparison.Ordinal)) continue;
				Console.WriteLine($"      [{r}] EN: {Trim(e)}");
				Console.WriteLine($"           PT: {Trim(p)}");
				++shown;
			}
			Console.WriteLine();
		}
		return 0;
	}

	case "csd-export": {
		// csd-export <index> <cikti.jsonl>
		// data/statdescriptions/*.csd icindeki stat/mod metinleri.
		if (args.Length < 3) { Usage(); return 1; }
		var csdOut = Path.GetFullPath(args[2]);
		using var idxC = OpenIndex(indexPath);
		const string csdDir = "data/statdescriptions/";
		var csdPaths = idxC.Files.Values.Select(f => f.Path)
			.Where(p => !string.IsNullOrEmpty(p) && p.StartsWith(csdDir, StringComparison.Ordinal)
				&& p.EndsWith(".csd", StringComparison.OrdinalIgnoreCase))
			.Order(StringComparer.OrdinalIgnoreCase).ToArray();

		Directory.CreateDirectory(Path.GetDirectoryName(csdOut)!);
		using var csdWriter = new StreamWriter(csdOut, false, new UTF8Encoding(false));
		var jsonC = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
		int filesDone = 0, unitsC = 0; long charsC = 0;

		foreach (var path in csdPaths) {
			var raw = ReadFromIndex(idxC, path);
			if (raw is null) continue;
			var lines = CsdFile.ReadLines(raw);
			var entries = CsdFile.Parse(lines);
			if (entries.Count == 0) continue;
			++filesDone;
			foreach (var e in entries) {
				csdWriter.WriteLine(JsonSerializer.Serialize(new TranslationUnit {
					Id = $"{path}|{e.DescIndex}|{e.VariantIndex}",
					Table = path, Off = e.DescIndex, Row = e.VariantIndex,
					Col = "csd", En = e.English,
				}, jsonC));
				++unitsC; charsC += e.English.Length;
			}
		}

		Console.WriteLine($"Dosya (metin iceren) : {filesDone} / {csdPaths.Length}");
		Console.WriteLine($"Ceviri birimi        : {unitsC:N0}");
		Console.WriteLine($"Kaynak karakter      : {charsC:N0}");
		Console.WriteLine($"Yazildi -> {csdOut}");
		return 0;
	}

	case "csd-check": {
		// csd-check <index>
		// Gidis-donus dogrulamasi: degistirmeden okuyup yazinca dosya bayt bayt
		// ayni cikmali. Oyuna yazmadan once bu gecmeli.
		using var idxK = OpenIndex(indexPath);
		const string csdDirK = "data/statdescriptions/";
		var pathsK = idxK.Files.Values.Select(f => f.Path)
			.Where(p => !string.IsNullOrEmpty(p) && p.StartsWith(csdDirK, StringComparison.Ordinal)
				&& p.EndsWith(".csd", StringComparison.OrdinalIgnoreCase))
			.Order(StringComparer.OrdinalIgnoreCase).ToArray();

		int same = 0, diff = 0;
		foreach (var path in pathsK) {
			var raw = ReadFromIndex(idxK, path);
			if (raw is null) continue;
			var back = CsdFile.WriteLines(CsdFile.ReadLines(raw), true);
			if (back.AsSpan().SequenceEqual(raw)) ++same;
			else {
				++diff;
				if (diff <= 5) {
					Console.WriteLine($"  FARKLI {Path.GetFileName(path),-50} {raw.Length,9:N0} -> {back.Length,9:N0}");
					// Ilk sapmayi goster: "6 bayt buyudu" tek basina teshis degil,
					// neyin degistigini gormeden yazmak riskli.
					var n = Math.Min(raw.Length, back.Length);
					var at = 0;
					while (at < n && raw[at] == back[at]) ++at;
					static string Peek(byte[] b, int at) {
						var s = Math.Max(0, at - 40);
						var len = Math.Min(120, b.Length - s);
						return Encoding.Unicode.GetString(b, s & ~1, len & ~1).Replace("\r", "\\r").Replace("\n", "\\n");
					}
					Console.WriteLine($"     ilk sapma bayt {at:N0} (~%{100.0 * at / raw.Length:F1})");
					Console.WriteLine($"     OYUNDA : {Peek(raw, at)}");
					Console.WriteLine($"     YAZILAN: {Peek(back, at)}");
				}
			}
		}
		Console.WriteLine();
		Console.WriteLine($"Birebir ayni : {same} / {pathsK.Length}");
		Console.WriteLine($"Farkli       : {diff}");
		return diff == 0 ? 0 : 1;
	}

	case "csd-import": {
		// csd-import <index> <kaynak.jsonl> <bellek.jsonl>
		if (args.Length < 4) { Usage(); return 1; }
		var csdCorpus = Path.GetFullPath(args[2]);
		var csdMemPath = Path.GetFullPath(args[3]);
		if (!File.Exists(csdCorpus) || !File.Exists(csdMemPath)) {
			Console.Error.WriteLine("Kaynak veya bellek dosyasi yok");
			return 1;
		}

		using var csdMem = new TranslationMemory(csdMemPath);
		Console.WriteLine($"Bellekteki ceviri: {csdMem.Count:N0}");

		// dosya -> (descIndex, variantIndex) -> ceviri
		var byFile = new Dictionary<string, Dictionary<(int, int), string>>(StringComparer.Ordinal);
		int matchedC = 0, missingC = 0;
		foreach (var line in File.ReadLines(csdCorpus)) {
			if (string.IsNullOrWhiteSpace(line)) continue;
			TranslationUnit? u;
			try { u = JsonSerializer.Deserialize<TranslationUnit>(line); } catch { continue; }
			if (u?.Table is null || u.En is null) continue;

			// Cevirisi yoksa INGILIZCE yaz — bkz. dat-import'taki ayni gerekce.
			string metinC;
			if (csdMem.TryGet(u.En, out var tr) && !string.IsNullOrEmpty(tr)) { metinC = tr; ++matchedC; }
			else { metinC = u.En; ++missingC; }

			if (!byFile.TryGetValue(u.Table, out var map))
				byFile[u.Table] = map = [];
			map[(u.Off, u.Row)] = metinC;
		}
		Console.WriteLine($"Eslesen birim    : {matchedC:N0}");
		if (missingC != 0) Console.WriteLine($"Cevirisi yok     : {missingC:N0}  (Ingilizce yazilacak)");
		Console.WriteLine();

		using var idxCi = OpenIndex(indexPath);
		var totalChanged = 0;
		foreach (var (path, map) in byFile.OrderBy(k => k.Key, StringComparer.Ordinal)) {
			var raw = ReadFromIndex(idxCi, path);
			if (raw is null) { Console.Error.WriteLine($"  atlandi {path}"); continue; }
			var lines = CsdFile.ReadLines(raw);
			var changed = CsdFile.Rewrite(lines, map);
			if (changed == 0) continue;
			if (!idxCi.TryGetFile(path, out var rec)) continue;
			rec.Write(CsdFile.WriteLines(lines, true), saveIndex: false);
			totalChanged += changed;
			Console.WriteLine($"  {Path.GetFileName(path),-52} {changed,6:N0} satir");
		}
		idxCi.Save();
		Console.WriteLine();
		Console.WriteLine($"Toplam {totalChanged:N0} satir yazildi, index kaydedildi.");
		return 0;
	}

	case "names-export": {
		// names-export <index> <cikti.txt>
		// Ceviri disi kolonlardaki ADLARI toplar. Bu adlar baska metinlerin
		// ICINDE gectiginde de Ingilizce kalmali (ornek: "Cannot be supported
		// by Multistrike" -> "Multistrike" cevrilmemeli).
		if (args.Length < 3) { Usage(); return 1; }
		var namesOut = Path.GetFullPath(args[2]);

		// Sozlukte cevrilebilir olarak gecen terimler ad sayilmaz: "Staff",
		// "Mace" gibi kelimeler hem taban esya adi hem de kategori adi, ve
		// aciklama metinlerinde cevrilmeleri gerekiyor.
		var glossaryPath = Path.Combine(Path.GetDirectoryName(Exclusions.Locate()!)!, "sozluk.tsv");
		var translatable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var raw in File.ReadLines(glossaryPath)) {
			var l = raw.Trim();
			if (l.Length == 0 || l.StartsWith('#')) continue;
			var pp = l.Split('\t');
			if (pp.Length >= 2 && !string.Equals(pp[0], pp[1], StringComparison.Ordinal))
				translatable.Add(pp[0]);
		}

		var schemaN = SchemaRoot.Load(SchemaRoot.Locate()).Poe2ByName();
		var exclN = Exclusions.Load(Exclusions.Locate());
		using var indexN = OpenIndex(indexPath);

		const string ptDirN = "data/balance/portuguese/";
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var ptPath in indexN.Files.Values.Select(f => f.Path)
				.Where(p => !string.IsNullOrEmpty(p) && p.StartsWith(ptDirN, StringComparison.Ordinal))) {
			var table = Path.GetFileNameWithoutExtension(ptPath);
			var ptRaw = ReadFromIndex(indexN, ptPath);
			var enRaw = ReadFromIndex(indexN, $"data/balance/{table}.datc64");
			if (ptRaw is null || enRaw is null) continue;
			schemaN.TryGetValue(table, out var stN);
			var resN = TableResolver.Resolve(table, enRaw, ptRaw, stN, out _);
			if (resN is null) continue;

			foreach (var col in resN.Columns) {
				if (!exclN.IsExcluded(table, col.Name, col.Offset)) continue;
				// Ceviri disi her kolon ad kaynagi OLAMAZ:
				//   passiveskills|Name  -> adlar cogu zaman stat cumlesinin kendisi
				//                          ("Energy Shield and Armour applies to...").
				//                          Maskelersek ayni cumleyi mod aciklamasinda
				//                          da ceviremeyiz.
				//   words|Text2         -> tek kelimeler ("White", "Viper"), sıradan
				//                          metinde de geciyor.
				// Yalnizca ayirt edici ve dusuk riskli kaynaklari aliyoruz.
				var safeSource =
					(table.Equals("baseitemtypes", StringComparison.OrdinalIgnoreCase) && col.Name == "Name")
					|| (table.Equals("activeskills", StringComparison.OrdinalIgnoreCase) && col.Name == "DisplayedName")
					|| (table.Equals("mtxtypes", StringComparison.OrdinalIgnoreCase) && col.Name == "Name");
				if (!safeSource) continue;

				for (var r = 0; r < resN.English.RowCount; ++r) {
					var v = resN.English.ReadString(r, col.Offset);
					if (string.IsNullOrEmpty(v)) continue;
					if (!char.IsUpper(v[0])) continue;
					if (translatable.Contains(v)) continue;
					if (v.Contains('[') || v.Contains('<') || v.Contains('{')) continue;
					// Ayirt edicilik: ya cok kelimeli ("Lightning Arrow") ya da
					// yeterince uzun tek kelime ("Multistrike"). Kisa tek kelimeler
					// ("Wrath", "Viper") sıradan metinde de gectigi icin disarida.
					var multiWord = v.Contains(' ');
					if (!multiWord && v.Length < 8) continue;
					names.Add(v);
				}
			}
		}

		var ordered = names.OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).ToArray();
		Directory.CreateDirectory(Path.GetDirectoryName(namesOut)!);
		File.WriteAllLines(namesOut, ordered, new UTF8Encoding(false));
		Console.WriteLine($"Korunacak ad: {ordered.Length:N0}");
		Console.WriteLine($"Yazildi -> {namesOut}");
		return 0;
	}

	case "bellek-birlestir": {
		// bellek-birlestir <hedefBellek.jsonl> <ekBellek.jsonl>
		//
		// Ek bellegi hedefe ekler. Bellek append-only ve SON deger gecerli oldugu
		// icin eklemek yeterli - uzerine yazmaya gerek yok.
		//
		// Yalnizca stat kalibini GECEN kayitlar aliniyor: yeniden ceviri koşusu
		// eskisinden daha kotu bir sonuc uretmissе eskisi yerinde kalsin.
		if (args.Length < 3) { Usage(); return 1; }
		var hedef = indexPath; // args[1]
		var ek = Path.GetFullPath(args[2]);
		if (!File.Exists(hedef)) { Console.Error.WriteLine("Hedef bellek yok: " + hedef); return 1; }
		if (!File.Exists(ek)) { Console.Error.WriteLine("Ek bellek yok: " + ek); return 1; }

		var jsonOpts2 = new JsonSerializerOptions {
			Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		};

		int alinan = 0, elenen = 0;
		using (var w = new StreamWriter(new FileStream(hedef, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))) {
			foreach (var line in File.ReadLines(ek)) {
				if (string.IsNullOrWhiteSpace(line)) continue;
				MemoryEntry? e;
				try { e = JsonSerializer.Deserialize<MemoryEntry>(line); } catch { continue; }
				if (e is not { En.Length: > 0 } || string.IsNullOrWhiteSpace(e.Tr)) continue;

				var tr = StatText.Normalize(e.En, e.Tr);
				if (StatText.Problem(e.En, tr) is not null) { ++elenen; continue; }
				w.WriteLine(JsonSerializer.Serialize(new MemoryEntry { En = e.En, Tr = tr }, jsonOpts2));
				++alinan;
			}
		}
		Console.WriteLine($"Alinan : {alinan:N0}");
		Console.WriteLine($"Elenen : {elenen:N0}  (kalip hâlâ bozuk, eski ceviri yerinde kaliyor)");
		Console.WriteLine("Simdi 'stat-duzelt' calistir: bellek tekillesir ve son durum raporlanir.");
		return 0;
	}

	case "stat-duzelt": {
		// stat-duzelt <bellek.jsonl> [kalanlar.jsonl]
		//
		// Iki is yapar:
		//   1. Makineyle duzeltilebilen stat kalibi hatalarini bellekte yerinde duzeltir
		//      (yuzde konumu, uydurulmus "ila"). Bunlar anlama dokunmaz.
		//   2. Kalan dizim hatalarini ayri bir korpusa yazar; onlar modele geri gider.
		//
		// Bellek append-only oldugu icin ayni "en" birden cok kez gecebilir; SON
		// deger gecerlidir. Burada dosyayi tekillestirerek yeniden yaziyoruz.
		var memFile = indexPath; // args[1]
		var leftoverFile = args.Length > 2 ? Path.GetFullPath(args[2]) : null;
		if (!File.Exists(memFile)) { Console.Error.WriteLine("Bellek yok: " + memFile); return 1; }

		var jsonOpts = new JsonSerializerOptions {
			Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		};

		// Sirayi koru, tekrar edende sonuncuyu tut
		var order = new List<string>();
		var latest = new Dictionary<string, string>(StringComparer.Ordinal);
		var broken = 0;
		foreach (var line in File.ReadLines(memFile)) {
			if (string.IsNullOrWhiteSpace(line)) continue;
			MemoryEntry? e;
			try { e = JsonSerializer.Deserialize<MemoryEntry>(line); } catch { ++broken; continue; }
			if (e is not { En.Length: > 0 }) continue;
			if (!latest.ContainsKey(e.En)) order.Add(e.En);
			latest[e.En] = e.Tr;
		}
		if (broken != 0) Console.Error.WriteLine($"Uyari: {broken} bozuk satir atlandi");

		int fixedCount = 0, stillBad = 0;
		var leftovers = new List<string>();
		foreach (var en in order) {
			var tr = latest[en];
			var norm = StatText.Normalize(en, tr);
			if (!string.Equals(norm, tr, StringComparison.Ordinal)) { latest[en] = norm; ++fixedCount; }
			var problem = StatText.Problem(en, norm);
			if (problem is not null) {
				++stillBad;
				leftovers.Add(en);
				if (stillBad <= 10) Console.WriteLine($"  kalan: {problem}\n     EN {Trim(en)}\n     TR {Trim(norm)}");
			}
		}

		// Yedekle, sonra yerinde yaz
		var backup = memFile + ".yedek";
		if (!File.Exists(backup)) File.Copy(memFile, backup);
		var tmp = memFile + ".tmp";
		using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false))) {
			foreach (var en in order)
				w.WriteLine(JsonSerializer.Serialize(new MemoryEntry { En = en, Tr = latest[en] }, jsonOpts));
		}
		File.Move(tmp, memFile, overwrite: true);

		if (leftoverFile is not null) {
			Directory.CreateDirectory(Path.GetDirectoryName(leftoverFile)!);
			using var w = new StreamWriter(leftoverFile, false, new UTF8Encoding(false));
			foreach (var en in leftovers)
				w.WriteLine(JsonSerializer.Serialize(new TranslationUnit { En = en }, jsonOpts));
		}

		Console.WriteLine();
		Console.WriteLine($"Bellekteki benzersiz kayit : {order.Count:N0}");
		Console.WriteLine($"Makineyle duzeltilen       : {fixedCount:N0}");
		Console.WriteLine($"Yeniden cevrilmesi gereken : {stillBad:N0}");
		Console.WriteLine($"Yedek                      : {backup}");
		if (leftoverFile is not null) Console.WriteLine($"Kalanlar korpusu           : {leftoverFile}");
		return 0;
	}

	case "translate": {
		// translate <kaynak.jsonl> <bellek.jsonl> [model] [partiBoyutu]
		// indexPath burada kaynak dosyasi olarak kullaniliyor
		if (args.Length < 3) { Usage(); return 1; }
		var corpus = indexPath; // args[1]
		var memoryPath = Path.GetFullPath(args[2]);
		var model = args.Length > 3 ? args[3] : "gemma3:12b";
		var batchSize = args.Length > 4 ? int.Parse(args[4]) : 10;

		var glossary = Path.Combine(
			Path.GetDirectoryName(Exclusions.Locate() ?? throw new FileNotFoundException(
				"ceviri-disi.txt bulunamadi, proje kokunu tespit edemedim"))!,
			"sozluk.tsv");
		if (!File.Exists(glossary)) { Console.Error.WriteLine("Sozluk yok: " + glossary); return 1; }

		// Benzersiz kaynak metinler (tekrar edenler bir kez cevrilir)
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var sources = new List<string>();
		foreach (var line in File.ReadLines(corpus)) {
			if (string.IsNullOrWhiteSpace(line)) continue;
			TranslationUnit? u;
			try { u = JsonSerializer.Deserialize<TranslationUnit>(line); } catch { continue; }
			if (u?.En is null || u.En.Length == 0) continue;
			if (seen.Add(u.En)) sources.Add(u.En);
		}

		Console.WriteLine($"Model    : {model}");
		Console.WriteLine($"Sozluk   : {glossary}");
		Console.WriteLine($"Bellek   : {memoryPath}");
		Console.WriteLine($"Parti    : {batchSize}");
		Console.WriteLine();

		using var cts = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) => {
			e.Cancel = true; // hemen olme, parti bitsin ve diske yazilsin
			Console.WriteLine();
			Console.WriteLine("Durdurma istendi, bu parti bitince guvenle cikilacak...");
			cts.Cancel();
		};

		using var memory = new TranslationMemory(memoryPath);
		var translator = new Translator(model, glossary);
		await translator.RunAsync(sources, memory, batchSize, maxAttempts: 3, cts.Token);
		return 0;
	}

	case "dat-export": {
		// dat-export <index> <cikti.jsonl> [tabloFiltresi]
		if (args.Length < 3) { Usage(); return 1; }
		var outFile = Path.GetFullPath(args[2]);
		var filter = args.Length > 3 ? args[3] : null;

		var schemaMap = SchemaRoot.Load(SchemaRoot.Locate()).Poe2ByName();
		var exclusions = Exclusions.Load(Exclusions.Locate());
		Console.WriteLine($"Ceviri disi kolon kurali: {exclusions.Count}");
		using var index = OpenIndex(indexPath);

		const string dir = "data/balance/portuguese/";
		var paths = index.Files.Values
			.Select(f => f.Path)
			.Where(p => !string.IsNullOrEmpty(p) && p.StartsWith(dir, StringComparison.Ordinal))
			.Where(p => filter is null || p.Contains(filter, StringComparison.OrdinalIgnoreCase))
			.Order(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
		using var writer = new StreamWriter(outFile, false, new UTF8Encoding(false));
		var jsonOpts = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

		int okTables = 0, skipped = 0, units = 0; long chars = 0;
		int excludedCols = 0, excludedCells = 0; long excludedChars = 0;
		foreach (var ptPath in paths) {
			var table = Path.GetFileNameWithoutExtension(ptPath);
			var ptRaw = ReadFromIndex(index, ptPath);
			var enRaw = ReadFromIndex(index, $"data/balance/{table}.datc64");
			if (ptRaw is null || enRaw is null) { ++skipped; continue; }

			schemaMap.TryGetValue(table, out var st);
			var res = TableResolver.Resolve(table, enRaw, ptRaw, st, out var problem);
			if (res is null) {
				Console.Error.WriteLine($"  atlandi {table}: {problem}");
				++skipped;
				continue;
			}
			if (res.Columns.Count == 0) continue;
			++okTables;

			foreach (var col in res.Columns) {
				if (exclusions.IsExcluded(table, col.Name, col.Offset)) {
					++excludedCols; excludedCells += col.Cells; excludedChars += col.Chars;
					continue;
				}
				for (var r = 0; r < res.Localized.RowCount; ++r) {
					var e = res.English.ReadString(r, col.Offset);
					var p = res.Localized.ReadString(r, col.Offset);
					if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p)) continue;

					// EN ile PT'nin AYNI oldugu hucre ATLANMAZ.
					//
					// Atlarsak yama ikinci kez calistirildiginda kendi yazdigini
					// goremez hale geliyor. Birinci kosu, cevirisi olmayan hucreye
					// Ingilizce yaziyor (dat-import'taki karara bakin). Ikinci
					// kosuda o hucrede artik EN == PT oldugu icin buradan eleniyor,
					// birime hic donusmuyor ve bellege sonradan eklenen ceviri o
					// hucreye BIR DAHA ULASAMIYOR.
					//
					// Olculdu: 19 Eylul istemcisinde arka arkaya iki kosu, ikincide
					// 103.736 birimi 102.859'a dusurdu. Aradaki 877 hucrenin bir
					// kismi zararsizdi (cevirisi Ingilizcesiyle ayni olan adlar),
					// ama 91'i gercekten cevirisi bekleyen hucreydi ve erisilemez
					// hale gelmisti.
					//
					// Kolon TESPITI hala EN != PT kuralini kullaniyor
					// (TableResolver.Count), yani gercekten yerellestirilmemis bir
					// kolon yine kolon sayilmiyor. Burada elenen tek sey, zaten
					// kabul edilmis bir kolonun icindeki tekil hucrelerdi.

					writer.WriteLine(JsonSerializer.Serialize(new TranslationUnit {
						Id = $"{table}|{col.Offset}|{r}",
						Table = table, Off = col.Offset, Row = r,
						Col = col.Name, En = e,
					}, jsonOpts));
					++units; chars += e.Length;
				}
			}
		}

		Console.WriteLine($"Tablo (metin iceren) : {okTables}");
		Console.WriteLine($"Atlanan tablo        : {skipped}");
		Console.WriteLine($"Ceviri disi kolon    : {excludedCols}  ({excludedCells:N0} hucre, {excludedChars:N0} karakter)");
		Console.WriteLine($"Ceviri birimi        : {units:N0}");
		Console.WriteLine($"Kaynak karakter      : {chars:N0}");
		Console.WriteLine($"Yazildi -> {outFile}");
		return 0;
	}

	case "dat-import": {
		// dat-import <index> <kaynak.jsonl> <bellek.jsonl>
		if (args.Length < 4) { Usage(); return 1; }
		var corpusFile = Path.GetFullPath(args[2]);
		var memFile = Path.GetFullPath(args[3]);
		if (!File.Exists(corpusFile)) { Console.Error.WriteLine("Kaynak yok: " + corpusFile); return 1; }
		if (!File.Exists(memFile)) { Console.Error.WriteLine("Bellek yok: " + memFile); return 1; }

		using var mem = new TranslationMemory(memFile);
		Console.WriteLine($"Bellekteki ceviri: {mem.Count:N0}");

		var schemaMap2 = SchemaRoot.Load(SchemaRoot.Locate()).Poe2ByName();
		// tablo -> (satir, kolonOffseti, ceviri)
		var byTable = new Dictionary<string, List<(int Row, int Off, string Tr)>>(StringComparer.OrdinalIgnoreCase);
		int bad = 0, missing = 0, matched = 0;
		foreach (var line in File.ReadLines(corpusFile)) {
			if (string.IsNullOrWhiteSpace(line)) continue;
			TranslationUnit? u;
			try { u = JsonSerializer.Deserialize<TranslationUnit>(line); } catch { ++bad; continue; }
			if (u?.Table is null || u.En is null) { ++bad; continue; }

			// Cevirisi yoksa INGILIZCE yaz, atlama.
			//
			// Atlarsak hucre Portekizce kalir - yazdigimiz slot Portekizce.
			// Oyun guncellemesiyle gelen yeni metinler icin bu en kotu sonuc:
			// Turk oyuncuya Portekizce, Ingilizceden cok daha yabanci. Ustelik
			// eski mesaj "Ingilizce kalacak" diyordu, yani davranis yanlis
			// belgeleniyordu da.
			string metin;
			if (mem.TryGet(u.En, out var tr) && !string.IsNullOrEmpty(tr)) { metin = tr; ++matched; }
			else { metin = u.En; ++missing; }

			if (!byTable.TryGetValue(u.Table, out var list))
				byTable[u.Table] = list = [];
			list.Add((u.Row, u.Off, metin));
		}
		Console.WriteLine($"Eslesen birim    : {matched:N0}");
		if (missing != 0) Console.WriteLine($"Cevirisi yok     : {missing:N0}  (Ingilizce yazilacak)");
		if (bad != 0) Console.Error.WriteLine($"Uyari: {bad} satir okunamadi");
		Console.WriteLine();

		// Ceviri disi kolonlar: dokunmazsak PORTEKIZCE metin kalir, cunku
		// yazdigimiz slot Portekizce. Kullanici bu adlarin INGILIZCE olmasini
		// istiyor (ticaret sitesi / build rehberi ile eslesme), o yuzden
		// Ingilizce degeri acikca yaziyoruz.
		var exclImport = Exclusions.Load(Exclusions.Locate());
		Console.WriteLine($"Ceviri disi kolon kurali: {exclImport.Count} (Ingilizce yazilacak)");
		Console.WriteLine();

		using var index2 = OpenIndex(indexPath);
		var written = 0;
		var englishWritten = 0;

		// Ceviri disi kolonlari da isleyebilmek icin ilgili tablolari listeye kat
		const string ptDirImp = "data/balance/portuguese/";
		foreach (var ptPath in index2.Files.Values.Select(f => f.Path)
				.Where(p => !string.IsNullOrEmpty(p) && p.StartsWith(ptDirImp, StringComparison.Ordinal))) {
			var t = Path.GetFileNameWithoutExtension(ptPath);
			if (!byTable.ContainsKey(t))
				byTable[t] = [];
		}

		foreach (var (table, edits) in byTable.OrderBy(k => k.Key)) {
			var ptPath = $"data/balance/portuguese/{table}.datc64";
			var ptRaw = ReadFromIndex(index2, ptPath);
			var enRaw = ReadFromIndex(index2, $"data/balance/{table}.datc64");
			if (ptRaw is null || enRaw is null) {
				Console.Error.WriteLine($"  atlandi {table}: dosya indexte yok");
				continue;
			}
			schemaMap2.TryGetValue(table, out var st2);
			var res = TableResolver.Resolve(table, enRaw, ptRaw, st2, out var problem);
			if (res is null) {
				Console.Error.WriteLine($"  atlandi {table}: {problem}");
				continue;
			}

			// Ceviri disi kolonlara Ingilizce degeri yaz
			var enEdits = new List<(int Row, int Off, string Value)>();
			foreach (var col in res.Columns) {
				if (!exclImport.IsExcluded(table, col.Name, col.Offset)) continue;
				for (var r = 0; r < res.Localized.RowCount; ++r) {
					var en = res.English.ReadString(r, col.Offset);
					if (string.IsNullOrEmpty(en)) continue;
					if (string.Equals(en, res.Localized.ReadString(r, col.Offset), StringComparison.Ordinal)) continue;
					enEdits.Add((r, col.Offset, en));
				}
			}

			var all = edits.Select(e => (e.Row, e.Off, e.Tr)).Concat(enEdits).ToList();
			if (all.Count == 0) continue;

			var applied = res.Localized.ApplyStrings(all);
			englishWritten += enEdits.Count;
			var bytes = res.Localized.Save();
			if (!index2.TryGetFile(ptPath, out var rec)) {
				Console.Error.WriteLine($"  atlandi {table}: index kaydi yok");
				continue;
			}
			rec.Write(bytes, saveIndex: false);
			Console.WriteLine($"  {table,-34} {applied,6:N0} hucre");
			written += applied;
		}
		index2.Save();
		Console.WriteLine();
		Console.WriteLine($"Turkce ceviri     : {written - englishWritten:N0} hucre");
		Console.WriteLine($"Ingilizce ad      : {englishWritten:N0} hucre");
		Console.WriteLine($"Toplam {written:N0} hucre yazildi, index kaydedildi.");
		return 0;
	}

	default:
		Usage();
		return 1;
}

static byte[]? ReadFromIndex(Index index, string path)
	=> index.TryGetFile(path, out var f) ? f.Read().ToArray() : null;

static string Trim(string s) {
	s = s.Replace("\r", " ").Replace("\n", " ");
	return s.Length > 90 ? s[..90] + "..." : s;
}

// Index'i acar. parsePaths:true ile acmak, cozulemeyen birkac yol yuzunden
// exception firlatiyor; bu yuzden elle ayristirip basarisizlari yok sayiyoruz.
static Index OpenIndex(string path) {
	var index = new Index(path, parsePaths: false);
	var failed = index.ParsePaths();
	if (failed != 0)
		Console.Error.WriteLine($"Uyari: {failed} dosyanin yolu cozulemedi, yok sayiliyor.");
	return index;
}

static void Usage() {
	Console.WriteLine("""
		poe2tr - PoE2 bundle okuma/yazma araci

		  poe2tr info    <_.index.bin>
		  poe2tr list    <_.index.bin> [filtre] [cikti.txt]
		  poe2tr extract <_.index.bin> <onEk> <hedefKlasor>
		  poe2tr write   <_.index.bin> <oyunIciYol> <yerelDosya>

		  poe2tr dat-dump   <_.index.bin> <TabloAdi> [satirSayisi]
		  poe2tr dat-tables <_.index.bin> [cikti.csv]
		  poe2tr dat-sniff  <_.index.bin> <tabloAdi> [ornekSayisi]

		  poe2tr dat-export <_.index.bin> <cikti.jsonl> [tabloFiltresi]
		  poe2tr translate  <kaynak.jsonl> <bellek.jsonl> [model] [partiBoyutu]
		  poe2tr dat-import <_.index.bin> <kaynak.jsonl> <bellek.jsonl>

		  poe2tr stat-duzelt <bellek.jsonl> [kalanlar.jsonl]
		      Stat satiri kalibini bellekte yerinde duzeltir (yuzde konumu,
		      uydurulmus "ila"). Duzeltilemeyenleri kalanlar.jsonl'e yazar;
		      o dosyayi translate'e verip modele geri gonder.

		translate istedigin zaman Ctrl+C ile durdurulabilir; bellek dosyasi
		append-only oldugu icin ayni komut kaldigi yerden devam eder.
		""");
}
