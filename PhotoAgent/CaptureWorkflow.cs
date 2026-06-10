using System;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace PhotoAgent
{
    public static class CaptureWorkflow
    {
        public static CaptureResult CaptureDocument(
    CaptureCommand cmd)
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
                    documentFolder))
                {
                    return result;
                }

                if (!CapturePassport(
                    cmd,
                    documentFolder))
                {
                    return result;
                }

                if (!CaptureRegistration(
                    cmd,
                    documentFolder))
                {
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

                return result;
            }
        }

        private static bool CaptureLomo(
            CaptureCommand cmd,
            string folder)
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

                LogManager.Write(
                    "Saved: "
                    + fileName
                );


               return true;
            }
        }

        private static bool CapturePassport(
            CaptureCommand cmd,
            string folder)
        {
            using (CaptureForm form =
                new CaptureForm(
    ConfigManager.Current
        .documentCameraId,

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
            string folder)
        {
            using (CaptureForm form =
                new CaptureForm(
    ConfigManager.Current
        .documentCameraId,

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

                LogManager.Write(
                    "Saved: "
                    + fileName
                );

                
                return true;
            }
        }
        public static CaptureResult CaptureExtra(
     CaptureCommand cmd)
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

                        documentFolder))
                {
                    form.ShowDialog();
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

                return result;
            }
        }
    }
}