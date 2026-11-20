namespace PhotoAgent
{
    public class CaptureCommand
    {
        public string command { get; set; }

        public string request_id { get; set; }

        public string document_number { get; set; }

        public string photo_type { get; set; }

        internal const string InvalidPhotoTypeMessage =
            "Для recapture_photo укажите photo_type: lomo, passport или registration.";

        internal static bool IsSupportedPhotoType(string value)
        {
            return value == "lomo" || value == "passport" || value == "registration";
        }
    }
}
