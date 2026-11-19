# Tutumlu – Akıllı Satın Alma Asistanı

Farklı satıcılardaki ürünleri eşleştirip kategori ve birim fiyat bazında karşılaştırarak kullanıcıların doğru satın alma kararı vermesini destekleyen proje. Staj sürecinde 8 kişilik bir ekip tarafından geliştirilmiştir.

## Özellikler
- Farklı marketlerden ve mağazalardan ürün/fiyat verisi toplama (scraper)
- Ürün adı benzerliği ve Türkçe metin normalizasyonu ile ürün eşleştirme
- Kategori sınıflandırma ve birim fiyat karşılaştırma
- Arama, filtreleme ve favori ürün listesi
- Kullanıcı girişi (JWT ile kimlik doğrulama)
- Web arayüzü (HTML, CSS, JavaScript)

## Teknolojiler
C# / .NET · REST API · SQL Server · Docker · HTML / CSS / JavaScript

## Proje Yapısı
- `PriceAggregator.Api`: REST API ve web arayüzü (Frontend)
- `PriceAggregator.Core`: Eşleştirme, kategorilendirme ve scraper mantığı
- `PriceAggregator.Scraper`: Veri toplama uygulaması
- `PriceAggregator.Worker`: Arka planda periyodik çalışan servis

## Ekip ve Rolüm
8 kişilik ekipte çalıştım. Dış kaynaklardan veri çekme (API entegrasyonu) ve web arayüzü (frontend) geliştirme görevlerini üstlendim; ayrıca projenin diğer bölümlerinde de ekibe destek verdim.

## Notlar
- Gizlilik nedeniyle ayar dosyaları (`appsettings`), veritabanı betikleri ve ortam bilgileri paylaşılmamıştır; proje bu hâliyle doğrudan çalıştırılamaz.
- Proje eğitim ve staj amaçlıdır.
