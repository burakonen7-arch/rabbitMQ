# .NET 10 ile RabbitMQ: iki API, Onion / Clean Architecture

RabbitMQ'yu doğrudan `RabbitMQ.Client` ile öğrenmek için küçük bir uygulama. HTTP uçları controller içindedir. MassTransit, MediatR veya veritabanı gerektirmez. RabbitMQ'yu Docker Compose ile veya işletim sistemi servisi olarak çalıştırabilirsin; API'ler yerelde .NET ile çalışır.

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
  Topology/                        # Ortak RabbitMQ kuyruk, exchange ve binding tanımları
  Orders/
    Orders.Domain/                 # Order ve doğrulama kuralları; bağımlılıksız
    Orders.Application/            # OrderService + IOrderEventPublisher
    Orders.Infrastructure/         # RabbitMQ publisher; ortak Topology projesini kullanır
    Orders.Api/                    # Controller + Program.cs DI kayıtları
  Notifications/
    Notifications.Domain/          # ReceivedNotification; bağımlılıksız
    Notifications.Application/     # Bellek içi NotificationInbox
    Notifications.Infrastructure/  # BackgroundService consumer; ortak Topology projesini kullanır
    Notifications.Api/             # Bildirimleri listeleyen controller
```

Application → Domain ve Contracts; Infrastructure → Application ve Topology; Api → Infrastructure (DI kurulumu için). Domain ve Application'da RabbitMQ veya ASP.NET bağımlılığı yoktur. API'ler birbirine HTTP çağrısı yapmaz. `Program.cs` yapılandırma, dependency injection, Swagger kurulumu ve kök adresin Swagger'a yönlendirilmesini içerir; iş uçları controller'larda tanımlıdır.

Önce `OrdersController`, sonra `OrderService`, `RabbitMqOrderPublisher`, `Topology/RabbitMqTopology`, `OrderCreatedConsumer`, `NotificationInbox` ve `NotificationsController` dosyalarını oku. Kuyruk, exchange ve binding tanımları yalnızca `src/Topology/RabbitMqTopology.cs` içinde bulunur. İki Infrastructure projesi de ortak Topology projesine referans verir ve başlangıçta aynı `DeclareAsync` metodunu çağırır. Böylece tanımlar tek dosyadan yönetilir ve servislerin başlatılma sırası bağımsız kalır. Aynı ayarlarla tekrar tanımlama yeni bir kuyruk oluşturmaz.

## 1. RabbitMQ'yu başlat

### Docker Compose ile kurulum

Windows'ta [Docker Desktop](https://docs.docker.com/desktop/setup/install/windows-install/) kurup başlat. Linux containers / WSL 2 motoru çalışıyor olmalı. Docker kullanırken Windows'a ayrıca Erlang veya RabbitMQ Server kurman gerekmez.

Proje kökünde çalıştır:

```powershell
docker compose up -d --wait --wait-timeout 180
docker compose ps
```

`compose.yaml`, yönetim eklentisi hazır olan `rabbitmq:4-management` imajını kullanır. AMQP `localhost:5672`, yönetim paneli [localhost:15672](http://localhost:15672), kullanıcı/parola `guest / guest`. Portlar yalnızca bu bilgisayardan erişime açılır. API'lerin mevcut `appsettings.json` bağlantı ayarlarıyla uyumludur.

Kuyruk verileri `rabbitmq-data` adlı Compose volume'unda tutulur. Sabit hostname, container yeniden oluşturulduğunda aynı RabbitMQ node kimliğinin kullanılmasını sağlar. Container yeniden başlasa bile henüz ACK edilmemiş kalıcı mesajlar aynı volume ile korunur. Volume, Compose proje adıyla öneklenir.

```powershell
# Logları izle (Ctrl+C yalnızca log takibini durdurur)
docker compose logs -f rabbitmq

# Broker'ı ve container'ı durdur; volume korunur
docker compose down

# Yeniden başlat
docker compose up -d --wait --wait-timeout 180
```

`docker compose down -v` volume'u ve içindeki mesajları siler. Mevcut veriyi korumak için `-v` kullanma. `guest / guest` yerel eğitim içindir. İlk kullanıcı ayarları yalnızca boş veri dizini ilk oluşturulurken uygulanır.

### Alternatif: Erlang + RabbitMQ işletim sistemi servisi

Önce .NET **10 SDK** kur ve `dotnet --version` ile `10.0.x` çıktısını doğrula. `global.json` .NET 10 feature güncellemelerine izin verir. RabbitMQ bir .NET NuGet paketiyle kurulmaz: önce makinede broker çalışır; `RabbitMQ.Client` yalnızca ona bağlanan istemcidir.

#### Windows

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

#### Debian / Ubuntu (systemd)

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

İşletim sistemi servisiyle kurulumda da yönetim arayüzünü `localhost:15672` adresinden açabilirsin. Yerel eğitim hesabı `guest / guest`; native RabbitMQ kurulumunda bu hesap varsayılan olarak yalnızca yerel bağlantılara açıktır. AMQP portu **5672**, yönetim portu **15672**. Aynı portları kullanan native broker ile Docker broker'ını aynı anda çalıştırma.

Bu bulut ortamı systemd/root sağlamadığı için aynı Debian paketleri `/workspace/tools/native` altına açıldı; broker doğrudan Erlang süreci olarak çalışıyor. Buluta özel başlatma talimatları ortam yapılandırmasına kaydedilir. Kendi bilgisayarında yukarıdaki standart servis kurulumunu kullan.

## 2. API'leri çalıştır

API'lerin `appsettings.json` dosyaları ayrı HTTP adresleri tanımlar: Orders.Api `http://localhost:5001`, Notifications.Api `http://localhost:5002`. Bu nedenle Rider'dan doğrudan çalıştırırken de port çakışmaz. Ayar değişikliğinden önce çalışan API'leri durdurup yeniden başlat. Komut satırındaki `--urls` ile bu adresleri değiştirebilirsin.

Tarayıcıda [Orders Swagger](http://localhost:5001/swagger) ve [Notifications Swagger](http://localhost:5002/swagger) arayüzlerini aç. Kök adresler (`/`) otomatik olarak Swagger'a yönlenir. **Try it out → Execute** ile Orders tarafında sipariş gönderebilir, Notifications tarafında işlenen bildirimleri listeleyebilirsin. OpenAPI belgeleri her API'de `/swagger/v1/swagger.json` adresindedir. Yerel öğrenme uygulamasında Swagger, Rider'ın ortam ayarından bağımsız olarak etkindir.

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

### Mesajları kuyrukta görüntüleme

Consumer açıkken de mesajları incelemek için `demo.order-created` kuyruğunu kullan. Bu kuyruk, `orders.events` exchange'ine aynı `order.created` binding key'i ile bağlıdır. Exchange her mesajın bir kopyasını hem normal kuyruğa hem demo kuyruğuna aktarır. Notifications normal kuyruğu tüketip ACK gönderirken demo kuyruğundaki kopya kalır; demo kuyruğunun consumer'ı yoktur. `ConsumerEnabled=true` kalabilir.

API'leri yeniden derleyip başlattıktan sonra Orders Swagger'dan sipariş gönder. [Yönetim panelinde](http://localhost:15672) **Queues and Streams → demo.order-created → Get messages** bölümünü aç, **Ack Mode: Nack message requeue true** seçip **Get Message(s)** düğmesine bas. Böylece içeriği görüntülerken kopyayı kuyruğa geri koyarsın. Önceden yayınlanıp kaldırılmış mesajlar yeni kuyruğa geriye dönük eklenmez.

Demo kuyruğu durable'dır ve en fazla **100 bekleyen mesaj** saklar. Sınır aşılınca en eski kopyalar kaldırılır (`x-max-length: 100`, `x-overflow: drop-head`). Bu kuyruk yayınlanan mesajları gösterir; başarılı işlem geçmişi değildir. Consumer'ın aldığı bozuk mesajın kopyası da demo kuyruğunda kalabilir.

Alternatif olarak normal `notifications.order-created` kuyruğunda mesajları biriktirmek istersen:

Notifications.Api açıkken mesajları kuyrukta biriktirmek için consumer'ı kapatarak başlat:

```powershell
dotnet run --project src/Notifications/Notifications.Api --no-launch-profile -- --urls http://localhost:5002 --RabbitMq:ConsumerEnabled=false
```

Rider'dan çalıştırıyorsan `Notifications.Api/appsettings.json` içindeki `RabbitMq:ConsumerEnabled` değerini `false` yapıp uygulamayı yeniden başlatabilirsin. Varsayılan değer `true`; ayar değişikliği yeniden başlatma gerektirir. Aynı kuyruğa bağlı başka consumer'lar varsa onların da durdurulması gerekir.

RabbitMQ ve Orders.Api çalışırken `POST /api/orders` ile mesaj gönder. [Yönetim panelinde](http://localhost:15672) `guest / guest` ile giriş yap, **Queues and Streams → notifications.order-created** kuyruğunu aç. **Ready** bekleyen, **Unacked** teslim edilmiş ama henüz onaylanmamış mesaj sayısıdır. **Get messages** bölümünde **Ack Mode: Nack message requeue true** seçip **Get Message(s)** düğmesine bas; payload görüntülenir ve mesaj kuyruğa geri konur. Bu işlem tekrar teslim işaretini ve mesaj sırasını etkileyebilir. ACK edilmiş mesajlar kuyrukta geçmiş olarak saklanmaz.

Mesajları işlemek için Notifications.Api'yi durdurup `--RabbitMq:ConsumerEnabled=false` argümanını kaldırarak (ayar dosyasını değiştirdiysen değeri `true` yaparak) yeniden başlat. Ardından `GET /api/notifications` ile işlenen bildirimleri görebilirsin.

`localhost:15672` açılmıyorsa önce RabbitMQ Server'ın kurulu ve çalışıyor olduğunu, ardından `rabbitmq_management` eklentisinin etkin olduğunu kontrol et. .NET API'lerini çalıştırmak RabbitMQ Server'ı başlatmaz; Windows kurulum ve servis komutları yukarıdadır.

### Diğer deneyler

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
