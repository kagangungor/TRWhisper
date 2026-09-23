using System;
using System.IO;

namespace TRWhisper.Core.Config
{
    /// <summary>
    /// Uygulamanın yazılabilir veri konumları.
    ///
    /// Varsayılan kurulum kullanıcı profilinedir (%LOCALAPPDATA%\Programs\TRWhisper) ve orada
    /// her şey exe'nin yanında durur — eski sürümlerle birebir aynı davranış. Kurulum yazma
    /// korumalı bir dizine yapıldıysa (kurulum dosyası /ALLUSERS ile Program Files'a kurulur)
    /// ayarlar %APPDATA%\TRWhisper, sonradan indirilen modeller %LOCALAPPDATA%\TRWhisper
    /// altına yazılır; uygulama dizini yalnızca okunur kalır.
    /// </summary>
    public static class AppPaths
    {
        private static bool? _baseWritable;

        public static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// Uygulama dizinine yazılabiliyor mu. Sonuç önbelleklenir: her yapılandırma
        /// okumasında dosya sistemine sonda dosyası yazmanın anlamı yok.
        /// </summary>
        public static bool IsBaseDirectoryWritable => _baseWritable ??= IsDirectoryWritable(BaseDirectory);

        /// <summary>Ayarların (config.json, dictionary.json) yedek konumu.</summary>
        public static string RoamingDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TRWhisper");

        /// <summary>İndirilen modellerin yedek konumu (profil ile dolaşmaz, GB'larca olabilir).</summary>
        public static string LocalDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TRWhisper");

        public static bool IsDirectoryWritable(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) return false;

                var probe = Path.Combine(directory, $".trwhisper_write_probe_{Guid.NewGuid():N}");
                using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
