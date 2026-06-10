using System;

namespace PhotoAgent
{
    public static class CaptureState
    {
        public static bool IsBusy = false;

        public static DateTime Started =
            DateTime.MinValue;
    }
}