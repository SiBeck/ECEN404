using System.IO;
using System.Text.Json;

namespace _403DesktopApp.Services
{
    public static class AuditLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BioMetrix", "audit.log");

        public static void Log(string providerId, string action, string resourceType, string resourceId)
        {
            try
            {
                string entry = JsonSerializer.Serialize(new
                {
                    timestamp = DateTime.UtcNow,
                    providerId,
                    action,
                    resourceType,
                    resourceId
                });
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllLines(LogPath, new[] { entry });
            }
            catch { /* audit log failures must not crash the app */ }
        }
    }
}
