using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Application.PointStatus;

namespace HonestFlow.Infrastructure
{
    public sealed class WindowsKktPnpProbe : IKktPnpProbe
    {
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfAllClasses = 0x00000004;
        private const uint SpdrpDeviceDescription = 0x00000000;
        private const uint SpdrpManufacturer = 0x0000000B;
        private const uint SpdrpFriendlyName = 0x0000000C;
        private const int ErrorNoMoreItems = 259;
        private static readonly IntPtr InvalidHandleValue = new(-1);

        public Task<KktPnpResult> DetectAsync(CancellationToken cancellationToken) =>
            Task.Run(() => Detect(cancellationToken), cancellationToken);

        private static KktPnpResult Detect(CancellationToken cancellationToken)
        {
            IntPtr devices = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (devices == InvalidHandleValue)
                return KktPnpResult.Unavailable(new Win32Exception(Marshal.GetLastWin32Error()).Message);

            try
            {
                for (uint index = 0; ; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var deviceInfo = new SpDeviceInfoData { Size = (uint)Marshal.SizeOf<SpDeviceInfoData>() };
                    if (!SetupDiEnumDeviceInfo(devices, index, ref deviceInfo))
                    {
                        int error = Marshal.GetLastWin32Error();
                        return error == ErrorNoMoreItems
                            ? KktPnpResult.NotDetected()
                            : KktPnpResult.Unavailable(new Win32Exception(error).Message);
                    }

                    string name = GetProperty(devices, ref deviceInfo, SpdrpDeviceDescription);
                    string friendlyName = GetProperty(devices, ref deviceInfo, SpdrpFriendlyName);
                    string manufacturer = GetProperty(devices, ref deviceInfo, SpdrpManufacturer);
                    if (KktPnpDeviceMatcher.IsAtol(name, friendlyName, manufacturer))
                    {
                        string displayName = FirstValue(friendlyName, name, manufacturer);
                        return KktPnpResult.Detected($"Windows PnP: {displayName}; manufacturer={manufacturer ?? "-"}.");
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devices);
            }
        }

        private static string GetProperty(IntPtr devices, ref SpDeviceInfoData deviceInfo, uint property)
        {
            var buffer = new StringBuilder(1024);
            return SetupDiGetDeviceRegistryProperty(
                devices, ref deviceInfo, property, out _, buffer, buffer.Capacity * sizeof(char), out _)
                ? buffer.ToString().Trim()
                : null;
        }

        private static string FirstValue(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value)) return value;
            return "ATOL device";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SpDeviceInfoData
        {
            public uint Size;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            IntPtr classGuid,
            string enumerator,
            IntPtr parentWindow,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDeviceInfoData deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SpDeviceInfoData deviceInfoData,
            uint property,
            out uint propertyType,
            StringBuilder propertyBuffer,
            int propertyBufferSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    }
}
