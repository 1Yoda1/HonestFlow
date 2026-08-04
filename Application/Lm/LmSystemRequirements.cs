using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace HonestFlow.Application.Lm
{
    public sealed class LmSystemResources
    {
        public LmSystemResources(int physicalCoreCount, ulong totalMemoryBytes, long availableDiskBytes)
        {
            PhysicalCoreCount = physicalCoreCount;
            TotalMemoryBytes = totalMemoryBytes;
            AvailableDiskBytes = availableDiskBytes;
        }

        public int PhysicalCoreCount { get; }
        public ulong TotalMemoryBytes { get; }
        public long AvailableDiskBytes { get; }
    }

    public sealed class LmSystemRequirementsResult
    {
        public LmSystemRequirementsResult(IReadOnlyList<string> minimumWarnings, IReadOnlyList<string> recommendations)
        {
            MinimumWarnings = minimumWarnings;
            Recommendations = recommendations;
        }

        public IReadOnlyList<string> MinimumWarnings { get; }
        public IReadOnlyList<string> Recommendations { get; }
        public bool MeetsMinimum => MinimumWarnings.Count == 0;
        public bool HasWarnings => MinimumWarnings.Count > 0 || Recommendations.Count > 0;
    }

    public static class LmSystemRequirements
    {
        public const int MinimumPhysicalCores = 4;
        public const ulong MinimumMemoryBytes = 4UL * 1024 * 1024 * 1024;
        public const long MinimumFreeDiskBytes = 150L * 1024 * 1024;
        public const long RecommendedFreeDiskBytes = 1024L * 1024 * 1024;

        public static LmSystemRequirementsResult Check() => Evaluate(ReadSystemResources());

        public static LmSystemRequirementsResult Evaluate(LmSystemResources resources)
        {
            ArgumentNullException.ThrowIfNull(resources);
            var minimumWarnings = new List<string>();
            var recommendations = new List<string>();

            if (resources.PhysicalCoreCount > 0 && resources.PhysicalCoreCount < MinimumPhysicalCores)
                minimumWarnings.Add($"Физических ядер: {resources.PhysicalCoreCount}, требуется не менее {MinimumPhysicalCores}");

            if (resources.TotalMemoryBytes > 0 && resources.TotalMemoryBytes < MinimumMemoryBytes)
                minimumWarnings.Add($"Оперативная память: {FormatGiB(resources.TotalMemoryBytes)}, требуется не менее 4 ГБ");

            if (resources.AvailableDiskBytes >= 0 && resources.AvailableDiskBytes < MinimumFreeDiskBytes)
                minimumWarnings.Add($"Свободно на системном диске: {FormatMiB(resources.AvailableDiskBytes)}, требуется не менее 150 МБ");
            else if (resources.AvailableDiskBytes >= MinimumFreeDiskBytes && resources.AvailableDiskBytes < RecommendedFreeDiskBytes)
                recommendations.Add($"Свободно на системном диске {FormatMiB(resources.AvailableDiskBytes)}; рекомендуется не менее 1 ГБ для роста базы ЛМ ЧЗ");

            return new LmSystemRequirementsResult(minimumWarnings, recommendations);
        }

        private static LmSystemResources ReadSystemResources()
        {
            int cores = TryGetPhysicalCoreCount();
            ulong memory = TryGetTotalMemory();
            long freeDisk = TryGetSystemDiskFreeSpace();
            return new LmSystemResources(cores, memory, freeDisk);
        }

        private static int TryGetPhysicalCoreCount()
        {
            try
            {
                uint length = 0;
                GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
                if (length == 0)
                    return Environment.ProcessorCount;

                IntPtr buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    if (!GetLogicalProcessorInformationEx(0, buffer, ref length))
                        return Environment.ProcessorCount;

                    int count = 0;
                    int offset = 0;
                    while (offset < length)
                    {
                        IntPtr record = IntPtr.Add(buffer, offset);
                        int relationship = Marshal.ReadInt32(record, 0);
                        int size = Marshal.ReadInt32(record, 4);
                        if (size <= 0 || offset + size > length)
                            break;
                        if (relationship == 0)
                            count++;
                        offset += size;
                    }

                    return count > 0 ? count : Environment.ProcessorCount;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return Environment.ProcessorCount;
            }
        }

        private static ulong TryGetTotalMemory()
        {
            try
            {
                var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                return GlobalMemoryStatusEx(ref status) ? status.TotalPhysical : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static long TryGetSystemDiskFreeSpace()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                return string.IsNullOrWhiteSpace(root) ? -1 : new DriveInfo(root).AvailableFreeSpace;
            }
            catch
            {
                return -1;
            }
        }

        private static string FormatGiB(ulong bytes) => $"{bytes / 1024d / 1024d / 1024d:0.#} ГБ";
        private static string FormatMiB(long bytes) => $"{bytes / 1024d / 1024d:0} МБ";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }
}
