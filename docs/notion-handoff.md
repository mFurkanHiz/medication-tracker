# Notion devir notları — V1 paket-öncelikli yeniden yapım

Notion bağlayıcısı **bu oturumda yetkilendirilmemiş**, bu yüzden Notion'a hiçbir şey
yazılmadı. Bu dosya, panonun hiçbir şeyi yeniden türetmeden güncellenebilmesi için
orada yapılacak değişiklikleri birebir kaydeder.

Bir ajana Notion erişimi vermek için: claude.ai bağlayıcı ayarlarından Notion'u
yetkilendirin ya da etkileşimli bir oturumda `claude mcp` / `/mcp` çalıştırın. O zamana
kadar devir belgesi bu dosyadır.

Notion'daki hiçbir kayıt silinmemeli. Mevcut proje, araştırma sayfası, Sprint 0,
Sprint 1 ve eski Codex görevleri tarihsel kanıttır; oldukları gibi kalır.

## 1. Önce okunacak mevcut kayıtlar (kopyasını oluşturmayın)

- Proje: `https://app.notion.com/p/3d4afec61a3f81fba16cd94cf9c5fdee`
- Araştırma: `https://app.notion.com/p/3d4afec61a3f81928cace707a08da80e`
- Tamamlanan Sprint 0: `https://app.notion.com/p/3d4afec61a3f81afaf46da73db2ccf48`
- Tamamlanan Sprint 1: `https://app.notion.com/p/3d5afec61a3f81dbaa3bdad4bc7c00ec`
- V1 tamamlama görevi: `https://app.notion.com/p/3d9afec61a3f81ebabd2fad7483e3c7e`

Tek bir Medication Tracker projesi olmalı. İkincisini oluşturmayın.

## 2. Proje düzeyinde güncellenecek alanlar

| Alan | Yeni değer |
| --- | --- |
| V1 durumu | **Devam ediyor — web yüzeyi tamamlandı; cihaz kabulü, erişilebilirlik denetimi, demo seed ve mobil yüzeyler açık.** Tamamlanmadı. |
| Kabul tablosu | **31 DONE, 8 PARTIAL, 1 OPEN** (40 satırın) |
| Güncel dal | `main-8ltlkf` |
| Açık PR | [#14](https://github.com/mFurkanHiz/medication-tracker/pull/14) — raporlar, dışa aktarma, sayım |
| Production commit | `371447da76f29e336ae3a00e1cc8864e5613d242` (**PR #14 henüz dağıtılmadı**) |
| Dağıtım preflight'ı | `docs/preflight.md` |
| Mimari kararlar | ADR 0013 (yeniden yapım stratejisi), ADR 0014 (paket-öncelikli envanter) |
| Kapsam otoritesi | `docs/v1-acceptance.md`, 2026-10-02'de uzlaştırıldı |
| Devam noktası | `docs/v1-progress.md` |

Projeye not olarak eklenecek: sahip, envanter alanını ilaç tanımları, fiziksel kutular
ve denetlenebilir tüketim tahsisleri etrafında yeniden tanımladı. Kanıtı eski modele
dayanan kabul satırları yeniden açıldı; zorunlu hiçbir V1 maddesi daraltılmadı veya
ertelenmedi.

## 3. PR #9 kaydının kapatılması

PR #9 (`Add weekday and interval treatment schedules`) **PR #11 tarafından kapsandı**;
reddedilmedi veya terk edilmedi. PR #11, PR #9'un ucundan dallandığı için commit'leri ve
katkı sahipliği korunuyor; tekrarlama kuralları yeniden yazılmak yerine yeni plan
modeline taşındı.

Durumunu *PR #11 tarafından kapsandı* olarak işaretleyin. PR #9'u ayrıca merge etmeyin.

## 4. Oluşturulacak yeni sprint

**Ad:** `Medication Tracker — V1 Domain Rebuild`

**Hedef:** Sahip tarafından tanımlanan paket-öncelikli V1'i — ilaç tanımları, fiziksel
kutular, denetlenebilir tüketim tahsisleri ve düzeltmeleri — API, web, mobil ve
production genelinde, V1 kapsamını daraltmadan teslim etmek.

### Görevler

Durumlar 2026-10-02 itibarıyla gerçeği yansıtır.

| # | Görev | Durum | Nota yapıştırılacak açıklama |
| --- | --- | --- | --- |
| 1 | Mevcut sistem denetimi | **Bitti** | Ürün, migration iskeleti dışında ~1.500 satır C# ve ~650 satır istemci kodu olarak ölçüldü. Korunanlar: CI'ın paketlenmiş migration geçidi, production compose/deploy/nginx/smoke, oturum kimliği, `ExactQuantity`, advisory-lock serileştirmesi, revizyonlu sayımlar, PR #9 tekrarlama kuralları. Reddedilenler: tek ve belirsiz `Medication` varlığı, kapasite/bakiye çifti olan paket, tahsis kaydının yokluğu, düzeltme yolunun yokluğu. ADR 0013'te kayıtlı. Ayrıca `X-Account-Id` başlığının **kimlik doğrulama açığı olmadığı** doğrulandı — kırılgan tasarım, canlı açık değil. |
| 2 | Paket-öncelikli alan tasarımı | **Bitti** | ADR 0014. `MedicationDefinition` → `MedicationPackage` → `AdministrationEvent` → `AdministrationAllocation` → ledger; düzeltmeler eklenen iade + yeniden tahsis olarak. `PackageConsumptionPolicy` birim testli bir alan servisi. Commit `3f4d0cc`. |
| 3 | Veritabanı / migration stratejisi | **Bitti** | Migration elle kuruldu. EF, dolu beş tabloyu düşürüp yeniden oluşturacaktı ve iki `administration_events` yeniden adlandırmasını konuma göre yanlış tahmin etmişti. Oluştur-kopyala-düşür + 13 geri doldurma ile değiştirildi; geçmiş tüketim tahsislere terfi ettirildi. `Down` reddediliyor; geri dönüş yolu doğrulanmış yedek. Commit `a18b8e1`, `d7c0a60`. |
| 4 | API uygulaması | **Bitti** | Katalog, envanter/kutular, planlar, bugün, doz kaydı (otomatik / belirli kutu / kutusuz / takip edilmeyen), tahsis düzeltme, temin politikası ve tahmini, sayımlar, workspace ve aktivite okumaları. Kimlik artık doğrulanmış principal'dan okunuyor. |
| 5 | Alan ve API test kapsamı | **Bitti** | Zorunlu kabul senaryosu uçtan uca, tahsis düzeltmesinin toplamı koruması, eşzamanlılık, idempotency, takip edilmeyen kaynak, ev-arası yetkilendirme, kapasite anlık görüntüsü, ledger üzerinden kullanımdan çıkarma, temin açığı ve sayım revizyonu. |
| 6 | Web arayüzü yeniden yapımı | **Bitti** | Tipli API istemcisi, kesin miktar modülü, eksik çeviri derlemeyi bozacak şekilde tiplenmiş TR/EN sözlükleri, konu başına ekranlar: Bugün (tek dokunuş varsayılan, gelişmiş kaynak seçimi, tahsis düzeltme), kutu ayrıntılı ilaçlar, sürümlü planlar, kişiler, geçmiş, temin ayarları, ödünç verme. Tarayıcıda masaüstü ve 375px'te doğrulandı. 2026-10-02'de raporlar, dışa aktarma ve sayım ekranları eklenerek tamamlandı; anlaşılan web yüzeyinde eksik kalan yok. |
| 7 | Mobil / çevrimdışı yeniden yapım | **Bitti (cihaz kabulü hariç)** | SQLite anlık görüntüsü, istek denenmeden önce commit edilen dayanıklı outbox, serileştirilmiş senkronizasyon, ağsız çalışan Bugün ekranı, gelişmiş kaynak seçimi, cihaz-yerel hatırlatıcılar. Çakışmalar yüzeye çıkarılıyor, sessizce çözülmüyor. **Fiziksel Android cihazda hiçbir şey çalıştırılmadı.** |
| 8 | Yerel hatırlatıcılar | **Bitti (cihaz kabulü hariç)** | Kütüphanenin kendi Android manifest'i okunarak uygulandı: `BOOT_COMPLETED` ve `MY_PACKAGE_REPLACED` alıcıları var, yani yeniden başlatma ve güncelleme kendiliğinden kurtarılıyor. Saat dilimi alıcısı yok, bu yüzden uygulama her öne gelişte yeniden uzlaştırıyor. Açıkça belirtilen sınırlar: `SCHEDULE_EXACT_ALARM` istenmiyor (Doze'da gecikebilir); yalnızca adlandırılmış gün dilimi olan plana hatırlatıcı kurulmuyor (ürün saat uydurmaz); saat dilimi değişiminden sonraki ilk hatırlatıcı eski anda çalabilir. |
| 9 | Raporlar ve dışa aktarma | **Bitti** | Kabul satırları 31 ve 32. Kullanım raporu, administration satırlarını saymak yerine takvimi tarih etkili plan sürümlerinden yeniden oynatıyor; böylece kaydedilmemiş doz ile planın hiç istemediği gün ayırt edilebiliyor. Üç davranış teste bağlandı: bir günü o gün yürürlükte olan sürüm yönetir (en yenisi değil); dönem ortasında durdurulan plan, durmadan önce koyduğu dozları korur; gerektiğinde planı hiç doz koymaz, dolayısıyla asla "kaçırıldı" görünmez. Doz ile cevapladığı slot döneme ayrı yerleştirilir — gece yarısından sonra alınan doz bir önceki günün slotunu cevaplar. Sayılar betimleyicidir: eşik, puan veya öneri yok. Dışa aktarma evin tüm zincirini indirilebilir dosya olarak verir ve hesap kimliğini (adres, parola özeti, oturum, üyelik satırları) **yapısı gereği** dışarıda bırakır; 32. satırın şart koştuğu çapraz ev testi ikinci bir evin verisini ne adıyla ne kimliğiyle içeri sokmuyor. |
| 10 | Sayım arayüzü | **Bitti** | Kabul satırı 18. Yazma yolu zaten kanıtlıydı ama kabul edilmiş bir sayım geri okunamıyordu, bu yüzden istemci hangi kaydın hâlâ düzeltilebilir olduğunu bilemiyordu. `GET /inventory/count-sessions` eklendi: beklenen, bulunan, işaretli fark, sayılan kutunun sıra numarası ve `isRevisable` — yazma yolunun uyguladığı aynı doğrusal zincir kuralı, böylece istemci sunucunun reddedeceği bir düzeltmeyi hiç önermiyor. Ekran varsayılanda ilacın tamamını sayar, kutu kutu mutabakat gelişmiş açılırda. Kabul edilmiş sayım hiç değiştirilmez; düzeltme ona bağlı yeni bir revizyon olarak eklenir. Okunamayan miktar, satırı sessizce atlamak yerine tüm gönderimi reddeder. |
| 11 | Erişilebilirlik denetimi | Açık | Her iki istemcide etiketler, dokunma hedefleri ve odak halkaları var; resmî denetim ve ekran okuyucu geçişi yapılmadı. |
| 12 | Sentetik demo seed | Açık | Güvenli kamuya açık gösterim için tekrarlanabilir seed. Yalnızca hayalî kişiler ve ilaçlar. |
| 13 | Güvenlik sıkılaştırma incelemesi | Açık | Log redaksiyonu doğrulanmadı, mobil depolama incelenmedi, hız sınırlama yalnızca auth uçlarında. |
| 14 | Production migration ve dağıtım | **Otomatikleştirildi, silahlandırılmayı bekliyor** | Yeniden yapım 2026-10-02'de `371447da` ile dağıtıldı. PR #14 (raporlar, dışa aktarma, sayım) `main`'e birleştirildi (`fe4b29a`, CI `37071473724` yeşil) ama **üretimde henüz canlı değil**. Dağıtım artık CI'ın `deploy` işiyle otomatik: her iki test işi geçince artefaktı, dağıtım betiğini ve compose dosyasını aynı commit'ten gönderiyor, SHA-256'yı iki tarafta karşılaştırıyor, host anahtarını doğruluyor. `DEPLOY_ENABLED` değişkeni kurulana kadar iş atlanıyor. Sahibin yapması gerekenler: iki GitHub sırrı, üç değişken ve VPS'e açık anahtar — `docs/preflight.md` 6a. En önemli bulgu: bu sürüm **hiçbir veritabanı göçü içermiyor**; dağıtım imaj değişiminden ibaret, geri alma da öyle. |
| 16 | Diğer projelere dokunmama güvencesi | **Bitti** | VPS'te yirmiden fazla konteyner ve tek nginx arkasında birkaç site var. Betiğin tüm çağrıları denetlendi: her `docker compose` `--project-name medication-tracker` ile kapsamlı, tek durdurma `stop api web`, compose dışındaki her çağrı `medication-tracker-*` kaynağını ya da betiğin kendi oluşturduğu geçici konteyneri adlandırıyor, `prune`/`system`/`network rm`/`volume rm` hiç yok, nginx'e dokunulmuyor. Betik ayrıca kendisine ait olmayan konteynerleri başlamadan kaydedip bitince karşılaştırıyor; biri kaybolmuşsa adını vererek hata veriyor, yeni konteyner başlaması sorun sayılmıyor. Guard'ın 7 senaryosu birim testli, betiğin tamamı Docker mock'lanarak iki uçtan uca provada doğrulandı. |
| 15 | Nihai sahip kabulü | Açık | Sahip `https://medicationtracker.rapidconfigs.com` üzerinde kabul akışını çalıştırıp açıkça onaylar. |

## 5. Her görevde güncel tutulacak alanlar

- **Özet** — ne teslim ettiği, tek satır.
- **Talep / Plan** — `docs/v1-acceptance.md` içindeki hangi kabul satırlarını karşıladığı.
- **Durum** — tablodaki gibi, iş ilerledikçe güncellenir.
- **Bileşen** — API / Web / Mobil / Veritabanı / CI / Dağıtım / Dokümantasyon.
- **Ortam** — Local / CI / Production.
- **Dal, commit, PR** — `main-8ltlkf`, commit SHA, PR #14.
- **CI** — kanıtı üreten run ID.
- **Uygulama notları** — ne yapıldığı ve beklenmedik şekilde ne bulunduğu.
- **Dağıtım durumu** — dağıtılan commit ve tarihi.
- **Açık riskler** — aşağıya bakın.

Her küçük kod değişikliği için Notion görevi açmayın; yalnızca anlamlı yürütme
birimlerini takip edin.

Mimari kararlar yalnızca Notion'a değil, depodaki ADR'lere yazılır. Mimariyi değiştiren
her Notion görevi ilgili ADR'ye bağlantı vermeli, içeriğini tekrarlamamalı.

## 6. Kaydedilecek açık riskler

1. **Hiçbir şey fiziksel Android cihazda çalıştırılmadı.** Çevrimdışı akış ve
   hatırlatıcılar uygulandı ve tiplendi, ama yeniden başlatma, izin iptali, saat dilimi
   değişimi ve Doze davranışı yalnızca gerçek bir telefonda kanıtlanabilir.
2. **Yeniden yapım migration'ı geri alınamaz.** `Down` bilerek hata fırlatıyor. Geri
   dönüş yolu, `deploy-production.sh`'ın aldığı ve `pg_restore -l` ile doğruladığı
   dağıtım öncesi yedektir.
3. **Production, main'in gerisindeydi ve on migration çalıştırıyordu, on bir değil.**
   Schedule migration'ı hiç dağıtılmamıştı, bu yüzden gerçek yükseltme yolu tek geçişte
   10 → 11 → 12 idi. CI artık tam olarak bu yolu production şekilli veriyle test ediyor.
4. **Production, altı adet sıfır miktarlı `acquisition` ledger satırı tutuyor.** Kontrol
   kısıtı başta yalnızca sayım düzeltmelerini muaf tutuyordu; bu satırlarda migration
   çökerdi. Preflight sırasında yakalandı ve düzeltildi.
5. **Migration'lar artık tablo yeniden adlandırıyor.** Dağıtım betiği şema değişmeden
   önce yalnızca bu projenin api ve web konteynerlerini durduruyor — kısa bir kesinti,
   bilinçli bir tercih.
6. ~~**Yerel veritabanı yok.**~~ **Çözüldü (2026-10-02).** .NET SDK ve PostgreSQL
   Ubuntu depolarından kuruldu; tüm paket artık yerelde gerçek PostgreSQL'e karşı
   koşuyor (158 test, 0 atlanmış). Yerel sürüm **16**, CI ve üretim **18**, bu yüzden
   paketlenmiş göç geçidinde yetki hâlâ CI'dadır.
7. **Raporlar, dışa aktarma ve sayım yalnızca web'de.** Mobil istemci bozulmuyor ama
   bu üç ekranı göstermiyor. Kabul satırları 18, 31 ve 32 karşılandı (kullanıcı
   davranışa erişebiliyor), ancak mobil eşlik etmiyor.
8. **Stok raporu ilaç başına bir tahmin sorgusu yapıyor.** Üretimde 11 ilaç var,
   sorun değil; yüzlerce ilaçta toplu hale getirilmeli.
9. **Hatırlatıcılar kesin zamanlı değil.** `SCHEDULE_EXACT_ALARM` istenmiyor; Google Play
   politikası bu izni takvim/alarm uygulamalarıyla sınırlıyor. Sahip kesin zamanlama
   isterse `app.json` içindeki `android.permissions` listesine tek satır eklemek yeterli.

## 7. Eklenecek kanıt bağlantıları

- PR'lar: [#11](https://github.com/mFurkanHiz/medication-tracker/pull/11) (yeniden yapım,
  birleşti), [#14](https://github.com/mFurkanHiz/medication-tracker/pull/14) (raporlar,
  dışa aktarma, sayım — açık)
- Commit'ler: `a75338e` (kararlar), `3f4d0cc` (alan çekirdeği), `a18b8e1` (yeniden yapım),
  `d7c0a60` (sıralama düzeltmeleri), `41ea4aa` (web), `4f3a144` (production migration
  geçidi), `df808eb` (mobil), `aa8b112` (raporlar + dışa aktarma), `38cfa29` (sayım),
  `c5f60c1` (smoke testi ve preflight)
- ADR'ler: `docs/adr/0013-v1-domain-rebuild-strategy.md`,
  `docs/adr/0014-package-first-inventory-model.md`
- Kabul sözleşmesi: `docs/v1-acceptance.md`
- Devam noktası: `docs/v1-progress.md`
- Dağıtım preflight'ı: `docs/preflight.md`
- Dağıtım kaydı: `docs/web-vps-deployment.md`
- CI run `37036017899`: 124 test, 0 başarısız, 0 atlanmış; her iki iş de yeşil.
- Yerel tam kapı (2026-10-02, PR #14 dalı): **158 test, 0 başarısız, 0 atlanmış**
  gerçek PostgreSQL'e karşı; `pnpm lint`, `pnpm typecheck:mobile`, `pnpm build:web`
  temiz. Her iki yeni ekran canlı API'ye karşı tarayıcıda 1280px ve 375px'te,
  TR ve EN olarak sürüldü.
