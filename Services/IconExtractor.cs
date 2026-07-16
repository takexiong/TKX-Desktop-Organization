using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace DesktopOrganizer.Services;

public static class IconExtractor
{
    /// <summary>
    /// 按 DIP 显示尺寸与 DPI 提取图标，尽量做到 1:1 像素对齐，减少放大发糊。
    /// </summary>
    public static ImageSource GetIcon(string path, int dipSize, double dpiScale = 1.0)
    {
        dpiScale = Math.Max(dpiScale, 1.0);
        // 目标物理像素：按 DPI 对齐，并上取到常见图标尺寸
        var targetPixels = (int)Math.Round(dipSize * dpiScale, MidpointRounding.AwayFromZero);
        targetPixels = SnapIconSize(Math.Clamp(targetPixels, 16, 256));

        try
        {
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return GetFallback(targetPixels, dpiScale);

            // 向 Shell 多要一档清晰度，再缩到目标像素（比直接放大更清晰）
            var requestPixels = SnapIconSize(Math.Min(256, Math.Max(targetPixels, NextShellSize(targetPixels))));

            if (TryGetShellBitmap(path, requestPixels, out var hBitmap) && hBitmap != IntPtr.Zero)
            {
                try
                {
                    return CreateSharpBitmap(hBitmap, targetPixels, dpiScale);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }

            if (TryGetImageListIcon(path, targetPixels, dpiScale, out var imageListIcon) && imageListIcon is not null)
                return imageListIcon;

            return GetFallback(targetPixels, dpiScale);
        }
        catch
        {
            return GetFallback(targetPixels, dpiScale);
        }
    }

    public static string GetDisplayName(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return new DirectoryInfo(path).Name;

            var name = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name;
        }
        catch
        {
            return path;
        }
    }

    private static BitmapSource CreateSharpBitmap(IntPtr hBitmap, int targetPixels, double dpiScale)
    {
        var raw = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap,
            IntPtr.Zero,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        raw.Freeze();
        return NormalizeToDisplay(raw, targetPixels, dpiScale);
    }

    private static BitmapSource NormalizeToDisplay(BitmapSource raw, int targetPixels, double dpiScale)
    {
        BitmapSource sized = raw;
        if (raw.PixelWidth != targetPixels || raw.PixelHeight != targetPixels)
        {
            var scaleX = (double)targetPixels / raw.PixelWidth;
            var scaleY = (double)targetPixels / raw.PixelHeight;
            var transformed = new TransformedBitmap(raw, new ScaleTransform(scaleX, scaleY));
            transformed.Freeze();
            sized = transformed;
        }

        // 写入正确 DPI，使 WPF 按 DIP 显示时尽量不再二次缩放
        var dpi = 96.0 * dpiScale;
        var bgra = new FormatConvertedBitmap(sized, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();

        var width = Math.Min(targetPixels, bgra.PixelWidth);
        var height = Math.Min(targetPixels, bgra.PixelHeight);
        var stride = targetPixels * 4;
        var pixels = new byte[stride * targetPixels];
        bgra.CopyPixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

        var result = BitmapSource.Create(
            targetPixels,
            targetPixels,
            dpi,
            dpi,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    private static int SnapIconSize(int size)
    {
        ReadOnlySpan<int> sizes = [16, 20, 24, 32, 40, 48, 64, 80, 96, 128, 256];
        foreach (var s in sizes)
        {
            if (size <= s)
                return s;
        }

        return 256;
    }

    private static int NextShellSize(int size) => size switch
    {
        <= 16 => 32,
        <= 32 => 48,
        <= 48 => 64,
        <= 64 => 128,
        _ => 256
    };

    private static bool TryGetShellBitmap(string path, int pixelSize, out IntPtr hBitmap)
    {
        hBitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            if (factory is null)
                return false;

            var size = new SIZE { cx = pixelSize, cy = pixelSize };
            // ICONONLY：图标；BIGGERSIZEOK：可用更大源图
            var hr = factory.GetImage(
                size,
                SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK,
                out hBitmap);

            Marshal.FinalReleaseComObject(factory);
            return hr == 0 && hBitmap != IntPtr.Zero;
        }
        catch
        {
            hBitmap = IntPtr.Zero;
            return false;
        }
    }

    private static bool TryGetImageListIcon(string path, int pixelSize, double dpiScale, out ImageSource? source)
    {
        source = null;
        var shInfo = new SHFILEINFO();
        var flags = SHGFI.SHGFI_SYSICONINDEX;
        var attrs = 0u;

        var himlSys = SHGetFileInfo(path, attrs, ref shInfo, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (himlSys == IntPtr.Zero && shInfo.iIcon == 0)
        {
            // 路径异常时再尝试按扩展名
            flags |= SHGFI.SHGFI_USEFILEATTRIBUTES;
            himlSys = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref shInfo, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (himlSys == IntPtr.Zero && shInfo.iIcon == 0)
                return false;
        }

        // 尽量取更大图再缩，避免糊
        var listSize = pixelSize <= 16 ? SHIL.SHIL_SMALL
            : pixelSize <= 32 ? SHIL.SHIL_LARGE
            : pixelSize <= 48 ? SHIL.SHIL_EXTRALARGE
            : SHIL.SHIL_JUMBO;

        var iidImageList = typeof(IImageList).GUID;
        if (SHGetImageList((int)listSize, ref iidImageList, out var imageList) != 0 || imageList is null)
            return false;

        try
        {
            imageList.GetIcon(shInfo.iIcon, ILD_TRANSPARENT, out var hIcon);
            if (hIcon == IntPtr.Zero)
                return false;

            try
            {
                var raw = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                raw.Freeze();
                source = NormalizeToDisplay(raw, pixelSize, dpiScale);
                return true;
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(imageList);
        }
    }

    private static ImageSource GetFallback(int pixelSize, double dpiScale)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(
                new SolidColorBrush(MediaColor.FromRgb(90, 140, 200)),
                null,
                new Rect(0, 0, pixelSize, pixelSize));
            dc.DrawRectangle(
                MediaBrushes.White,
                null,
                new Rect(pixelSize * 0.25, pixelSize * 0.25, pixelSize * 0.5, pixelSize * 0.5));
        }

        var dpi = 96.0 * Math.Max(dpiScale, 1.0);
        var bmp = new RenderTargetBitmap(pixelSize, pixelSize, dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const int ILD_TRANSPARENT = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10
    }

    [Flags]
    private enum SHGFI : uint
    {
        SHGFI_SYSICONINDEX = 0x000004000,
        SHGFI_USEFILEATTRIBUTES = 0x000000010
    }

    private enum SHIL
    {
        SHIL_LARGE = 0,
        SHIL_SMALL = 1,
        SHIL_EXTRALARGE = 2,
        SHIL_SYSSMALL = 3,
        SHIL_JUMBO = 4
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(ref IMAGELISTDRAWPARAMS pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGELISTDRAWPARAMS
    {
        public int cbSize;
        public IntPtr himl;
        public int i;
        public IntPtr hdcDst;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public int xBitmap;
        public int yBitmap;
        public int rgbBk;
        public int rgbFg;
        public int fStyle;
        public int dwRop;
        public int fState;
        public int Frame;
        public int crEffect;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        SHGFI uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
