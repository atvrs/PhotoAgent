using System;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace PhotoAgent
{
    public static class CaptureWorkflow
    {
        public static CaptureResult CaptureDocument(CaptureCommand cmd)
        {
            return CaptureDocument(cmd, null);
        }

        internal static CaptureResult CaptureDocument(
            CaptureCommand cmd, Action<string, string> photoReady)
        {
            CaptureResult result =
                new CaptureResult();

            try
            {
                string documentFolder =
                    Path.Combine(
                        @"C:\psa_photos\photos",
                        cmd.document_number
                    );

                Directory.CreateDirectory(
                    documentFolder
                );

                SessionManager.Current =
                    new PhotoSession
                    {
                        RequestId =
                            cmd.request_id,

                        DocumentNumber =
                            cmd.document_number,

                        PhotosFolder =
                            documentFolder,

                        ExtraPhotoCount = 0
                    };

                if (!CaptureLomo(
                    cmd,
                    documentFolder, photoReady))
                {
                    return result;
                }

                using (var documentPreview = new CameraPreviewControl(
                    ConfigManager.Current.documentCameraId))
                {
                    if (!CapturePassport(cmd, documentFolder, documentPreview, photoReady))
                        return result;

                    if (!CaptureRegistration(cmd, documentFolder, documentPreview, photoReady))
                        return result;
                }

                ArchiveManager.ArchiveDocument(
                    cmd.document_number
                );

                result.Files.Add(
                    cmd.document_number
                    + "_lomo.jpg"
                );

                result.Files.Add(
                    cmd.document_number
                    + "_passport.jpg"
                );

                result.Files.Add(
                    cmd.document_number
                    + "_registration.jpg"
                );

                result.Success = true;

                return result;
            }
            catch (Exception ex)
            {
                LogManager.Write(
                    "CaptureWorkflow error: "
                    + ex.Message
                );

                result.Success = false;
                result.ErrorMessage = ex.Message;

                return result;
            }
        }

        private static bool CaptureLomo(
            CaptureCommand cmd,
            string folder, Action<string, string> photoReady)
        {
            using (CaptureForm form =
                new CaptureForm(
    ConfigManager.Current
        .lomoCameraId,

    "Ломосдатчик",

    "Ломосдатчик"
    + "\r\n\r\nДокумент: "
    + cmd.document_number
))
            {
                if (form.ShowDialog() !=
                    DialogResult.OK)
                {
                    return false;
                }

                string fileName =
                    Path.Combine(
                        folder,
                        cmd.document_number
                        + "_lomo.jpg"
                    );

                form.CapturedImage.Save(
                    fileName,
                    ImageFormat.Jpeg
                );
                if (photoReady != null)
                    photoReady(fileName, "lomo");

                LogManager.Write(
                    "Saved: "
                    + fileName
                );


               return true;
            }
        }

        private static bool CapturePassport(
            CaptureCommand cmd,
            string folder,
            CameraPreviewControl preview, Action<string, string> photoReady)
        {
            using (CaptureForm form =
                new CaptureForm(
    preview,

    "Паспорт",

    "Фотографирование документа"
    + "\r\n\r\nДокумент: "
    + cmd.document_number
    + "\r\nТип снимка: Паспорт"
))
            {
                if (form.ShowDialog() !=
                    DialogResult.OK)
                {
                    return false;
                }

                string fileName =
                    Path.Combine(
                        folder,
                        cmd.document_number
                        + "_passport.jpg"
                    );

                form.CapturedImage.Save(
                    fileName,
                    ImageFormat.Jpeg
                );
                if (photoReady != null)
                    photoReady(fileName, "passport");

                LogManager.Write(
                    "Saved: "
                    + fileName
                );

                ArchiveManager.ArchiveDocument(
                    cmd.document_number
                );

                return true;
            }
        }

        private static bool CaptureRegistration(
            CaptureCommand cmd,
            string folder,
            CameraPreviewControl preview, Action<string, string> photoReady)
        {
            using (CaptureForm form =
                new CaptureForm(
    preview,

    "Прописка",

    "Фотографирование документа"
    + "\r\n\r\nДокумент: "
    + cmd.document_number
    + "\r\nТип снимка: Прописка"
))
            {
                if (form.ShowDialog() !=
                    DialogResult.OK)
                {
                    return false;
                }

                string fileName =
                    Path.Combine(
                        folder,
                        cmd.document_number
                        + "_registration.jpg"
                    );

                form.CapturedImage.Save(
                    fileName,
                    ImageFormat.Jpeg
                );
                if (photoReady != null)
                    photoReady(fileName, "registration");

                LogManager.Write(
                    "Saved: "
                    + fileName
                );

                
                return true;
            }
        }
        public static CaptureResult CaptureExtra(CaptureCommand cmd)
        {
            return CaptureExtra(cmd, null);
        }

        internal static CaptureResult CaptureExtra(
            CaptureCommand cmd, Action<string, string> photoReady)
        {
            CaptureResult result =
    new CaptureResult();
            try
            {
                string documentFolder =
                    Path.Combine(
                        @"C:\psa_photos\photos",
                        cmd.document_number
                    );

                Directory.CreateDirectory(
                    documentFolder
                );

                using (ExtraCaptureForm form =
                    new ExtraCaptureForm(
                        ConfigManager.Current
                            .documentCameraId,

                        cmd.document_number,

                        documentFolder, photoReady))
                {
                    if (form.ShowDialog() != DialogResult.OK)
                    {
                        result.ErrorMessage = form.ErrorMessage;
                        return result;
                    }
                }

                foreach (string file in
      Directory.GetFiles(
          documentFolder,
          cmd.document_number
          + "_extra_*.jpg"))
                {
                    result.Files.Add(
                        Path.GetFileName(file)
                    );
                }

                result.Success = true;

                return result;
            }
            catch (Exception ex)
            {
                LogManager.Write(
                    "CaptureExtra error: "
                    + ex.Message
                );

                result.Success = false;
                result.ErrorMessage = ex.Message;

                return result;
            }
        }
    }
}
