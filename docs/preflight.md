# Dağıtım öncesi kontrol raporu (preflight)

Hazırlandığı tarih: 2026-10-02
Dal: `main-8ltlkf`
Önerilen sürüm: raporlar + dışa aktarma + sayım ekranı

Bu belge, kabul sözleşmesinin 39. satırının istediği **sahip onaylı preflight**tir.
Onay vermeden önce okunacak tek belge budur; aşağıdaki adımlar onaysız uygulanmaz.

---

## 1. Bu sürümde ne değişiyor

Üç kullanıcı yüzeyi ekleniyor. Hiçbiri mevcut bir davranışı değiştirmiyor.

| Kabul satırı | Ne geldi |
| --- | --- |
| 31 — Raporlar | Ev bazlı **kullanım özeti** (planlanan / planına göre alınan / kayıtsız / atlanan / kısmi / ek doz) ve **stok özeti** (kalan, kutu sayısı, bitiş tahmini, yenileme boşluğu). |
| 32 — Dışa aktarma | `GET /export` — evin tüm geçmişini indirilebilir JSON dosyası olarak verir. |
| 18 — Sayım | Varsayılanda ilacın tamamını sayma, gelişmişte kutu kutu mutabakat, ve kabul edilmiş bir sayımı **yeni revizyon** olarak düzeltme. |
| 30 — Web yönetimi | Yukarıdakilerle birlikte anlaşılan web yüzeyinde eksik kalan bir şey yok. |

Kabul tablosu bu sürümle **31 DONE, 8 PARTIAL, 1 OPEN** oluyor (önceki: 28/9/3).

### Yeni API uçları

| Uç | Tür | Not |
| --- | --- | --- |
| `GET /api/households/{id}/reports/adherence` | okuma | Dönem ve saat dilimi parametreli |
| `GET /api/households/{id}/reports/inventory` | okuma | — |
| `GET /api/households/{id}/export` | okuma | Dosya indirme |
| `GET /api/households/{id}/inventory/count-sessions` | okuma | Sayımları geri okur |

Yazan tek yol olan sayım kaydetme ve revizyon uçları **zaten canlıdaydı**; bu sürüm
onlara yalnızca bir istemci yüzeyi ve bir okuma ucu ekliyor.

---

## 2. En önemli madde: **veritabanı göçü yok**

```
git diff --stat origin/main...HEAD -- apps/api/Persistence/Migrations/
→ boş
```

Bu sürüm **şema değişikliği içermiyor**. Eklenen her şey mevcut tablolar üzerinde
okuma yapıyor. Dolayısıyla:

- Önceki dağıtımın asıl riski olan tablo yeniden adlandırma / kopyala-sil zinciri
  **bu sefer yok**.
- `deploy-production.sh` yine `migrations.sql` uygular, ama içerik aynı olduğu için
  bu bir **no-op**'tur (paketlenmiş SQL idempotent; CI onu iki kez üst üste
  uyguluyor ve doğruluyor).
- Dağıtım pratikte **imaj değişimi**dir.

Bu, geri alma maliyetini de düşürür: şema geri alınacak bir şey değişmediği için
rollback, önceki imaja dönmekten ibarettir.

---

## 3. Kanıt

### Testler — 158 test, 0 başarısız, 0 atlanmış

Tamamı yerelde gerçek PostgreSQL'e karşı koştu. Bu sürümle gelen 15 yeni test:

- **Kullanım raporu matematiği** (`AdherenceReportTests`, 22 test): takvim yeniden
  oynatma, tarih etkili sürüm seçimi, durdurulmuş planın geçmişini koruması,
  gerektiğinde planının asla "kaçırıldı" sayılmaması, yaz saati geçişinde gün
  başına tek doz, aynı slotun iki kez kaydedilmesinin tek sayılması.
- **Rapor ve export API'si** (`ReportApiTests`, 7 test): uçtan uca sayımlar, dönem
  dışı dozun içeri sızmaması, geçersiz dönem ve bilinmeyen saat diliminin
  reddi, **çapraz ev yetkilendirmesi**, ve export'un ikinci bir evin verisini
  ne adıyla ne kimliğiyle taşımaması + e-posta/parola özeti/oturum jetonu
  içermemesi.
- **Sayım okuma** (`InventoryCountApiTests`, 5 test): kabul edilmiş sayımın
  beklenen/bulunan/fark ile geri okunması, revize edilen sayımın düzeltilebilir
  olmaktan çıkması, kutu bazlı sayımın doğru kutuya yazılması, çapraz ev reddi.

### Tarayıcı doğrulaması

Canlı API'ye karşı gerçek tarayıcıda sürüldü (1280px ve 375px, TR ve EN):

- **Raporlar**: 21 planlanan, 12 planına göre alınan, 5 kayıtsız, %57 (12/21) —
  tohumlanan geçmişle birebir. Gerektiğinde ilacı "Planlanmış doz yok" gösterdi,
  uydurma kaçırma üretmedi. Export düğmesi gerçek 39 KB dosya indirdi.
- **Sayım**: okunamayan miktar yazıya dökülmeden reddedildi; 38 → 35 kaydedildi;
  36'ya düzeltildi (2. revizyon) ve **orijinal kendi −3'ünü göstermeye devam etti**,
  "daha yeni revizyon var" olarak işaretlendi; Kutu 1 18 → 19 ve Kutu 2 20 → 18
  mutabakatı yapıldı; ev toplamı 37'ye kadar tutarlı ilerledi.
- Telefon genişliğinde **yatay taşma 0 piksel**.
- Dil değiştirmede çevrilmemiş tek dize kalmadı.

### Dört kalite kapısı

```
dotnet test MedicationTracker.slnx   → 158 geçti
pnpm lint                            → temiz
pnpm typecheck:mobile                → temiz
pnpm build:web                       → başarılı
```

### Smoke testi genişletildi

`deploy/smoke-test.ps1` artık bu sürümün eklediği üç yüzeyi de deniyor: sayım +
revizyon + tekrar oynatma (idempotency), iki rapor, ve export'un hem içeriğini
hem de **sızıntı yapmadığını**. Bu konteynerde PowerShell olmadığı için betiğin
sözdizimi çalıştırılamadı; ancak **iddia ettiği tüm sayılar** aynı çağrı dizisi
Node ile birebir tekrarlanarak gerçek API'ye karşı doğrulandı ve tamamı tuttu.

---

## 4. Riskler ve bilinmeyenler

Dürüst liste. Hiçbiri dağıtımı engellemiyor, ama bilerek onaylayın.

| Risk | Değerlendirme |
| --- | --- |
| **CI bu dalda hiç koşmadı.** | CI yalnızca `main` push'unda ve PR'da tetikleniyor. Dağıtılabilir imaj artefaktı da **sadece `main` push'unda** üretiliyor. Yani merge etmeden dağıtılacak imaj yok. Aşağıdaki adımlar bunu çözüyor. |
| Yerel PostgreSQL **16**, CI ve üretim **18**. | Tüm testler 16'da koştu. Sürüme özgü bir şey kullanılmıyor, ama paketlenmiş göç kapısında yetki yine CI'dadır. |
| Export dosya boyutu | Her koleksiyon 20.000 satırla sınırlı; aşılırsa `truncatedCollections` içinde adı geçer. Üretimde en büyük tablo 55 satır, yani mesafe çok. |
| Rapor sorgu maliyeti | Stok raporu ilaç başına bir tahmin sorgusu yapıyor. Üretimde 11 ilaç var; sorun değil. Yüzlerce ilaçta toplu hale getirilmeli. |
| Mobil | Bu üç yüzey **yalnızca web'de**. Mobil istemci bozulmuyor, sadece yeni ekranları göstermiyor. |
| Fiziksel cihaz kabulü (28, 29) | Hâlâ açık; sizin Android telefonunuzu gerektiriyor. |

---

## 5. Geri alma planı

1. `deploy-production.sh` çalışmadan önce çalışan imajları `:previous` etiketiyle
   sabitliyor. Geri dönüş:
   ```bash
   cd /opt/medication-tracker
   docker tag medication-tracker-web:previous medication-tracker-web:local
   docker tag medication-tracker-api:previous medication-tracker-api:local
   docker compose --project-name medication-tracker --env-file .env.production \
     -f compose.production.yml up -d api web
   ```
2. Şema değişmediği için veritabanını geri almaya **gerek yok**.
3. Yine de betik her çalıştırmada `.deploy/backups/pre-migration-<zaman>.dump`
   yedeğini alıyor ve okunabilirliğini doğruluyor.

---

## 6. Dağıtım adımları

Bu adımları **siz** uygulamalısınız. Bu oturum uygulayamaz: ortamın ağ politikası
hem VPS'in SSH portunu hem de `medicationtracker.rapidconfigs.com` adresini
engelliyor, ve oturumda `.env.production`, SSH anahtarı veya imaj artefaktı yok.

1. **PR'ı birleştirin.**
   [PR #14](https://github.com/mFurkanHiz/medication-tracker/pull/14) —
   `main-8ltlkf` → `main`. CI bu PR üzerinde tam kapıyı koşturur
   (API testleri + web/mobil kontrolleri + her iki Docker derlemesi + paketlenmiş
   göç geçidi). **Yeşil olduğunu görmeden birleştirmeyin.**

2. **`main` CI koşusunun bitmesini bekleyin.** Artefakt adı:
   `medication-tracker-web-<sha>` → içinde `medication-tracker-web.tar.gz`.
   Saklama süresi **2 gün**, geciktirmeyin.

3. **Artefaktı indirin ve sağlamasını alın.**
   ```bash
   # indirildikten sonra
   sha256sum medication-tracker-web.tar.gz
   ```

4. **VPS'e aktarın ve sağlamayı karşılaştırın.**
   ```bash
   scp medication-tracker-web.tar.gz \
     root@31.97.53.159:/opt/medication-tracker/.deploy/medication-tracker-web.tar.gz
   ssh root@31.97.53.159 'sha256sum /opt/medication-tracker/.deploy/medication-tracker-web.tar.gz'
   ```
   İki sağlama **aynı değilse durun**.

5. **Dağıtın.**
   ```bash
   ssh root@31.97.53.159 'bash /opt/medication-tracker/deploy/deploy-production.sh'
   ```
   Betik sırasıyla: eski imajları `:previous` etiketler, veritabanını açar, yedek
   alır ve yedeğin okunabilirliğini doğrular, yalnızca bu projenin api/web
   konteynerlerini durdurur, `migrations.sql` uygular (bu sürümde no-op), konteynerleri
   geri açar, API hazırlık kontrolünü bekler ve siteyi `127.0.0.1:3022` üzerinden sınar.

6. **Dağıtım sonrası doğrulama.**
   ```powershell
   pwsh deploy/smoke-test.ps1
   ```
   `SMOKE PASSED: ... counting with revision and replay, both reports and the export
   all verified.` görmelisiniz.

7. **Kendi gözünüzle bakın.** https://medicationtracker.rapidconfigs.com/
   - **Sayım** sekmesi: bir ilaca saydığınız miktarı girin, kaydedin, geçmişte görün.
   - Aynı sayımı **Düzelt** ile değiştirin; orijinalin değişmediğini ve
     "daha yeni bir revizyon var" olarak işaretlendiğini doğrulayın.
   - **Raporlar** sekmesi: sayıların kendi geçmişinizle tutarlı olduğunu görün.
   - **JSON dosyası indir** düğmesiyle dosyayı indirin, içinde e-posta adresinizin
     **geçmediğini** doğrulayın.

8. **Kaydedin.** Dağıtılan commit SHA'sını, CI koşu numarasını, imaj arşivinin
   SHA-256'sını ve smoke sonucunu `docs/v1-progress.md` kanıt bölümüne ekleyin.

---

## 7. Onay

Bu sürüm, V1'in tamamlandığı anlamına **gelmez**. Kabul sözleşmesinde 8 PARTIAL ve
1 OPEN satır duruyor; fiziksel cihaz kabulü, erişilebilirlik denetimi, sentetik demo
tohumu ve mobil yüzeyler açık. 40. satır (sahip kabulü) yalnızca sizin açık onayınızla
kapanır.

Onaylıyorsanız 6. bölümdeki adımları uygulayın.
