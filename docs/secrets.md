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

## Gerçekten sır olanlar (Secret olmak zorunda)

| Ad | Nedir | Nasıl üretilir |
| --- | --- | --- |
| `VPS_SSH_KEY` | Sunucuya bağlanmak için kullanılan **özel** SSH anahtarı. GitHub Actions bununla dağıtım yapar. | `ssh-keygen -t ed25519 -C "claude" -f %USERPROFILE%\claude-mt -N ""` komutunun ürettiği **uzantısız** dosyanın içeriği (`-----BEGIN` satırından `-----END` satırına kadar tamamı). |
| `VPS_KNOWN_HOSTS` | *(isteğe bağlı)* Sunucunun **açık** host anahtarı. Bağlanılan makinenin gerçekten sizin sunucunuz olduğunu doğrular; araya giren bir makineye bağlanmayı imkânsız kılar. | Actions → **Show VPS host key** iş akışını çalıştırın, çıktıdaki bloğu kopyalayın. (Windows'un kendi `ssh-keyscan`'i çoğu zaman sunucunun şifreleme yöntemini tanımadığı için hata verir; bu iş akışı o yüzden var.) |

Host anahtarı aslında bir sır değil — her SSH istemcisi ilk bağlantıda onu gösterir.
Bu yüzden `VPS_KNOWN_HOSTS` zorunlu değil: aynı anahtar depoda
`deploy/known_hosts` dosyasında sabitli. Sır kurulmuşsa o öne geçer, yoksa dosya
kullanılır. İkisi de yoksa dağıtım **ağa güvenmez**: sunucunun o an sunduğu
anahtarı kayda yazar ve durur, hiçbir şey dağıtmadan. Mantığın tamamı
`deploy/authorise-ssh.sh` içinde ve iki iş akışı da aynı betiği çağırır.

Sunucuyu yeniden kurar ya da taşırsanız anahtar değişir ve dağıtım durur — bu bir
arıza değil, tam olarak o kontrolün işini yapmasıdır. O zaman kayıttaki yeni
anahtarı `deploy/known_hosts`'a yazın (ya da sırra koyun).

## Sır olmayan ayarlar (Secret da olur, Variable da)

| Ad | Değer | Nedir |
| --- | --- | --- |
| `VPS_HOST` | `31.97.53.159` | Sunucunun adresi. |
| `VPS_USER` | `root` | Bağlanılacak kullanıcı. |
| `DEPLOY_ENABLED` | `true` | Dağıtımın açma/kapama anahtarı. `true` değilse dağıtım işi atlanır — `main`'e birleştirme yapılsa bile sunucuya hiçbir şey gitmez. Dağıtımı geçici olarak durdurmak isterseniz bunu `false` yapmak yeterlidir. |
| `PUBLIC_URL` | *(isteğe bağlı)* | Dağıtım sonrası kontrol edilen adres. Boşsa `https://medicationtracker.rapidconfigs.com` kullanılır. |

Bu üçü ve `PUBLIC_URL` için iş akışları **her iki yeri de** okur ve önce Variable'a
bakar. Yani hangisine koyduğunuz çalışmayı etkilemez. Tek fark okunabilirlik:
bir değer Secret'ta durduğunda GitHub onu bütün iş kayıtlarında `***` ile
maskeler. `VPS_USER` = `root` bir sır olarak durursa kayıtlarda geçen her "root"
kelimesi maskelenir; `DEPLOY_ENABLED` = `true` bir sır olarak durursa her "true"
maskelenir. Hiçbir şeyi bozmaz, yalnızca kayıtları okumayı zorlaştırır.

`DEPLOY_ENABLED`'ın bir iş kapısı olarak okunabilmesi için ayrı bir `gate` işi
var: GitHub, bir işin `if:` koşulunda `secrets` bağlamını hiç göstermez, bu yüzden
sır olarak duran bir anahtar orada boş okunur ve dağıtım hiç çalışmaz — hem de
sebebini söyleyen bir hata olmadan. `gate` işi değeri bir adımın içinde okur
(adımlar iki bağlamı da görür) ve dağıtım işine düz bir `yes`/`no` verir.

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
| **Purge care data** | Üretimdeki tüm bakım verisini siler (hesaplar kalır). Önce yedek alır. | İki yoldan onaylanır: Actions → Purge care data → *Run workflow* → `ERASE-CARE-DATA` yazmak; ya da `main`'e içinde o satır bulunan `deploy/purge-care-data.request` dosyasını commit etmek. Kendi başına asla çalışmaz. |
| **Show VPS host key** | Sunucunun açık host anahtarını yazdırır. | Yalnızca elle. Sunucuya giriş yapmaz, hiçbir sır kullanmaz. |

`VPS_HOST`'u Secret olarak tuttuysanız host anahtarı satırının başındaki adres
kayıtta `***` olarak görünür. Bu bir arıza değil — kaydın değeri saklamasıdır,
anahtarın kendisi sağlamdır. Bloğu olduğu gibi kopyalayın: dağıtım, bir
`known_hosts` satırını adres alanıyla da, yalnız anahtar hâliyle de, `***` ile
başlamış hâliyle de kabul eder ve bağlanacağı adresi anahtarın önüne kendisi
yazar. Beş yazım biçimiyle sınanmıştır; okunamaz bir değer ise dağıtımı SSH'in
kendi reddine bırakmadan, sebebini söyleyerek durdurur.
