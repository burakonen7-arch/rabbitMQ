# .NET 10 ile RabbitMQ: iki API, Onion / Clean Architecture

RabbitMQ'yu doğrudan `RabbitMQ.Client` ile öğrenmek için küçük bir uygulama. HTTP uçları controller içindedir. MassTransit, MediatR, Docker veya veritabanı gerektirmez.

- **Orders.Api (5001):** `POST /api/orders` sipariş mesajını yayınlar. Broker onayından sonra `202 Accepted` döner.
- **Notifications.Api (5002):** arka plandaki consumer mesajı işler; `GET /api/notifications` alınan bildirimleri listeler.

```text
POST /api/orders
       ↓
OrdersController → OrderService → IOrderEventPublisher
                                        ↓
                              RabbitMqOrderPublisher
                                        ↓
                exchange: orders.events (direct)
                routing key: order.created
                                        ↓
                queue: notifications.order-created
                                        ↓
                OrderCreatedConsumer → NotificationInbox → ACK
                                                ↑
                                   GET /api/notifications

Geçersiz mesaj → Reject(requeue: false)
              → exchange: orders.dead-letter
              → queue: notifications.failed
```

## Katmanlar ve okuma sırası

Her servisin kendi Domain, Application, Infrastructure ve Api projesi var. Onion ve Clean Architecture'ın ortak bağımlılık kuralı uygulanır: iç katman dış katmanı tanımaz.

```text
src/
  Contracts/                       # Ortak OrderCreated mesaj sözleşmesi
  Orders/
    Orders.Domain/                 # Order ve doğrulama kuralları; bağımlılıksız
    Orders.Application/            # OrderService + IOrderEventPublisher
    Orders.Infrastructure/         # RabbitMQ publisher ve topoloji
    Orders.Api/                    # Controller + Program.cs DI kayıtları
  Notifications/
    Notifications.Domain/          # ReceivedNotification; bağımlılıksız
    Notifications.Application/     # Bellek içi NotificationInbox
    Notifications.Infrastructure/  # BackgroundService consumer ve topoloji
    Notifications.Api/             # Bildirimleri listeleyen controller
```

Application → Domain ve Contracts; Infrastructure → Application; Api → Infrastructure (DI kurulumu için). Domain ve Application'da RabbitMQ veya ASP.NET bağımlılığı yoktur. API'ler birbirine HTTP çağrısı yapmaz. `Program.cs` sadece yapılandırma ve dependency injection içerir; minimal API endpoint'i yoktur.

Önce `OrdersController`, sonra `OrderService`, `RabbitMqOrderPublisher`, `RabbitMqTopology`, `OrderCreatedConsumer`, `NotificationInbox` ve `NotificationsController` dosyalarını oku. Sadelik için iki Infrastructure projesindeki küçük topoloji tanımı aynıdır; isimleri/ayarları değiştirirken ikisini de güncelle.

## 1. Geleneksel kurulum: Erlang + RabbitMQ işletim sistemi servisi

Önce .NET **10 SDK** kur ve `dotnet --version` ile `10.0.x` çıktısını doğrula. `global.json` .NET 10 feature güncellemelerine izin verir. RabbitMQ bir .NET NuGet paketiyle kurulmaz: önce makinede broker çalışır; `RabbitMQ.Client` yalnızca ona bağlanan istemcidir.

### Windows

1. RabbitMQ'nun [Erlang uyumluluk tablosundan](https://www.rabbitmq.com/docs/which-erlang) uyumlu bir Erlang/OTP sürümü seç ve 64 bit Erlang kurulumunu tamamla.
2. [Resmi Windows kurulum rehberindeki](https://www.rabbitmq.com/docs/install-windows) RabbitMQ Server yükleyicisini çalıştır. RabbitMQ Windows servisi olarak kurulur. Gerekirse `ERLANG_HOME` Erlang kurulum dizinini göstermelidir.
3. Başlat menüsünden **RabbitMQ Command Prompt (sbin dir)** penceresini yönetici olarak aç:

```bat
rabbitmq-plugins.bat enable rabbitmq_management
rabbitmq-service.bat start
rabbitmq-diagnostics.bat ping
rabbitmq-diagnostics.bat check_running
rabbitmq-diagnostics.bat check_local_alarms
```

Servis zaten çalışıyorsa tekrar başlatmak gerekmez. `rabbitmqctl` cookie/erişim hatası verirse resmi rehberin Erlang cookie bölümünü kontrol et; servis hesabı ile komut satırı hesabının cookie dosyaları eşleşmelidir.

### Debian / Ubuntu (systemd)

Dağıtım paketleriyle doğrudan kurulum:

```bash
sudo apt-get update
sudo apt-get install -y rabbitmq-server
sudo rabbitmq-plugins enable rabbitmq_management
sudo systemctl enable --now rabbitmq-server
sudo rabbitmq-diagnostics ping
sudo rabbitmq-diagnostics check_running
sudo rabbitmq-diagnostics check_local_alarms
```

Dağıtımın paketi eski olabilir. Bu bulut makinesinde Debian paketinden **RabbitMQ 4.0.5 + Erlang 27.3.4.1** ile doğrulandı. Yeni bir kalıcı kurulumda desteklenen sürüm için [resmi Debian/Ubuntu kurulum rehberindeki](https://www.rabbitmq.com/docs/install-debian) imzalı RabbitMQ ve Erlang depolarını kullan. İmza/TLS doğrulamasını kapatma.

Yönetim arayüzünü kendi bilgisayarında `localhost:15672` adresinden açabilirsin. Yerel eğitim hesabı `guest / guest`; bu hesap varsayılan olarak yalnızca yerel bağlantılara açıktır. AMQP portu **5672**, yönetim portu **15672**. RabbitMQ burada Docker içinde çalışmaz.

Bu bulut ortamı systemd/root sağlamadığı için aynı Debian paketleri `/workspace/tools/native` altına açıldı; broker doğrudan Erlang süreci olarak çalışıyor. Buluta özel başlatma talimatları ortam yapılandırmasına kaydedilir. Kendi bilgisayarında yukarıdaki standart servis kurulumunu kullan.

## 2. API'leri çalıştır

Solution kökünde:

```bash
dotnet restore RabbitMqLearning.sln --locked-mode
dotnet build RabbitMqLearning.sln --no-restore
```

RabbitMQ hazır olduktan sonra iki ayrı terminal aç:

```bash
# Terminal 1
dotnet run --project src/Orders/Orders.Api --no-launch-profile -- --urls http://localhost:5001
```

```bash
# Terminal 2
dotnet run --project src/Notifications/Notifications.Api --no-launch-profile -- --urls http://localhost:5002
```

Linux/macOS alternatifleri: `bash scripts/run-service.sh orders` ve `bash scripts/run-service.sh notifications`. Bu yardımcılar 127.0.0.1'e bağlanır. Servis ilk açılışta broker'a erişemiyorsa kapanır; önce RabbitMQ'yu başlatıp sonra API'yi tekrar çalıştır. Kurulmuş bağlantının kesilmesi için istemcinin otomatik bağlantı/topoloji kurtarması varsayılan olarak açıktır.

Bağlantı ayarları her API'nin `appsettings.json` dosyasındaki `RabbitMq` bölümündedir. Varsayılan `localhost:5672`, virtual host `/`, kullanıcı/parola `guest` eğitim içindir. Farklı bir yerel kullanıcı için environment değişkenleriyle `RabbitMq__UserName`, `RabbitMq__Password`, `RabbitMq__HostName`, `RabbitMq__Port`, `RabbitMq__VirtualHost` ayarlarını ezebilirsin; gerçek parolanı Git'e ekleme.

## 3. İlk mesajını gönder

`requests.http` dosyasını IDE'nin HTTP istemcisinde kullan veya terminalden:

```bash
curl -i -X POST http://localhost:5001/api/orders \
  -H 'Content-Type: application/json' \
  -d '{"product":"Mekanik klavye","quantity":2}'

curl http://localhost:5002/api/notifications
```

`202` yanıtında `id`, `product`, `quantity`, `createdAt` bulunur. Kısa süre sonra GET yanıtında aynı `orderId` ile bildirim görülür. `202`, broker'ın mesajı kabul ettiğini gösterir; consumer'ın işlemi bitirdiğini garanti etmez. Broker'a gönderim başarısız olursa `503`, geçersiz istek için `400` döner. Zaman aşımı/bağlantı kopması sırasında gönderim sonucu belirsiz olabilir; HTTP isteğini tekrar denemek yeni sipariş/EventId oluşturur.

## 4. RabbitMQ kavramlarını deneyerek öğren

1. **Exchange / routing key / queue:** Yönetim arayüzünde `orders.events` exchange'inin `order.created` anahtarıyla `notifications.order-created` kuyruğuna bağlandığını gör. Producer doğrudan kuyruk adını hedeflemez.
2. **Consumer kapalıyken biriktirme:** Notifications.Api terminalinde Ctrl+C yap; Orders.Api üzerinden birkaç sipariş gönder. Kuyrukta **Ready** sayısı yükselir. Notifications.Api'yi yeniden aç; Ready düşer ve GET'te bekleyen mesajlar görünür. API'ler birbirinden bağımsızdır.
3. **Manual ACK / prefetch:** Consumer `autoAck: false`, `prefetchCount: 1` kullanır. İşlem bittiğinde ACK gönderir. ACK gelmeden bağlantı koparsa mesaj tekrar teslim edilebilir. Hızlı işlemede Unacked sayısını gözle yakalayamayabilirsin; breakpoint ile `BasicAckAsync` öncesinde durdurabilirsin.
4. **Durable / persistent:** Consumer kapalıyken mesaj gönder, broker'ı normal servis komutuyla durdurup yeniden başlat. Kuyruk durable, mesaj persistent olduğu için aynı veri diziniyle bekleyen mesajlar korunur. Sonra consumer'ı aç. `rabbitmqctl reset` kullanma; bu broker verisini siler.
5. **Publisher confirms / mandatory:** Publisher uzun ömürlü tek bağlantı, her HTTP isteğinde ayrı channel kullanır. Confirm broker kabulünü; `mandatory: true` ise mesajın bir kuyruğa yönlendirilebilmesini denetler. Bunlar consumer ACK'inden farklıdır.
6. **Dead-letter:** Yönetim arayüzünde `orders.events` → Publish message bölümünde routing key olarak `order.created`, payload olarak `bu-json-degil` gir. Consumer bu mesajı `requeue: false` ile reddeder; `notifications.failed` kuyruğuna gider. Mesajın `x-death` header'ını incele. Bu örnekte otomatik retry yoktur; sonsuz tekrar döngüsü oluşturmaz.
7. **Tekrar teslim:** Aynı `EventId` ile iki mesaj gönderildiğinde NotificationInbox süreç içinde tek kayıt tutar. Bu özellik broker'ın exactly-once teslim yaptığı anlamına gelmez.

Queue özelliklerini sonradan farklı değerlerle tanımlamak `PRECONDITION_FAILED` verir. Öğrenme ortamında yalnızca ilgili kuyruğun içeriğini kaybetmeyi kabul ediyorsan silip yeniden oluştur; gerçek veriyi rastgele temizleme. Kullanıcı/virtual host değişirse o kullanıcının configure/write/read izinlerini kontrol et.

## 5. Otomatik işlevsel kontrol

İki API, RabbitMQ ve management eklentisi çalışırken (Python 3 gerekir):

```bash
python3 scripts/smoke-test.py
```

Dört senaryo gerçek servislerde doğrulanır: geçersiz HTTP istekleri, uçtan uca teslim, aynı EventId için tek kayıt ve bozuk JSON'un dead-letter'a gitmesi. Test eğitim verisi oluşturur; bildirimleri ve hatalı mesajı incelemen için bırakır. Tekrar çalıştırılabilir. Farklı adres/kullanıcı için script başındaki environment değişkenlerini kullan. Test varsayılan `/` virtual host'unu kullanır; farklı virtual host için management API yollarını da uyarlaman gerekir.

## Bilinçli sınırlar

Bildirim deposu ve tekrar teslim tespiti bellektedir; Notifications.Api yeniden başlayınca silinir. ACK edilmiş mesaj broker'dan kaldırılmıştır ve bildirim deposunu yeniden doldurmaz. Kalıcı iş verisi, transactional outbox, kalıcı idempotency deposu, retry politikası, kimlik doğrulama ve TLS üretim senaryosu için ayrıca tasarlanmalıdır. Bu örnek tek publisher/consumer akışını anlaşılır tutar; birden fazla consumer aynı kuyruktaki mesajları paylaşır, her API örneğinin bellek içi listesi farklı olur.
