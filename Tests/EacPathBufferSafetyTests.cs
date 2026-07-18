using System;

namespace AudioDataPlugIn
{
    internal static class EacPathBufferSafetyTests
    {
        private const uint Eac18ActualPathVa = 0x009A1534;
        private const uint Eac18ChecksumFlagVa = 0x009A173D;
        private const uint Eac16ActualPathVa = 0x0083E3A4;
        private const uint Eac16ChecksumFlagVa = 0x0083E5AD;

        private static int Main()
        {
            AssertBufferEndsBeforeChecksum(
                "EAC 1.8",
                Eac18ActualPathVa,
                Eac18ChecksumFlagVa);
            AssertBufferEndsBeforeChecksum(
                "EAC 1.6",
                Eac16ActualPathVa,
                Eac16ChecksumFlagVa);

            Console.WriteLine("EAC path buffer safety tests passed.");
            return 0;
        }

        private static void AssertBufferEndsBeforeChecksum(
            string version,
            uint pathBufferVa,
            uint checksumFlagVa)
        {
            uint firstByteAfterBuffer = pathBufferVa +
                (uint)(EnhancementRuntime.EacPathBufferCapacity * sizeof(char));
            if (firstByteAfterBuffer > checksumFlagVa)
            {
                throw new Exception(
                    version + " path buffer reaches the AddChecksumLogFile flag: end=0x" +
                    firstByteAfterBuffer.ToString("X8") + ", checksum=0x" +
                    checksumFlagVa.ToString("X8") + ".");
            }
        }
    }
}
