# Dağıtım öncesi kontrol raporu (preflight)

Hazırlandığı tarih: 2026-10-02
Sürüm: raporlar + dışa aktarma + sayım ekranı
Durum: **`main`'e birleştirildi** (`fe4b29a`, CI koşusu `37071473724` yeşil).
Üretimde **henüz canlı değil** — dağıtım kurulumu 6a'da.

Bu belge, kabul sözleşmesinin 39. satırının istediği **sahip onaylı preflight**tir.
Onay vermeden önce okunacak tek belge budur.

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
| **Dağıtım işi gerçek sunucuya karşı hiç çalışmadı.** | En büyük bilinmeyen bu. Betik mock'lanmış Docker ile baştan sona iki kez prova edildi ve guard'ı izole test edildi, ama SSH/scp adımları yalnızca sözdizimi düzeyinde doğrulandı — bu oturum VPS'e erişemiyor. Bu yüzden `production` ortamına kendinizi *required reviewer* ekleyip **ilk dağıtımı izleyerek onaylamanız** önerilir. Başarısız olursa hiçbir şey dağıtılmaz: aktarım adımı sağlama tutmazsa durur, betik de yedeği doğrulamadan şemaya dokunmaz. |
| Dağıtım anahtarı VPS'e root erişimi verir. | Paylaşımlı sunucuda bu geniş bir yetki. 6a'daki güvenlik notu, `docker` grubunda ayrı bir `deploy` kullanıcısı alternatifini anlatıyor. |
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

## 6. Dağıtım

### 6a. Otomatik dağıtım — bir kereye mahsus kurulum

`main`'e birleştirilince dağıtım artık CI'ın içindeki `deploy` işi tarafından
yapılır. İş üç kapıdan geçmeden çalışmaz: her iki test işi yeşil olacak, yayınlanabilir
bir `main` derlemesi olacak (birleştirme ya da elle tetikleme), ve **siz**
`DEPLOY_ENABLED`'ı `true` yapmış olacaksınız. O kurulmadan iş atlanır, yani
birleştirmeler asla kurulmamış bir dağıtım yüzünden kırmızı görünmez.

Kurulumun **tamamı bundan ibaret**. Sonrasını ben yapıyorum: dağıtımı
`workflow_dispatch` ile tetikliyorum (gereksiz commit atmadan), koşuyu izliyorum,
başarısız olursa düzeltip tekrar deniyorum, ve kanıtı `docs/v1-progress.md`'ye
yazıyorum.

Bu beş adımı ben yapamam: oturumun ağ politikası VPS'i engelliyor ve GitHub sırrı
oluşturan bir araç yok. Anahtarın iki yarısı da erişemediğim yerlere gidiyor —
özel yarısı GitHub sırlarına, açık yarısı sunucuya.

```bash
# 1. Yalnızca dağıtım için bir anahtar çifti üretin (kendi makinenizde)
ssh-keygen -t ed25519 -C "github-actions-deploy" -f medication-tracker-deploy -N ""

# 2. Açık anahtarı VPS'e kurun — Hostinger panelinden de eklenebilir
ssh-copy-id -i medication-tracker-deploy.pub root@31.97.53.159
```

3. GitHub → **Settings → Secrets and variables → Actions**:

| Ad | Secret mi Variable mı | Değer |
| --- | --- | --- |
| `VPS_SSH_KEY` | **Secret** (zorunlu) | `medication-tracker-deploy` dosyasının tamamı (özel anahtar) |
| `VPS_HOST` | ikisi de olur | `31.97.53.159` |
| `VPS_USER` | ikisi de olur | `root` |
| `DEPLOY_ENABLED` | ikisi de olur | `true` |
| `VPS_KNOWN_HOSTS` | **Secret**, isteğe bağlı | Sunucunun açık host anahtarı. Boş bırakılabilir: aynı anahtar depoda `deploy/known_hosts` içinde sabitli. |

Yalnızca ilk satır gerçekten sırdır. Host anahtarı gizli bir veri değil — her SSH
istemcisi ilk bağlantıda onu gösterir — ve depoda sabitli olduğu için onu
yapıştırmanız gerekmiyor. Sabitli bir anahtar da yoksa dağıtım **ağa güvenmez**:
sunucunun sunduğu anahtarı kayda yazıp durur, hiçbir şey dağıtmadan. Anahtarı
yeniden okumak isterseniz: Actions → **Show VPS host key** → *Run workflow*.
(Windows'un kendi `ssh-keyscan`'i çoğu zaman güncel bir OpenSSH sunucusunun
anahtar değişim yöntemini tanımadığı için hata verir; o iş akışı bu yüzden var.)

`VPS_HOST`, `VPS_USER` ve `DEPLOY_ENABLED` sır değildir — bir adres, bir kullanıcı
adı ve bir açma/kapama anahtarı. İş akışları her iki yeri de okur, önce Variable'a
bakar. Variable olarak koyarsanız kayıtlar okunabilir kalır; Secret olarak
koyarsanız GitHub o değerleri bütün kayıtlarda `***` ile maskeler — çalışmayı
engellemez, yalnızca kayıtları okumayı zorlaştırır.

4. İsteğe bağlı ama önerilir: **Settings → Environments → production** altına
   kendinizi *required reviewer* olarak ekleyin. O zaman her dağıtım sizin
   onayınızı bekler — kabul sözleşmesinin 39. satırındaki preflight kapısı
   otomasyona rağmen korunmuş olur.

**Güvenlik notu.** Bu anahtar VPS'e root erişimi verir. Paylaşımlı bir sunucu
olduğu için daha sıkı bir alternatif: `docker` grubunda, yalnızca
`/opt/medication-tracker` sahibi olan ayrı bir `deploy` kullanıcısı açıp
`VPS_USER` değişkenine onu yazmak. Betik root gerektirmiyor, yalnızca Docker
soketine erişim istiyor.

### 6b. Dağıtım işinin yaptıkları

1. Artefaktı indirir (`medication-tracker-web-<sha>`).
2. Host anahtarını doğrulayarak SSH açar — bilinmeyen anahtar **kabul edilmez**,
   bağlantı iptal edilir.
3. Arşivi, dağıtım betiğini ve `compose.production.yml`'ı tam o commit'ten gönderir.
   Böylece eski bir betik yeni bir imajla çalışamaz ve VPS'teki checkout durumuna
   hiç bağımlılık kalmaz.
4. SHA-256'yı aktarımdan önce ve sonra karşılaştırır; tutmazsa hiçbir şey dağıtılmaz.
5. `deploy/deploy-production.sh` çalıştırır.
6. **Genel adresin cevap verdiğini kanıtlar** — hiçbir şey yazmadan: `GET /` → 200 ve
   `GET /api/auth/session` → **401**. İkincisi kritik, çünkü 401 isteğin nginx
   üzerinden gerçekten API'ye ulaştığını gösterir; 200 ya da 502 başka bir sorun
   demektir. Betik yalnızca sunucunun kendi loopback portunu sınıyor, bu adım ise
   dışarıdan görünen hâli.
7. İstenirse tam smoke testini çalıştırır (aşağıya bakın).
8. Özel anahtarı runner'dan siler.

Aynı anda iki dağıtım çalışamaz (`concurrency: production-deploy`) ve çalışan bir
dağıtım yarıda iptal edilmez.

**Elle tetikleme.** Actions → CI → *Run workflow* → `main`. `run_smoke_test` kutusu
işaretlenirse dağıtımdan sonra `deploy/smoke-test.ps1` canlı siteye karşı çalışır.
Varsayılan kapalı, çünkü betik her koşuda canlıda bir sentetik hane oluşturuyor —
davranışı değişen bir sürümde açmaya değer, rutin birleştirmelerde değmez.

### 6c. Diğer sistemlere dokunulmadığının garantisi

VPS'te yirmiden fazla konteyner ve tek nginx arkasında birkaç site var. Betiğin
tamamı Docker mock'lanarak prova edildi ve **tüm** çağrıları denetlendi:

- Her `docker compose` çağrısı `--project-name medication-tracker` ile kapsamlı.
- Tek durdurma komutu `stop api web` — yalnızca bu projenin iki servisi.
- Compose dışındaki her `docker` çağrısı ya `medication-tracker-*` kaynağını ya da
  betiğin kendi oluşturduğu geçici konteyneri adlandırıyor.
- `prune`, `system`, `network rm`, `volume rm`, `restart`: **hiçbiri yok**.
- nginx'e hiç dokunulmuyor.

Buna ek olarak betik artık, kendisine ait olmayan konteynerleri **başlamadan önce
kaydediyor ve bitince karşılaştırıyor**. Başka bir projenin konteyneri kaybolmuşsa
dağıtım adını vererek hata veriyor. Yeni konteyner başlaması sorun sayılmıyor —
başka bir projenin kendi işini yapması bizi ilgilendirmez. Guard'ın yedi senaryosu
izole olarak test edildi, ve betiğin tamamı iki uçtan uca provada (sağlıklı ve
zarar görmüş) doğru davrandı.

### 6d. Elle dağıtım (otomatik olanı kullanmak istemezseniz)

1. `main` CI koşusunun artefaktını indirin (**2 gün** saklanıyor).
2. Sağlamasını alın: `sha256sum medication-tracker-web.tar.gz`
3. Aktarın ve karşılaştırın:
   ```bash
   scp medication-tracker-web.tar.gz \
     root@31.97.53.159:/opt/medication-tracker/.deploy/medication-tracker-web.tar.gz
   ssh root@31.97.53.159 'sha256sum /opt/medication-tracker/.deploy/medication-tracker-web.tar.gz'
   ```
   İki sağlama aynı değilse **durun**.
4. Çalıştırın:
   ```bash
   ssh root@31.97.53.159 'bash /opt/medication-tracker/deploy/deploy-production.sh'
   ```

### 6e. Dağıtım sonrası doğrulama

```powershell
pwsh deploy/smoke-test.ps1
```

`SMOKE PASSED: ... counting with revision and replay, both reports and the export
all verified.` görmelisiniz. Bu test canlı sitede bir sentetik hane oluşturur;
bu yüzden her dağıtımda otomatik çalıştırılmıyor, kararı size bırakılıyor.

Sonra https://medicationtracker.rapidconfigs.com/ üzerinde kendiniz bakın:

- **Sayım** sekmesi: bir ilaca saydığınız miktarı girin, kaydedin, geçmişte görün.
- Aynı sayımı **Düzelt** ile değiştirin; orijinalin değişmediğini ve "daha yeni bir
  revizyon var" olarak işaretlendiğini doğrulayın.
- **Raporlar** sekmesi: sayıların kendi geçmişinizle tutarlı olduğunu görün.
- **JSON dosyası indir** ile dosyayı indirin, içinde e-posta adresinizin
  **geçmediğini** doğrulayın.

Son olarak dağıtılan commit SHA'sını, CI koşu numarasını ve smoke sonucunu
`docs/v1-progress.md` kanıt bölümüne ekleyin.

## 7. Onay

Bu sürüm, V1'in tamamlandığı anlamına **gelmez**. Kabul sözleşmesinde 8 PARTIAL ve
1 OPEN satır duruyor; fiziksel cihaz kabulü, erişilebilirlik denetimi, sentetik demo
tohumu ve mobil yüzeyler açık. 40. satır (sahip kabulü) yalnızca sizin açık onayınızla
kapanır.

Onaylıyorsanız 6. bölümdeki adımları uygulayın.
