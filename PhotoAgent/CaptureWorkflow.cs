using System;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace PhotoAgent
{
    public static class CaptureWorkflow
    {
        public static CaptureResult RecapturePhoto(CaptureCommand cmd)
        {
            return RecapturePhoto(cmd, null);
        }

        internal static CaptureResult RecapturePhoto(
            CaptureCommand cmd, Action<string, string> photoReady)
        {
            var result = new CaptureResult();
            string stagingFolder = null;
            try
            {
                if (!CaptureCommand.IsSupportedPhotoType(cmd.photo_type))
                    throw new ArgumentException(CaptureCommand.InvalidPhotoTypeMessage);

                string title = cmd.photo_type == "lomo" ? "Ломосдатчик"
                    : cmd.photo_type == "passport" ? "Паспорт" : "Прописка";
                string cameraId = cmd.photo_type == "lomo"
                    ? ConfigManager.Current.lomoCameraId : ConfigManager.Current.documentCameraId;

                using (var form = new CaptureForm(cameraId, title,
                    "Пересъёмка: " + title + "\r\n\r\nДокумент: " + cmd.document_number))
                {
                    if (form.ShowDialog() != DialogResult.OK)
                        return result;

                    // Stage the named JPEG before replacing the legacy working copy.
                    // A rejected transfer must not destroy the previous photograph.
                    stagingFolder = Path.Combine(Path.GetTempPath(),
                        "PhotoAgent_recapture_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(stagingFolder);
                    string fileName = cmd.document_number + "_" + cmd.photo_type + ".jpg";
                    string stagedPhoto = Path.Combine(stagingFolder, fileName);
                    form.CapturedImage.Save(stagedPhoto, ImageFormat.Jpeg);
                    if (photoReady != null)
                        photoReady(stagedPhoto, cmd.photo_type);

                    string documentFolder = Path.Combine(@"C:\psa_photos\photos", cmd.document_number);
                    Directory.CreateDirectory(documentFolder);
                    string workingPhoto = Path.Combine(documentFolder, fileName);
                    File.Copy(stagedPhoto, workingPhoto, true);
                    ArchiveManager.ArchivePhoto(cmd.document_number, workingPhoto);
                    LogManager.Write("Recaptured: " + workingPhoto);
                    result.Files.Add(fileName);
                    result.Success = true;
                }
            }
            catch (Exception ex)
            {
                LogManager.Write("RecapturePhoto error: " + ex.Message);
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                if (stagingFolder != null)
                {
                    try { Directory.Delete(stagingFolder, true); }
                    catch (Exception ex) { LogManager.Write("Recapture staging cleanup: " + ex.Message); }
                }
            }
            return result;
        }

        public static CaptureResult CaptureDocument(CaptureCommand cmd)
        {
            return CaptureDocument(cmd, null);
        }

        internal static CaptureResult CaptureDocument(
            CaptureCommand cmd, Action<string, string> photoReady)
        {
            return CaptureSeries(cmd, photoReady, true, true);
        }

        public static CaptureResult CapturePhoto(CaptureCommand cmd)
        {
            return CapturePhoto(cmd, null);
        }

        internal static CaptureResult CapturePhoto(
            CaptureCommand cmd, Action<string, string> photoReady)
        {
            return CaptureSeries(cmd, photoReady, true, false);
        }

        public static CaptureResult CaptureDocuments(CaptureCommand cmd)
        {
            return CaptureDocuments(cmd, null);
        }

        internal static CaptureResult CaptureDocuments(
            CaptureCommand cmd, Action<string, string> photoReady)
        {
            return CaptureSeries(cmd, photoReady, false, true);
        }

        private static CaptureResult CaptureSeries(CaptureCommand cmd,
            Action<string, string> photoReady, bool includeLomo, bool includeDocuments)
        {
            var result = new CaptureResult();
            bool legacySeries = includeLomo && includeDocuments;
            try
            {
                string documentFolder = Path.Combine(
                    @"C:\psa_photos\photos", cmd.document_number);
                Directory.CreateDirectory(documentFolder);
                SessionManager.Current = new PhotoSession
                {
                    RequestId = cmd.request_id,
                    DocumentNumber = cmd.document_number,
                    PhotosFolder = documentFolder,
                    ExtraPhotoCount = 0
                };

                Action<string, string> ready = photoReady;
                if (!legacySeries)
                {
                    ready = (file, type) =>
                    {
                        if (photoReady != null) photoReady(file, type);
                        // Independent requests must not replace unrelated archive photos.
                        ArchiveManager.ArchivePhoto(cmd.document_number, file);
                    };
                }

                if (includeLomo && !CaptureLomo(cmd, documentFolder, ready))
                    return result;

                if (includeDocuments)
                {
                    // Neither create nor check the document camera for a face-only request.
                    using (var preview = new CameraPreviewControl(
                        ConfigManager.Current.documentCameraId))
                    {
                        if (!CapturePassport(cmd, documentFolder, preview, ready, legacySeries))
                            return result;
                        if (!CaptureRegistration(cmd, documentFolder, preview, ready))
                            return result;
                    }
                }

                if (legacySeries)
                    ArchiveManager.ArchiveDocument(cmd.document_number);
                if (includeLomo)
                    result.Files.Add(cmd.document_number + "_lomo.jpg");
                if (includeDocuments)
                {
                    result.Files.Add(cmd.document_number + "_passport.jpg");
                    result.Files.Add(cmd.document_number + "_registration.jpg");
                }
                result.Success = true;
            }
            catch (Exception ex)
            {
                LogManager.Write("CaptureWorkflow error: " + ex.Message);
                result.ErrorMessage = ex.Message;
            }
            return result;
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
            CameraPreviewControl preview, Action<string, string> photoReady,
            bool archiveDocument)
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

                if (archiveDocument)
                    ArchiveManager.ArchiveDocument(cmd.document_number);

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
