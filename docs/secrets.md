# Dağıtım sırları ve değişkenleri

Bu belge **hangi ayarın ne işe yaradığını** ve **nasıl değiştirileceğini** anlatır.
Değerlerin kendisi burada **yoktur ve olmamalıdır**.

Sırların tek yaşadığı yer GitHub Secrets'tır. Oradan şifreli saklanır, iş kayıtlarında
otomatik olarak gizlenir ve kimse geri okuyamaz. Aynı değeri bir dokümana, Notion
sayfasına ya da bu depoya kopyalamak, korunması gereken yer sayısını çoğaltmaktan
başka bir şey yapmaz. Bir değeri unuttuysanız geri okumaya çalışmayın — yenisini
üretip değiştirin, aşağıda anlatıldığı gibi.

Hepsinin bulunduğu sayfa:
`https://github.com/mFurkanHiz/medication-tracker/settings/secrets/actions`

---

## Secrets (şifreli, geri okunamaz)

| Ad | Nedir | Nasıl üretilir |
| --- | --- | --- |
| `VPS_SSH_KEY` | Sunucuya bağlanmak için kullanılan **özel** SSH anahtarı. GitHub Actions bununla dağıtım yapar. | `ssh-keygen -t ed25519 -C "claude" -f %USERPROFILE%\claude-mt -N ""` komutunun ürettiği **uzantısız** dosyanın içeriği (`-----BEGIN` satırından `-----END` satırına kadar tamamı). |
| `VPS_KNOWN_HOSTS` | Sunucunun **açık** host anahtarı. Bağlanılan makinenin gerçekten sizin sunucunuz olduğunu doğrular; araya giren bir makineye bağlanmayı imkânsız kılar. | Actions → **Show VPS host key** iş akışını çalıştırın, çıktıdaki bloğu kopyalayın. (Windows'un kendi `ssh-keyscan`'i çoğu zaman sunucunun şifreleme yöntemini tanımadığı için hata verir; bu iş akışı o yüzden var.) |

## Variables (düz metin, herkes görebilir — sır değildir)

| Ad | Değer | Nedir |
| --- | --- | --- |
| `VPS_HOST` | `31.97.53.159` | Sunucunun adresi. |
| `VPS_USER` | `root` | Bağlanılacak kullanıcı. |
| `DEPLOY_ENABLED` | `true` | Dağıtımın açma/kapama anahtarı. `true` değilse dağıtım işi atlanır — `main`'e birleştirme yapılsa bile sunucuya hiçbir şey gitmez. Dağıtımı geçici olarak durdurmak isterseniz bunu `false` yapmak yeterlidir. |
| `PUBLIC_URL` | *(isteğe bağlı)* | Dağıtım sonrası kontrol edilen adres. Boşsa `https://medicationtracker.rapidconfigs.com` kullanılır. |

---

## Anahtarı değiştirme (rotasyon)

Anahtarın sızdığından şüphelenirseniz — bir sohbete, ekran görüntüsüne, e-postaya
düştüyse — hemen değiştirin. Sızmış bir anahtar, sizden başkasının da sunucunuza
girebileceği anlamına gelir.

1. Yeni anahtar üretin:
   ```cmd
   ssh-keygen -t ed25519 -C "claude" -f %USERPROFILE%\claude-mt-yeni -N ""
   ```
2. Açık anahtarı (`.pub`) Hostinger → VPS → SSH keys bölümüne yeni bir kayıt olarak
   ekleyin.
3. GitHub'da `VPS_SSH_KEY` sırrını yeni **özel** anahtarla güncelleyin
   (sırrın yanındaki kalem simgesi → yapıştır → Save).
4. Dağıtımı bir kez çalıştırıp çalıştığını görün (Actions → CI → Run workflow).
5. **Ancak ondan sonra** Hostinger'dan eski anahtarı silin. Önce silerseniz, yenisi
   çalışmazsa elinizde bağlanacak bir şey kalmaz.

Sunucuyu yeniden kurar ya da taşırsanız host anahtarı değişir; o zaman
`VPS_KNOWN_HOSTS`'u **Show VPS host key** ile yeniden alıp güncelleyin. Güncellemezseniz
dağıtım "host key doğrulanamadı" diyerek durur — bu bir arıza değil, tam olarak o
kontrolün işini yapmasıdır.

---

## Bu anahtar ne yapabilir

`root` olarak eklediğiniz için sunucunun tamamına erişir. Sunucuda başka projeleriniz
olduğu için daha dar bir seçenek de var: `docker` grubunda, yalnızca
`/opt/medication-tracker` dizinine sahip bir `deploy` kullanıcısı açıp `VPS_USER`
değişkenine onu yazmak. Dağıtım betiği root istemiyor, yalnızca Docker soketine erişim
istiyor.

Erişimi tamamen kesmek isterseniz: Hostinger'dan anahtarı silin. GitHub'daki sır
kalsa bile artık hiçbir kapıyı açmaz.

---

## Bu ayarları kullanan iş akışları

| İş akışı | Ne yapar | Nasıl çalışır |
| --- | --- | --- |
| **CI** | Testleri koşturur; `main`'de ayrıca imajı derleyip sunucuya dağıtır. | `main`'e birleştirmede kendiliğinden; ya da Actions → CI → *Run workflow*. |
| **Purge care data** | Üretimdeki tüm bakım verisini siler (hesaplar kalır). Önce yedek alır. | Yalnızca elle: Actions → Purge care data → *Run workflow* → `ERASE-CARE-DATA` yazın. |
| **Show VPS host key** | Sunucunun açık host anahtarını yazdırır. | Yalnızca elle. Sunucuya giriş yapmaz, hiçbir sır kullanmaz. |
