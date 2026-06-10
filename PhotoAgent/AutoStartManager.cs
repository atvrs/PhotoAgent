using Microsoft.Win32;
using System.Windows.Forms;

namespace PhotoAgent
{
    public static class AutoStartManager
    {
        private const string AppName =
            "PhotoAgent";

        public static void EnsureAutoStart()
        {
            try
            {
                RegistryKey key =
                    Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run",
                        true
                    );

                string exePath =
                    Application.ExecutablePath;

                object current =
                    key.GetValue(AppName);

                if (current == null ||
                    current.ToString() != exePath)
                {
                    key.SetValue(
                        AppName,
                        exePath
                    );

                    LogManager.Write(
                        "Autostart registered"
                    );
                }
            }
            catch
            {
            }
        }
    }
}