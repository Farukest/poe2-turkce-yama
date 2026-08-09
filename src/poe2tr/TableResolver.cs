namespace Poe2Tr;

/// <summary>Bir tabloda cevrilebilir oldugu tespit edilen metin kolonu.</summary>
public sealed record TranslatableColumn(int Offset, string? Name, int Cells, long Chars);

/// <summary>Cozumlenmis tablo: Ingilizce kaynak, yerellestirilmis hedef, cevrilecek kolonlar.</summary>
public sealed class ResolvedTable {
	public required string Name { get; init; }
	public required DatFile English { get; init; }
	public required DatFile Localized { get; init; }
	public required IReadOnlyList<TranslatableColumn> Columns { get; init; }
	/// <summary>Sema kullanildi mi, yoksa sema-bagimsiz tespite mi dusuldu.</summary>
	public required bool UsedSchema { get; init; }

	public int TotalCells => Columns.Sum(c => c.Cells);
	public long TotalChars => Columns.Sum(c => c.Chars);
}

public static class TableResolver {
	/// <summary>
	/// Bir tabloyu cozumler. Once sema denenir; satir uzunlugu uyusmazsa
	/// sema-bagimsiz tespite dusulur.
	/// </summary>
	/// <returns>Cozumlenemezse null; sebep <paramref name="problem"/> ile bildirilir.</returns>
	public static ResolvedTable? Resolve(
			string tableName, byte[] enRaw, byte[] ptRaw, SchemaTable? schema, out string? problem) {
		problem = null;
		DatFile en, pt;
		var usedSchema = false;

		// 1) Sema yolu
		if (schema is not null) {
			int rowLen;
			try { rowLen = schema.RowLength; } catch (NotSupportedException) { rowLen = -1; }
			if (rowLen > 0) {
				try {
					var p = DatFile.Load(ptRaw, rowLen, out var warn);
					if (warn is null) {
						pt = p;
						en = DatFile.Load(enRaw, rowLen, out _);
						usedSchema = true;
						goto resolved;
					}
				} catch (Exception) { /* sema-bagimsiz yola dusulecek */ }
			}
		}

		// 2) Sema-bagimsiz yol
		try {
			pt = DatFile.LoadWithoutSchema(ptRaw);
			en = DatFile.LoadWithoutSchema(enRaw);
		} catch (Exception e) {
			problem = "cozumlenemedi: " + e.Message;
			return null;
		}

	resolved:
		if (!pt.Save().AsSpan().SequenceEqual(ptRaw) || !en.Save().AsSpan().SequenceEqual(enRaw)) {
			problem = "gidis-donus bozuldu";
			return null;
		}
		if (en.RowCount != pt.RowCount || en.RowLength != pt.RowLength) {
			problem = $"EN/PT yapisi farkli (EN {en.RowCount}x{en.RowLength}, PT {pt.RowCount}x{pt.RowLength})";
			return null;
		}

		var columns = usedSchema
			? FromSchema(en, pt, schema!)
			: FromDetection(en, pt);

		return new ResolvedTable {
			Name = tableName, English = en, Localized = pt,
			Columns = columns, UsedSchema = usedSchema,
		};
	}

	/// <summary>
	/// Semadaki metin kolonlarindan gercekten cevrilmis olanlari secer.
	/// Semanin @localized isareti eksik oldugu icin buna guvenmiyoruz;
	/// EN ile PT degerlerini karsilastirip ampirik karar veriyoruz.
	/// </summary>
	private static List<TranslatableColumn> FromSchema(DatFile en, DatFile pt, SchemaTable schema) {
		var result = new List<TranslatableColumn>();
		for (var i = 0; i < schema.Columns.Count; ++i) {
			var c = schema.Columns[i];
			if (c.Array || c.Type != "string")
				continue;
			var off = schema.OffsetOf(i);
			var (cells, chars) = Count(en, pt, off);
			if (cells > 0)
				result.Add(new TranslatableColumn(off, c.Name, cells, chars));
		}
		return result;
	}

	private static List<TranslatableColumn> FromDetection(DatFile en, DatFile pt) {
		var scored = new List<TranslatableColumn>();
		foreach (var (off, _) in pt.DetectStringColumns()) {
			var (cells, chars) = Count(en, pt, off);
			// cok az hucre veya ortalama 2 karakterin altinda => gurultudur
			if (cells < 3 || chars < cells * 2)
				continue;
			scored.Add(new TranslatableColumn(off, null, cells, chars));
		}

		var accepted = new List<TranslatableColumn>();
		foreach (var c in scored.OrderByDescending(c => c.Chars)) {
			// Alanlar 8 bayt; kabul edilmis bir kolonun icinde baskasi baslayamaz
			if (accepted.Any(a => Math.Abs(a.Offset - c.Offset) < 8))
				continue;
			// Kayik okuma, kabul edilmis bir kolonun metinlerini yineler
			if (accepted.Any(a => Overlap(en, c.Offset, a.Offset) > c.Cells * 0.5))
				continue;
			accepted.Add(c);
		}
		return accepted.OrderBy(c => c.Offset).ToList();
	}

	/// <summary>Iki kolonun ayni satirlarda ayni metni verdigi satir sayisi.</summary>
	private static int Overlap(DatFile en, int offA, int offB) {
		var same = 0;
		for (var r = 0; r < en.RowCount; ++r) {
			var s = en.ReadString(r, offA);
			if (string.IsNullOrEmpty(s)) continue;
			if (string.Equals(s, en.ReadString(r, offB), StringComparison.Ordinal))
				++same;
		}
		return same;
	}

	/// <summary>Bir kolonda cevrilmis hucre ve kaynak karakter sayisi.</summary>
	private static (int Cells, long Chars) Count(DatFile en, DatFile pt, int off) {
		var cells = 0; long chars = 0;
		for (var r = 0; r < pt.RowCount; ++r) {
			var e = en.ReadString(r, off);
			var p = pt.ReadString(r, off);
			if (string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p)) continue;
			if (string.Equals(e, p, StringComparison.Ordinal)) continue;
			++cells; chars += e.Length;
		}
		return (cells, chars);
	}
}
