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
    /// 按 DIP 显示尺寸与 DPI 提取清晰图标（优先 Shell 高清图，避免 32px 被放大发糊）。
    /// </summary>
    public static ImageSource GetIcon(string path, int dipSize, double dpiScale = 1.0)
    {
        var pixelSize = Math.Clamp((int)Math.Ceiling(dipSize * Math.Max(dpiScale, 1.0)), 16, 256);

        try
        {
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return GetFallback(pixelSize, dpiScale);

            // 对 .lnk 直接用快捷方式路径，让 Shell 解析图标，更清晰也更准确
            if (TryGetShellBitmap(path, pixelSize, out var hBitmap) && hBitmap != IntPtr.Zero)
            {
                try
                {
                    var bmp = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bmp.Freeze();
                    return bmp;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }

            // 回退：从系统图像列表取大图标
            if (TryGetImageListIcon(path, pixelSize, out var imageListIcon) && imageListIcon is not null)
                return imageListIcon;

            return GetFallback(pixelSize, dpiScale);
        }
        catch
        {
            return GetFallback(pixelSize, dpiScale);
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
            // ICONONLY：只要图标不要缩略图；BIGGERSIZEOK：允许更大源再缩
            var hr = factory.GetImage(
                size,
                SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_RESIZETOFIT,
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

    private static bool TryGetImageListIcon(string path, int pixelSize, out ImageSource? source)
    {
        source = null;
        var shInfo = new SHFILEINFO();
        var flags = SHGFI.SHGFI_SYSICONINDEX | SHGFI.SHGFI_USEFILEATTRIBUTES;
        var attrs = File.Exists(path) || Directory.Exists(path) ? 0u : FILE_ATTRIBUTE_NORMAL;

        var himlSys = SHGetFileInfo(path, attrs, ref shInfo, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (himlSys == IntPtr.Zero && shInfo.iIcon == 0)
            return false;

        var listSize = pixelSize switch
        {
            <= 16 => SHIL.SHIL_SMALL,
            <= 32 => SHIL.SHIL_LARGE,
            <= 48 => SHIL.SHIL_EXTRALARGE,
            _ => SHIL.SHIL_JUMBO
        };

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
                source = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(pixelSize, pixelSize));
                source.Freeze();
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
