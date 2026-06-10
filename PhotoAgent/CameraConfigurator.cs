using System.Windows.Forms;

namespace PhotoAgent
{
    public static class CameraConfigurator
    {
        public static bool Run()
        {
            bool lomoAssigned = false;
            bool documentAssigned = false;

            var cameras =
                CameraManager.GetCameras();

            foreach (CameraInfo camera in cameras)
            {
                using (var form =
                    new CameraAssignmentForm(
                        camera,
                        cameras.IndexOf(camera) + 1,
                        cameras.Count,
                        lomoAssigned,
                        documentAssigned))
                {
                    DialogResult result =
                        form.ShowDialog();

                    if (result != DialogResult.OK)
                    {
                        MessageBox.Show(
                            "Настройка камер не завершена.\r\nПриложение будет закрыто.",
                            "PhotoAgent",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );

                        return false;
                    }

                    if (form.SelectedRole ==
                        "LOMO")
                    {
                        ConfigManager.Current
                            .lomoCameraId =
                            camera.MonikerString;

                        lomoAssigned = true;
                    }

                    if (form.SelectedRole ==
                        "DOCUMENT")
                    {
                        ConfigManager.Current
                            .documentCameraId =
                            camera.MonikerString;

                        documentAssigned = true;
                    }

                    if (lomoAssigned &&
                        documentAssigned)
                    {
                        ConfigManager.Save();

                        MessageBox.Show(
                            "Настройка камер завершена.",
                            "PhotoAgent",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );

                        return true;
                    }
                }
            }

            MessageBox.Show(
                "Не удалось назначить обе камеры.",
                "PhotoAgent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );

            return false;
        }
    }
}