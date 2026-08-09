using System.Buffers.Binary;
using System.Text;

namespace Poe2Tr;

/// <summary>
/// .datc64 tablo dosyasi.
///
/// Bicim:
///   [0..4)                      satir sayisi (u32)
///   [4 .. 4+n*rowLength)        sabit boyutlu satir verisi
///   sonra                       8 bayt 0xBB ayraci
///   sonra                       degisken veri bolumu (metinler, diziler)
///
/// Metin kolonlari sabit bolumde 8 baytlik bir offset tutar; bu offset
/// degisken bolumun basina goredir. Metinler UTF-16LE ve 4 sifir bayt ile biter.
/// </summary>
public sealed class DatFile {
	public static ReadOnlySpan<byte> Magic => [0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB];

	public int RowCount { get; }
	public int RowLength { get; }
	/// <summary>Sabit boyutlu satir verisi (degistirilebilir).</summary>
	public byte[] Fixed { get; }
	/// <summary>
	/// Degisken veri bolumu. 8 baytlik 0xBB ayracini da icerir; cunku metin
	/// offsetleri ayracin sonuna degil <b>basina</b> gore verilmis.
	/// Boylece offsetler dogrudan bu diziye indeks olarak kullanilabiliyor.
	/// </summary>
	public byte[] Variable { get; private set; }

	private DatFile(int rowCount, int rowLength, byte[] fixedData, byte[] variable) {
		RowCount = rowCount;
		RowLength = rowLength;
		Fixed = fixedData;
		Variable = variable;
	}

	/// <param name="expectedRowLength">
	/// Semadan hesaplanan satir uzunlugu. Dosyadan bulunan ile uyusmazsa
	/// sema guncel degildir; <paramref name="mismatch"/> ile bildirilir.
	/// </param>
	public static DatFile Load(byte[] data, int expectedRowLength, out string? mismatch) {
		mismatch = null;
		if (data.Length < 12)
			throw new InvalidDataException("Dosya cok kisa, gecerli bir .datc64 degil");

		var rowCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
		var magicPos = FindMagic(data, rowCount, expectedRowLength);
		if (magicPos < 0)
			throw new InvalidDataException("0xBB ayraci bulunamadi, dosya bozuk olabilir");

		var actualRowLength = rowCount == 0 ? expectedRowLength : (magicPos - 4) / rowCount;
		if (rowCount != 0 && (magicPos - 4) % rowCount != 0)
			throw new InvalidDataException($"Satir uzunlugu tam bolunmuyor (veri {magicPos - 4} bayt / {rowCount} satir)");
		if (rowCount != 0 && actualRowLength != expectedRowLength)
			mismatch = $"satir uzunlugu uyusmuyor: dosya {actualRowLength}, sema {expectedRowLength}";

		var fixedData = data[4..magicPos];
		var variable = data[magicPos..]; // ayrac dahil
		return new DatFile(rowCount, actualRowLength, fixedData, variable);
	}

	/// <summary>
	/// Ayraci bulur. Once semadan beklenen konuma bakar (0xBB sabit veride de
	/// gecebilecegi icin bu daha guvenilir), olmazsa bastan tarar.
	/// </summary>
	private static int FindMagic(byte[] data, int rowCount, int expectedRowLength) {
		var expected = 4 + (long)rowCount * expectedRowLength;
		if (expected >= 4 && expected + 8 <= data.Length
			&& data.AsSpan((int)expected, 8).SequenceEqual(Magic))
			return (int)expected;

		for (var i = 4; i + 8 <= data.Length; ++i)
			if (data.AsSpan(i, 8).SequenceEqual(Magic))
				return i;
		return -1;
	}

	/// <summary>
	/// Sema olmadan yukler. Satir uzunlugunu dosyanin kendisinden cikarir:
	/// tum 0xBB ayrac adaylarini tarar ve satir sayisina tam bolunen ilkini secer.
	/// Semanin guncel olmadigi tablolar icin yedek yol.
	/// </summary>
	public static DatFile LoadWithoutSchema(byte[] data) {
		if (data.Length < 12)
			throw new InvalidDataException("Dosya cok kisa, gecerli bir .datc64 degil");
		var rowCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);

		for (var i = 4; i + 8 <= data.Length; ++i) {
			if (!data.AsSpan(i, 8).SequenceEqual(Magic))
				continue;
			int rowLen;
			if (rowCount == 0) {
				if (i != 4) continue; // satir yoksa ayrac hemen basta olmali
				rowLen = 0;
			} else {
				if ((i - 4) % rowCount != 0) continue;
				rowLen = (i - 4) / rowCount;
				if (rowLen is <= 0 or > 8192) continue;
			}
			return new DatFile(rowCount, rowLen, data[4..i], data[i..]);
		}
		throw new InvalidDataException("Satir sayisina uyan bir 0xBB ayraci bulunamadi");
	}

	/// <summary>
	/// Bir offsetin degisken bolumde gercek bir metin BASLANGICI olup olmadigi.
	/// Metinler 4 sifir bayt ile bittigi icin, bir metnin basi ya ayracin hemen
	/// ardidir ya da oncesinde sonlandirici vardir. Bu, rastgele sayilarin
	/// metin offseti sanilmasini buyuk olcude engelliyor.
	/// </summary>
	private bool IsStringStart(long offset) {
		if (offset < 8 || offset >= Variable.Length || offset % 2 != 0)
			return false;
		if (offset == 8)
			return true;
		if (offset < 12)
			return false;
		var o = (int)offset;
		return Variable[o - 1] == 0 && Variable[o - 2] == 0 && Variable[o - 3] == 0 && Variable[o - 4] == 0;
	}

	/// <summary>
	/// Sema olmadan, satirdaki hangi bayt konumlarinin metin kolonu oldugunu bulur.
	/// Her 8 baytlik pencereyi tum satirlarda deneyip gecerli metin offseti
	/// verenlerin oranina bakar.
	/// </summary>
	/// <param name="minFraction">Kolon sayilmasi icin gereken gecerli satir orani</param>
	/// <returns>(bayt konumu, dolu metin sayisi) ciftleri</returns>
	public List<(int Offset, int NonEmpty)> DetectStringColumns(double minFraction = 0.9) {
		var found = new List<(int Offset, int NonEmpty)>();
		if (RowCount == 0)
			return found;

		for (var off = 0; off + 8 <= RowLength; ++off) {
			var valid = 0;
			var nonEmpty = 0;
			for (var r = 0; r < RowCount; ++r) {
				var v = ReadOffset(r, off);
				if (v == 8) { ++valid; continue; } // bos metin
				if (!IsStringStart(v)) continue;
				var s = ReadString(v);
				if (s is null) continue;
				++valid;
				if (s.Length != 0) ++nonEmpty;
			}
			if (valid >= RowCount * minFraction && nonEmpty > 0)
				found.Add((off, nonEmpty));
		}
		return found;
	}

	/// <summary>Bir hucredeki metin offsetini okur.</summary>
	public long ReadOffset(int row, int fieldOffset)
		=> BinaryPrimitives.ReadInt64LittleEndian(Fixed.AsSpan(row * RowLength + fieldOffset, 8));

	private void WriteOffset(int row, int fieldOffset, long value)
		=> BinaryPrimitives.WriteInt64LittleEndian(Fixed.AsSpan(row * RowLength + fieldOffset, 8), value);

	/// <summary>
	/// Degisken bolumdeki bir metni okur. Offset gecersizse null doner.
	/// </summary>
	public string? ReadString(long offset) {
		if (offset < 0 || offset >= Variable.Length)
			return null;
		var start = (int)offset;
		// UTF-16LE, 4 sifir bayt ile sonlanir
		var i = start;
		while (i + 4 <= Variable.Length) {
			if (Variable[i] == 0 && Variable[i + 1] == 0 && Variable[i + 2] == 0 && Variable[i + 3] == 0)
				return Encoding.Unicode.GetString(Variable, start, i - start);
			i += 2;
		}
		return null; // sonlandirici yok, bozuk
	}

	public string? ReadString(int row, int fieldOffset) => ReadString(ReadOffset(row, fieldOffset));

	/// <summary>
	/// Metinleri degistirir.
	///
	/// Mevcut degisken bolume dokunmaz; yeni metinleri sonuna ekler ve yalnizca
	/// ilgili hucrelerin offsetlerini gunceller. Boylece dizi alanlarinin ve
	/// dokunmadigimiz metinlerin verisi bit duzeyinde korunur.
	/// Ayni metin birden fazla hucrede kullanilirsa tek kopya yazilir.
	/// </summary>
	/// <returns>Yazilan hucre sayisi.</returns>
	public int ApplyStrings(IEnumerable<(int Row, int FieldOffset, string Value)> edits) {
		var appended = new MemoryStream();
		appended.Write(Variable, 0, Variable.Length);
		var pool = new Dictionary<string, long>(StringComparer.Ordinal);
		var count = 0;

		foreach (var (row, fieldOffset, value) in edits) {
			if (row < 0 || row >= RowCount)
				throw new ArgumentOutOfRangeException(nameof(edits), $"Satir {row} tablo disinda (satir sayisi {RowCount})");

			if (!pool.TryGetValue(value, out var offset)) {
				offset = appended.Length;
				var bytes = Encoding.Unicode.GetBytes(value);
				appended.Write(bytes, 0, bytes.Length);
				appended.Write([0, 0, 0, 0], 0, 4);
				pool[value] = offset;
			}
			WriteOffset(row, fieldOffset, offset);
			++count;
		}

		Variable = appended.ToArray();
		return count;
	}

	public byte[] Save() {
		// Variable zaten 0xBB ayraci ile basliyor
		var result = new byte[4 + Fixed.Length + Variable.Length];
		BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)RowCount);
		Fixed.CopyTo(result, 4);
		Variable.CopyTo(result, 4 + Fixed.Length);
		return result;
	}
}
