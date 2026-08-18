using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace RubiksCubeSolver.Hardware;

public static partial class DeviceEnumerator
{
    public static IReadOnlyList<CameraDevice> ListCameras()
    {
        var named = ListDirectShowCameras();
        if (named.Count > 0)
        {
            return named;
        }

        var fallback = new List<CameraDevice>();
        for (int i = 0; i < 6; i++)
        {
            using var capture = new OpenCvSharp.VideoCapture(i, OpenCvSharp.VideoCaptureAPIs.DSHOW);
            if (!capture.IsOpened())
            {
                capture.Open(i);
            }

            if (capture.IsOpened())
            {
                fallback.Add(new CameraDevice { Index = i, Name = $"Camera {i}" });
                capture.Release();
            }
        }

        return fallback;
    }

    static IReadOnlyList<CameraDevice> ListDirectShowCameras()
    {
        var cameras = new List<CameraDevice>();
        ICreateDevEnum? creator = null;
        IEnumMoniker? enumerator = null;
        try
        {
            creator = (ICreateDevEnum)new CreateDevEnum();
            var category = VideoInputDeviceCategory;
            creator.CreateClassEnumerator(ref category, out enumerator, 0);
            if (enumerator is null)
            {
                return cameras;
            }

            var monikers = new IMoniker[1];
            int index = 0;
            while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var moniker = monikers[0];
                try
                {
                    var name = ReadFriendlyName(moniker) ?? $"Camera {index}";
                    cameras.Add(new CameraDevice { Index = index, Name = name });
                    index++;
                }
                finally
                {
                    Marshal.ReleaseComObject(moniker);
                }
            }
        }
        catch
        {
            return cameras;
        }
        finally
        {
            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }

            if (creator is not null)
            {
                Marshal.ReleaseComObject(creator);
            }
        }

        return cameras;
    }

    static string? ReadFriendlyName(IMoniker moniker)
    {
        object? bagObj = null;
        try
        {
            var iid = typeof(IPropertyBag).GUID;
            moniker.BindToStorage(null!, null, ref iid, out bagObj);
            if (bagObj is IPropertyBag bag)
            {
                object value = "";
                bag.Read("FriendlyName", ref value, IntPtr.Zero);
                return value?.ToString();
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (bagObj is not null)
            {
                Marshal.ReleaseComObject(bagObj);
            }
        }

        return null;
    }

    static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");

    [ComImport]
    [Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86")]
    class CreateDevEnum;

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid type, out IEnumMoniker enumMoniker, int flags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string propName, [In][Out][MarshalAs(UnmanagedType.Struct)] ref object value, IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string propName, ref object value);
    }
}
