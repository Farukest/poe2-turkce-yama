using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Poe2Tr;

/// <summary>
/// Kurulum/güncelleme penceresi.
///
/// İş bir arka plan iş parçacığında koşuyor; arayüze her dokunuş Invoke ile
/// UI iş parçacığına taşınıyor. Sorular da öyle — <see cref="Sor"/> arka
/// plandan çağrılıp UI'da modal pencere açıyor ve cevabı bekliyor.
/// </summary>
public sealed class Pencere : Form, IKurulumArayuzu {
	private static readonly Color Arka = Color.FromArgb(20, 18, 16);
	private static readonly Color Panel = Color.FromArgb(30, 27, 24);
	private static readonly Color Altin = Color.FromArgb(216, 178, 106);
	private static readonly Color Metin = Color.FromArgb(214, 208, 198);
	private static readonly Color Solgun = Color.FromArgb(128, 120, 110);
	private static readonly Color Yesil = Color.FromArgb(126, 190, 122);
	private static readonly Color Sari = Color.FromArgb(226, 186, 94);
	private static readonly Color Kirmizi = Color.FromArgb(214, 106, 96);

	private readonly bool guncelleme;
	/// <summary>
	/// Oyun yolu DÜZENLENEBİLİR bir kutu, salt okunur etiket değil.
	///
	/// Bir kullanıcıda "Gözat" penceresi açılmadı/dondu ve bende yeniden
	/// üretilemedi. Kabuğun klasör seçicisi COM tabanlı; ağ sürücüsü,
	/// bağlantısı kopmuş eşleme ya da antivirüs yüzünden takılabiliyor ve
	/// buna karşı yapabileceğimiz bir şey yok. Yolu elle yapıştırmak o
	/// bağımlılığı tamamen ortadan kaldırıyor.
	/// </summary>
	private readonly TextBox oyunKutusu = new();
	private readonly Label durumEtiket = new();
	private readonly RichTextBox kayit = new();
	private readonly Button eylem = new();
	private readonly Button degistir = new();
	private readonly ProgressBar cubuk = new();
	private string? oyunYolu;
	private bool calisiyor;

	/// <summary>
	/// Kurulum ve güncelleme aynı işlem olduğu için tek program var.
	/// Hangi ismi göstereceğine, bu makinede daha önce kurulmuş olup
	/// olmadığına bakarak kendi karar veriyor.
	/// </summary>
	public Pencere() {
		guncelleme = Kurulum.DahaOnceKuruldu();
		Kur();
		OyunuTespitEt();
	}

	private void Kur() {
		Text = "PoE 2 Türkçe Yama";
		ClientSize = new Size(760, 560);
		MinimumSize = new Size(680, 480);
		BackColor = Arka;
		Font = new Font("Segoe UI", 9F);
		StartPosition = FormStartPosition.CenterScreen;

		// Yerleşim tamamen DOCK ile. Mutlak koordinat + Anchor karışımı, pencere
		// yeniden boyutlanınca alt şeridin altında boyanmamış bir bant bırakıyordu.

		// ---- üst: başlık
		var ustPanel = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Arka };
		var baslik = new Label {
			Text = guncelleme ? "Türkçe Yamayı Yeniden Uygula" : "Türkçe Yamayı Kur",
			ForeColor = Altin, Font = new Font("Segoe UI Semibold", 16F),
			AutoSize = true, Location = new Point(24, 18),
		};
		var altBaslik = new Label {
			Text = guncelleme
				? "Oyun güncellemesinden sonra yamayı geri yükler, yeni metinleri çevirir."
				: "Path of Exile 2'yi Türkçeleştirir. Oyunun kapalı olması gerekir.",
			ForeColor = Solgun, AutoSize = true, Location = new Point(26, 52),
		};
		ustPanel.Controls.AddRange([baslik, altBaslik]);

		// ---- oyun klasörü şeridi
		var kutuDis = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Arka, Padding = new Padding(24, 0, 24, 16) };
		var kutu = new Panel { Dock = DockStyle.Fill, BackColor = Panel };
		var oyunBas = new Label {
			Text = "OYUN KLASÖRÜ  ·  yolu buraya yapıştırabilirsin", ForeColor = Solgun, AutoSize = true,
			Font = new Font("Segoe UI", 7.5F), Location = new Point(12, 9),
		};
		oyunKutusu.ForeColor = Metin;
		oyunKutusu.BackColor = Panel;
		oyunKutusu.BorderStyle = BorderStyle.None;
		oyunKutusu.Font = new Font("Segoe UI", 9.5F);
		oyunKutusu.Location = new Point(12, 27);
		oyunKutusu.Size = new Size(540, 20);
		oyunKutusu.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		oyunKutusu.TextChanged += (_, _) => YolDegisti();

		DugmeBicimle(degistir, "Gözat…", ikincil: true);
		degistir.Size = new Size(104, 30);
		degistir.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		degistir.Location = new Point(kutu.Width - 116, 14);
		degistir.Click += (_, _) => OyunSec();
		kutu.Resize += (_, _) => {
			degistir.Location = new Point(kutu.Width - 116, 14);
			oyunKutusu.Width = kutu.Width - 132;
		};

		kutu.Controls.AddRange([oyunBas, oyunKutusu, degistir]);
		kutuDis.Controls.Add(kutu);

		// ---- alt şerit
		var altPanel = new Panel { Dock = DockStyle.Bottom, Height = 92, BackColor = Arka, Padding = new Padding(24, 14, 24, 18) };

		DugmeBicimle(eylem, guncelleme ? "YENİDEN UYGULA" : "KUR", ikincil: false);
		eylem.Dock = DockStyle.Right;
		eylem.Width = 220;
		eylem.Click += (_, _) => Basla();

		var solAlt = new Panel { Dock = DockStyle.Fill, BackColor = Arka, Padding = new Padding(0, 12, 20, 0) };
		cubuk.Dock = DockStyle.Top;
		cubuk.Height = 6;
		cubuk.Style = ProgressBarStyle.Continuous;
		cubuk.Value = 0;

		durumEtiket.Dock = DockStyle.Top;
		durumEtiket.Height = 26;
		durumEtiket.Padding = new Padding(0, 8, 0, 0);
		durumEtiket.ForeColor = Solgun;
		durumEtiket.Text = "Hazır.";

		solAlt.Controls.Add(durumEtiket);
		solAlt.Controls.Add(cubuk);
		altPanel.Controls.Add(solAlt);
		altPanel.Controls.Add(eylem);

		// ---- kayıt (kalan tüm alan)
		var kayitDis = new Panel { Dock = DockStyle.Fill, BackColor = Arka, Padding = new Padding(24, 0, 24, 0) };
		kayit.Dock = DockStyle.Fill;
		kayit.BackColor = Panel;
		kayit.ForeColor = Metin;
		kayit.BorderStyle = BorderStyle.None;
		kayit.ReadOnly = true;
		kayit.Font = new Font("Consolas", 9F);
		kayit.ScrollBars = RichTextBoxScrollBars.Vertical;
		kayitDis.Controls.Add(kayit);

		// Ekleme SIRASI onemli: Fill en once, sonra Bottom, en son Top'lar.
		Controls.Add(kayitDis);
		Controls.Add(altPanel);
		Controls.Add(kutuDis);
		Controls.Add(ustPanel);
	}

	private static void DugmeBicimle(Button d, string yazi, bool ikincil) {
		d.Text = yazi;
		d.FlatStyle = FlatStyle.Flat;
		d.FlatAppearance.BorderSize = 1;
		d.FlatAppearance.BorderColor = ikincil ? Solgun : Altin;
		d.BackColor = ikincil ? Panel : Color.FromArgb(58, 46, 28);
		d.ForeColor = ikincil ? Metin : Altin;
		d.Font = new Font("Segoe UI Semibold", ikincil ? 8.5F : 10.5F);
		d.Cursor = Cursors.Hand;
	}

	// ------------------------------------------------------------- olaylar

	private void OyunuTespitEt() {
		Yaz("Oyun aranıyor…", KurulumRenk.Solgun);
		var bulundu = Kurulum.OyunuBul(null, new SessizArayuz());
		if (bulundu is not null) {
			oyunKutusu.Text = bulundu;      // YolDegisti tetiklenir
			Yaz("Oyun bulundu.", KurulumRenk.Basarili);
		} else {
			Yaz("Oyun otomatik bulunamadı.", KurulumRenk.Uyari);
			Yaz("Klasör yolunu yukarıdaki kutuya yapıştır, ya da 'Gözat…' kullan.", KurulumRenk.Uyari);
			Yaz(@"Örnek:  D:\SteamLibrary\steamapps\common\Path of Exile 2", KurulumRenk.Solgun);
		}
		if (!Kurulum.OodleHazirMi())
			Yaz("Not: Oodle kütüphanesi ilk çalıştırmada aranacak (bir kez, ~45 sn).", KurulumRenk.Solgun);
	}

	/// <summary>Kutuya her yazıldığında yolu doğrular ve rengiyle geri bildirir.</summary>
	private void YolDegisti() {
		var yol = oyunKutusu.Text.Trim().Trim('"');
		if (Kurulum.Gecerli(yol)) {
			oyunYolu = yol;
			oyunKutusu.ForeColor = Yesil;
		} else {
			oyunYolu = null;
			oyunKutusu.ForeColor = yol.Length == 0 ? Metin : Kirmizi;
		}
	}

	private void OyunSec() {
		try {
			using var d = new FolderBrowserDialog {
				Description = "Path of Exile 2 klasörünü seç",
				ShowNewFolderButton = false,
			};
			// Mevcut yoldan basla: kabuk boylece tum ag konumlarini taramak
			// zorunda kalmiyor, acilma da hizlaniyor.
			if (oyunYolu is not null) d.SelectedPath = oyunYolu;

			if (d.ShowDialog(this) != DialogResult.OK) return;
			oyunKutusu.Text = d.SelectedPath;
			if (!Kurulum.Gecerli(d.SelectedPath))
				Yaz($"Bu klasörde {Kurulum.IndexAltYol} yok — içinde 'Bundles2' olan klasörü seç.", KurulumRenk.Hata);
			else
				Yaz("Oyun klasörü seçildi.", KurulumRenk.Basarili);
		} catch (Exception e) {
			// Kabuk secicisi bazi makinelerde acilmiyor/donuyor. Program bu
			// yuzden kullanilamaz hale gelmemeli - kutuya elle yazmak duruyor.
			Yaz("Klasör seçici açılamadı: " + e.Message, KurulumRenk.Hata);
			Yaz("Yolu yukarıdaki kutuya elle yapıştırabilirsin.", KurulumRenk.Uyari);
		}
	}

	private void Basla() {
		if (calisiyor) return;
		if (oyunYolu is null) {
			MessageBox.Show(this, "Önce oyun klasörünü seç.", "Oyun bulunamadı",
				MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		if (Kurulum.OyunAcikMi()) {
			MessageBox.Show(this, "Path of Exile 2 çalışıyor. Önce oyunu kapat.", "Oyun açık",
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		calisiyor = true;
		eylem.Enabled = degistir.Enabled = false;
		eylem.Text = "ÇALIŞIYOR…";
		cubuk.Style = ProgressBarStyle.Marquee;
		kayit.Clear();

		var yol = oyunYolu;
		var is_ = new Thread(() => {
			Kurulum.Calistir([yol], this);
		}) { IsBackground = true };
		is_.Start();
	}

	// ------------------------------------------------- IKurulumArayuzu

	public void Yaz(string metin, KurulumRenk renk = KurulumRenk.Normal) {
		if (InvokeRequired) { BeginInvoke(() => Yaz(metin, renk)); return; }
		var c = renk switch {
			KurulumRenk.Basarili => Yesil,
			KurulumRenk.Uyari => Sari,
			KurulumRenk.Hata => Kirmizi,
			KurulumRenk.Baslik => Altin,
			KurulumRenk.Solgun => Solgun,
			_ => Metin,
		};
		kayit.SelectionStart = kayit.TextLength;
		kayit.SelectionLength = 0;
		kayit.SelectionColor = c;
		kayit.AppendText(metin + Environment.NewLine);
		kayit.SelectionColor = kayit.ForeColor;
		kayit.ScrollToCaret();
	}

	public bool Sor(string soru) {
		if (InvokeRequired) return (bool)Invoke(() => Sor(soru));
		return MessageBox.Show(this, soru, "Onay",
			MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
	}

	public string? YolIste(string mesaj, bool klasor) {
		if (InvokeRequired) return (string?)Invoke(() => YolIste(mesaj, klasor));
		if (klasor) {
			using var d = new FolderBrowserDialog { Description = mesaj };
			return d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
		}
		using var f = new OpenFileDialog { Title = mesaj, Filter = "oo2core|oo2core*.dll|Tüm dosyalar|*.*" };
		return f.ShowDialog(this) == DialogResult.OK ? f.FileName : null;
	}

	public void Adim(string? baslik) {
		if (baslik is null) return;
		if (InvokeRequired) { BeginInvoke(() => Adim(baslik)); return; }
		Yaz("");
		Yaz("▶ " + baslik, KurulumRenk.Baslik);
		durumEtiket.Text = baslik + "…";
	}

	/// <summary>
	/// Bitiş durumu PENCEREDE gösteriliyor, açılır kutuda değil.
	///
	/// Önce sonuç MessageBox ile bildiriliyordu. Testte kutu hiç görünmedi ve
	/// düğme "ÇALIŞIYOR…" takılı kaldı — BeginInvoke geri çağrısı içinde modal
	/// açmak durum güncellemesini yarıda bırakıyordu. Zaten ekranda satır satır
	/// akan bir günlük varken üstüne bir de kutu çıkarmak gereksizdi.
	/// </summary>
	public void Bitti(bool basarili) {
		if (InvokeRequired) { BeginInvoke(() => Bitti(basarili)); return; }
		try {
			calisiyor = false;
			eylem.Enabled = true;
			degistir.Enabled = true;
			eylem.Text = basarili ? "TEKRAR ÇALIŞTIR" : (guncelleme ? "YENİDEN UYGULA" : "KUR");
			cubuk.Style = ProgressBarStyle.Continuous;
			cubuk.Value = basarili ? 100 : 0;
			durumEtiket.Text = basarili ? "✔  Tamamlandı — oyunu başlatabilirsin." : "✖  Hata ile bitti.";
			durumEtiket.ForeColor = basarili ? Yesil : Kirmizi;
			durumEtiket.Font = new Font("Segoe UI Semibold", 9F);
		} catch (Exception e) {
			// Arayuz guncellemesi patlarsa bile kullanici en azindan gunlukte gorsun
			Yaz("Arayüz güncellenemedi: " + e.Message, KurulumRenk.Uyari);
		}
	}

	protected override void OnFormClosing(FormClosingEventArgs e) {
		if (calisiyor && MessageBox.Show(this,
				"İşlem sürüyor. Şimdi kapatmak oyun dosyalarını yarım bırakabilir.\n\nYine de kapatılsın mı?",
				"Devam ediyor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) {
			e.Cancel = true;
			return;
		}
		base.OnFormClosing(e);
	}

	/// <summary>Otomatik tespit sirasinda soru sormasin diye.</summary>
	private sealed class SessizArayuz : IKurulumArayuzu {
		public void Yaz(string metin, KurulumRenk renk = KurulumRenk.Normal) { }
		public bool Sor(string soru) => false;
		public string? YolIste(string mesaj, bool klasor) => null;
		public void Adim(string? baslik) { }
		public void Bitti(bool basarili) { }
	}
}
