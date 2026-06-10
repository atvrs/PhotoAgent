using System.Collections.Generic;

namespace PhotoAgent
{
    public class CaptureResult
    {
        public bool Success
        {
            get;
            set;
        }

        public List<string> Files
        {
            get;
            set;
        }

        public CaptureResult()
        {
            Files =
                new List<string>();
        }
    }
}