using System;
using System.Windows.Forms;
using System.Threading;

namespace PhotoAgent
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\PhotoAgent", out createdNew))
            {
                if (!createdNew)
                    return;

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new TrayApplicationContext());
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }
    }
}
