# TRWhisper 2.0.1 — Güvenlik Yaması & Kararlılık Sürümü / Security Patch & Hardening 🛡️

TRWhisper 2.0.1, açık kaynaklı ve yerel öncelikli mimarimizi daha da güçlendiren, gizlilik ve güvenlik açıklarını kapatan kapsamlı bir güvenlik yaması ve kararlılık sürümüdür. 2.0.0 kullanan tüm kullanıcıların bu sürüme güncellemeleri önemle tavsiye edilir.

---

## 🇹🇷 Türkçe Sürüm Notları

### 🛡️ 2.0.1 ile Gelen Başlıca Güvenlik İyileştirmeleri ve Düzeltmeler

1. **🔒 PII & Transkript Gizliliği Koruması**:
   - Genel ayarlara `EnableHistoryLogging` (Dikte Geçmişini Günlükle) seçeneği eklendi. Kullanıcılar diskte `.md` dosyası olarak transkript kaydı tutulmasını diledikleri zaman tamamen kapatabilir.
   - Uygulama içi teşhis loglarından (`trwhisper.log`) tam konuşma metinleri temizlendi; loglarda yalnızca işlem süresi ve karakter uzunluğu gibi operasyonel metrikler tutuluyor.
   - Geçici ses dosyaları için GUID tabanlı dinamik dosya isimlendirmesi (`trwhisper_{Guid}.wav`) ve başlangıçta yetim dosya temizliği getirilerek çoklu kullanıcı veya süreç çakışmaları engellendi.

2. **🔑 API Anahtarı ve Ağ Güvenliği Sertleştirmesi**:
   - Google Gemini API çağrılarında API anahtarının URL sorgu parametresinde (`?key=...`) gönderilmesi yerine HTTP başlığına (`x-goog-api-key`) taşındı; proxy ve sunucu loglarına anahtar sızması önlendi.
   - Harici LLM ve özel uç noktalarda şifrelenmemiş HTTP bağlantıları engellenerek `https://` zorunlu kılındı (yalnızca yerel testler için `localhost/loopback` HTTP adreslerine izin verilir).

3. **🧠 Model Bütünlüğü & SHA-256 Sağlama**:
   - Whisper model kataloğundaki tüm modeller (Large-v3 Turbo, Small, Base, Tiny, Medium, Large-v3, Silero VAD) için resmi SHA-256 özetleri eklendi.
   - İndirme tamamlandığında model dosyası doğrulanır; karma özeti uyuşmayan veya manipüle edilmiş dosyalar otomatik olarak silinir ve kullanıcı uyarılır.

4. **🔐 DPAPI Şifreleme Sertleştirmesi**:
   - API anahtarlarının Windows DPAPI ile saklanmasında uygulamaya özel ek entropy (`TRWhisper_DPAPI_Entropy_v2`) tanımlandı.
   - DPAPI hatası durumunda anahtarın sessizce düz metin (cleartext) olarak diske kaydedilmesi riski ortadan kaldırıldı; geriye dönük uyumluluk korunarak eski şifreli anahtarların okunması sağlandı.

5. **⚡ Komut Enjeksiyonu ve ReDoS Koruması**:
   - Whisper CLI çalıştırma sürecinde argüman birleştirme yerine `.NET 9` `ArgumentList` güvenli parametre geçişine geçildi.
   - Özel sözlük düzenli ifadelerine (Regex) 250 ms zaman aşımı atanarak ReDoS (düzenli ifade kaynak tüketimi) saldırıları engellendi.

6. **🛠️ Sistem Kararlılığı ve Bellek İyileştirmesi**:
   - Sistem tepsisi simgesinde (NotifyIcon) tespit edilen Windows GDI unmanaged handle (`HICON`) sızıntısı `DestroyIcon` Windows API entegrasyonu ile giderildi.
   - DLL hijacking / binary planting saldırılarına karşı `SetDefaultDllDirectories` ile arama yolları güvenli kılındı.
   - Inno Setup kurulum paketinde CUDA 13 paketi indirmeleri için resmi SHA-256 kontrolü ve çalışma zamanı güvenlik kilidi sabitlendi.

---

## 🇬🇧 English Release Notes

### 🛡️ Key Security & Reliability Improvements in 2.0.1

1. **🔒 PII & Transcript Privacy Protection**:
   - Added `EnableHistoryLogging` setting to allow users to completely disable saving transcript markdown logs to disk.
   - Sanitized diagnostic logs (`trwhisper.log`) to strip sensitive transcript content, recording only character counts and performance metrics.
   - Replaced static temp audio filenames with session-scoped GUIDs (`trwhisper_{Guid}.wav`) and added startup cleanup for orphaned audio recordings.

2. **🔑 API Key & Network Security**:
   - Migrated Google Gemini API keys from URL query strings to the `x-goog-api-key` request header, preventing key leakage in proxy/server logs.
   - Enforced HTTPS for external custom LLM endpoints (plain HTTP is strictly restricted to loopback/localhost).

3. **🧠 Model Integrity & SHA-256 Verification**:
   - Added verified SHA-256 hashes to all catalog models. Model files are cryptographically validated after download, and corrupt or tampered downloads are automatically purged.

4. **🔐 DPAPI Storage Hardening**:
   - Added application-specific entropy for DPAPI credential encryption.
   - Eliminated silent cleartext fallback on DPAPI encryption errors, while preserving backward compatibility for decrypting existing keys.

5. **⚡ Command Injection & ReDoS Mitigations**:
   - Switched to `.NET 9` safe `ArgumentList` API in CLI runner to prevent command-line argument injection.
   - Implemented 250ms timeouts on custom dictionary regular expressions to prevent Regular Expression Denial of Service (ReDoS).

6. **🛠️ System Stability & GDI Handle Leak Resolution**:
   - Fixed an unmanaged `HICON` GDI handle leak in tray icon status updates via `DestroyIcon` P/Invoke.
   - Hardened DLL search orders against binary planting via `SetDefaultDllDirectories`.
   - Guaranteed SHA-256 verification and safety gates for optional CUDA 13 package downloads in Inno Setup installer.
