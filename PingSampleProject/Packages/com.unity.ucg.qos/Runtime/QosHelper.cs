using System;

namespace Unity.Networking.QoS
{
    public static class QosHelper
    {
        public static bool WouldBlock(int errorcode)
        {
            // WSAEWOULDBLOCK == 10035 (windows)
            // EAGAIN == 11 or 35 (supported POSIX platforms)
            // EWOULDBLOCK == 11 or 35 (supported POSIX platforms, generally an alias for EAGAIN)(*)
            //
            // (*)Could also be 54 on SUSv3, and 246 on AIX 4.3,5.1, but we don't support those platforms
            return (errorcode == 10035 /*WSAEWOULDBLOCK*/ ||
                    errorcode == 11 || errorcode == 35 /*EWOULDBLOCK, EAGAIN*/)
                ? true
                : false;
        }

        public static bool ExpiredUtc(DateTime timeUtc)
        {
            return DateTime.UtcNow > timeUtc;
        }

        public static TimeSpan RemainingUtc(DateTime timeUtc)
        {
            return timeUtc.Subtract(DateTime.UtcNow);
        }
    }
}