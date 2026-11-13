# Kor

Clevo kasalı dizüstüler (Monster Abra A7 V13.2 üzerinde denendi) için klavye ışığı yöneticisi. Clevo'nun "LED Keyboard Setting" uygulamasının yerine geçer.

Tek bölgeli RGB klavyeler içindir: klavyenin tamamı aynı anda tek renk alır, tuş başına renk donanım olarak desteklenmez.

## Özellikler

- **Efektler:** sabit renk, nefes, gökkuşağı, aurora, tuş vuruşu, renk zarı, yazma ısısı, polis
- **Renk:** hex kodu (`#FF6000`), ton ve doygunluk kaydırıcıları, hazır renkler, kendi kayıtlı renklerin
- **Uygulamaya göre renk:** seçtiğin uygulama öndeyken klavye o uygulamanın rengine döner
- **Canlı önizleme:** pencerede klavyenin o anki rengi gösterilir
- **Tepsi simgesi:** o anki ışık renginde; pencere kapatılınca program tepside çalışır
- Ayarlar kendiliğinden kaydedilir; uykudan dönüşte renk yeniden uygulanır

## Kısayollar

| Tuş | İşlev |
| --- | --- |
| Fn + Numpad − | Parlaklığı azalt |
| Fn + Numpad + | Parlaklığı artır |
| Fn + Numpad * | Işığı aç / kapat |
| Fn + Numpad / | Kor penceresini aç |
| Ctrl + Alt + M | Sonraki efekt |

## Gereksinimler

- Windows 10/11, .NET Framework 4.8 (Windows ile gelir)
- Clevo **Control Center 3.0** ve **Fn hot keys and OSD** kurulu olmalı. Kor ışığı Clevo'nun `InsydeDCHU.dll` arayüzü üzerinden yönetir, Fn tuşlarını da Fn uygulamasının `OutLog` olay günlüğünden okur.
- Clevo'nun "LED Keyboard Setting" uygulaması kaldırılmalı; yoksa iki uygulama birbirinin rengini ezer.

## Derleme

Ek kurulum gerekmez; Windows'un kendi C# derleyicisi kullanılır.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Betik `InsydeDCHU.dll` dosyasını kurulu Clevo paketinden kopyalar, `Kor.ico` simgesini üretir ve `Kor.exe` dosyasını derler. DLL Clevo'ya ait olduğu için depoda yer almaz.

## Dosyalar

- `Kor.cs` — program
- `Arayuz.xaml` — arayüz (exe içine gömülür)
- `build.ps1` — derleme betiği

Ayarlar `%AppData%\Kor\ayar.ini` dosyasında tutulur.
