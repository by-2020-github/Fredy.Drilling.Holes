using MvCamCtrl.NET;
using System;

namespace HAL
{
    public static class HkSdkManager
    {
        private static readonly object SyncRoot = new();
        private static int _referenceCount;
        private static bool _isInitialized;

        public static void Acquire()
        {
            lock (SyncRoot)
            {
                if (_referenceCount == 0)
                {
                    var ret = MyCamera.MV_CC_Initialize_NET();
                    if (ret != MyCamera.MV_OK)
                    {
                        throw new InvalidOperationException($"Initialize HK SDK failed: 0x{ret:X8}");
                    }

                    _isInitialized = true;
                }

                _referenceCount++;
            }
        }

        public static void Release()
        {
            lock (SyncRoot)
            {
                if (_referenceCount <= 0)
                {
                    return;
                }

                _referenceCount--;
                if (_referenceCount > 0 || !_isInitialized)
                {
                    return;
                }

                try
                {
                    MyCamera.MV_CC_Finalize_NET();
                }
                finally
                {
                    _isInitialized = false;
                }
            }
        }
    }
}
