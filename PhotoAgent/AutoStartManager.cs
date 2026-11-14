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
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    string command = "\"" + Application.ExecutablePath + "\"";
                    object current = key.GetValue(AppName);
                    if (current == null || current.ToString() != command)
                    {
                        key.SetValue(AppName, command, RegistryValueKind.String);
                        LogManager.Write("Autostart registered");
                    }
                }
            }
            catch
            {
            }
        }
    }
}
