using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopOrganizer.Models;
using DesktopOrganizer.Services;

namespace DesktopOrganizer;

public partial class ZoneWindow : Window
{
    private readonly Action _onChanged;
    private readonly Action<ZoneWindow> _onClosed;
    private Point? _resizeStart;
    private Size _resizeOriginSize;
    private Point _resizeOriginPos;
    private string? _resizeEdge;
    private bool _suppressSave;

    public ZoneData Data { get; }

    public ZoneWindow(ZoneData data, Action onChanged, Action<ZoneWindow> onClosed)
    {
        InitializeComponent();
        Data = data;
        _onChanged = onChanged;
        _onClosed = onClosed;

        _suppressSave = true;
        Left = data.Left;
        Top = data.Top;
        Width = data.Width;
        Height = data.Height;
        TitleText.Text = data.Title;
        TitleBox.Text = data.Title;
        SizeCombo.SelectedIndex = data.IconSize switch
        {
            IconSizeMode.Small => 0,
            IconSizeMode.Large => 2,
            _ => 1
        };
        _suppressSave = false;

        ApplyLockState();
        ReloadIcons();
        AllowDrop = true;
    }

    private void ApplyLockState()
    {
        var locked = Data.IsLocked;
        LockButton.Content = locked ? "开" : "锁";
        LockButton.ToolTip = locked ? "解锁窗口" : "锁定窗口（锁定后不可拖动/缩放）";
        LockButton.Foreground = locked
            ? new SolidColorBrush(Color.FromRgb(255, 200, 90))
            : Brushes.White;
        ResizeLayer.IsHitTestVisible = !locked;
        Header.Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        Data.IsLocked = !Data.IsLocked;
        ApplyLockState();
        NotifyChanged();
    }

    private void ReloadIcons()
    {
        IconPanel.Children.Clear();
        var (iconPx, tile) = SizeHelper.GetPixels(Data.IconSize);
        var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;

        foreach (var item in Data.Icons.ToList())
        {
            if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
                continue;

            var tileControl = CreateIconTile(item, iconPx, tile, dpiScale);
            IconPanel.Children.Add(tileControl);
        }
    }

    private Border CreateIconTile(IconData item, int iconPx, int tileWidth, double dpiScale)
    {
        var image = new Image
        {
            Width = iconPx,
            Height = iconPx,
            Stretch = Stretch.Uniform,
            Source = IconExtractor.GetIcon(item.Path, iconPx, dpiScale),
            Margin = new Thickness(0, 4, 0, 2),
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(image, EdgeMode.Unspecified);

        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(item.DisplayName)
                ? IconExtractor.GetDisplayName(item.Path)
                : item.DisplayName,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = Data.IconSize == IconSizeMode.Small ? 10 : 11,
            Foreground = Brushes.White,
            MaxWidth = tileWidth - 8,
            Margin = new Thickness(4, 0, 4, 4)
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { image, label }
        };

        var border = new Border
        {
            Width = tileWidth,
            Margin = new Thickness(2),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = stack,
            Tag = item,
            ToolTip = item.Path
        };

        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                OpenTarget(item.Path);
                e.Handled = true;
            }
        };

        border.MouseEnter += (_, _) =>
            border.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
        border.MouseLeave += (_, _) =>
            border.Background = Brushes.Transparent;

        var menu = new ContextMenu();
        var remove = new MenuItem { Header = item.HiddenFromDesktop ? "移出分区（还原到桌面）" : "移出分区" };
        remove.Click += (_, _) =>
        {
            DesktopIconStore.RestoreToDesktop(item);
            Data.Icons.Remove(item);
            ReloadIcons();
            NotifyChanged();
        };
        menu.Items.Add(remove);
        border.ContextMenu = menu;

        return border;
    }

    private static void OpenTarget(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开：{ex.Message}", "桌面图标整理", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NotifyChanged()
    {
        if (!_suppressSave)
            _onChanged();
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TitleBox.Visibility != Visibility.Visible)
            return;

        Data.Title = TitleBox.Text;
        TitleText.Text = TitleBox.Text;
        NotifyChanged();
    }

    private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            BeginRename();
            e.Handled = true;
        }
    }

    private void BeginRename()
    {
        TitleBox.Text = Data.Title;
        TitleText.Visibility = Visibility.Collapsed;
        TitleBox.Visibility = Visibility.Visible;
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void EndRename()
    {
        if (TitleBox.Visibility != Visibility.Visible)
            return;

        var name = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = "分区";

        Data.Title = name;
        TitleBox.Text = name;
        TitleText.Text = name;
        TitleBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        NotifyChanged();
    }

    private void TitleBox_LostFocus(object sender, RoutedEventArgs e) => EndRename();

    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EndRename();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TitleBox.Text = Data.Title;
            TitleBox.Visibility = Visibility.Collapsed;
            TitleText.Visibility = Visibility.Visible;
            e.Handled = true;
        }
    }

    private void SizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _suppressSave)
            return;

        Data.IconSize = SizeCombo.SelectedIndex switch
        {
            0 => IconSizeMode.Small,
            2 => IconSizeMode.Large,
            _ => IconSizeMode.Medium
        };
        ReloadIcons();
        NotifyChanged();
    }

    private bool _closingForAppExit;

    /// <summary>程序退出时关闭窗口，不触发“删除分区”。</summary>
    public void CloseForAppExit()
    {
        _closingForAppExit = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_closingForAppExit)
            return;

        var result = MessageBox.Show(
            "确定删除这个分区吗？\n\n已从桌面隐藏的图标会还原回桌面；非桌面拖入的引用只会从分区移除。",
            "桌面图标整理",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        DesktopIconStore.RestoreAll(Data.Icons);
        DesktopIconStore.CleanupZoneFolder(Data.Id);
        _onClosed(this);
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Data.IsLocked)
            return;

        if (e.OriginalSource is DependencyObject source)
        {
            if (FindAncestor<ComboBox>(source) is not null
                || FindAncestor<Button>(source) is not null
                || FindAncestor<TextBox>(source) is not null)
                return;

            // 点在标题文字上且准备双击改名时，不要开始拖动
            if (FindAncestor<TextBlock>(source) == TitleText && e.ClickCount > 1)
                return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // 忽略偶发拖动异常
            }

            PersistGeometry();
        }
    }

    private void PersistGeometry()
    {
        Data.Left = Left;
        Data.Top = Top;
        Data.Width = Width;
        Data.Height = Height;
        NotifyChanged();
    }

    // 不在 LocationChanged/SizeChanged 里实时落盘，只在拖动/缩放结束时保存，避免长时间拖动导致卡顿。

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        var added = false;
        foreach (var file in files)
        {
            if (IsAlreadyInZone(file))
                continue;

            try
            {
                var item = DesktopIconStore.Intake(file, Data.Id);
                Data.Icons.Add(item);
                added = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"无法加入分区：{Path.GetFileName(file)}\n{ex.Message}",
                    "桌面图标整理",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        if (!added)
            return;

        ReloadIcons();
        NotifyChanged();
    }

    private bool IsAlreadyInZone(string file)
    {
        string full;
        try
        {
            full = Path.GetFullPath(file);
        }
        catch
        {
            full = file;
        }

        return Data.Icons.Any(i =>
            string.Equals(i.Path, full, StringComparison.OrdinalIgnoreCase)
            || string.Equals(i.DesktopOriginPath, full, StringComparison.OrdinalIgnoreCase)
            || string.Equals(i.Path, file, StringComparison.OrdinalIgnoreCase)
            || string.Equals(i.DesktopOriginPath, file, StringComparison.OrdinalIgnoreCase));
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Resize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Data.IsLocked)
            return;

        if (sender is not FrameworkElement edge)
            return;

        _resizeEdge = edge.Tag as string;
        _resizeStart = PointToScreen(e.GetPosition(this));
        _resizeOriginSize = new Size(Width, Height);
        _resizeOriginPos = new Point(Left, Top);
        edge.CaptureMouse();
        e.Handled = true;
    }

    private void Resize_MouseMove(object sender, MouseEventArgs e)
    {
        if (_resizeStart is null || _resizeEdge is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = PointToScreen(e.GetPosition(this));
        var dx = current.X - _resizeStart.Value.X;
        var dy = current.Y - _resizeStart.Value.Y;
        const double min = 80;

        var left = _resizeOriginPos.X;
        var top = _resizeOriginPos.Y;
        var width = _resizeOriginSize.Width;
        var height = _resizeOriginSize.Height;

        if (_resizeEdge.Contains('E'))
            width = Math.Max(min, _resizeOriginSize.Width + dx);
        if (_resizeEdge.Contains('S'))
            height = Math.Max(min, _resizeOriginSize.Height + dy);
        if (_resizeEdge.Contains('W'))
        {
            width = Math.Max(min, _resizeOriginSize.Width - dx);
            left = _resizeOriginPos.X + (_resizeOriginSize.Width - width);
        }
        if (_resizeEdge.Contains('N'))
        {
            height = Math.Max(min, _resizeOriginSize.Height - dy);
            top = _resizeOriginPos.Y + (_resizeOriginSize.Height - height);
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    private void Resize_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement edge)
            edge.ReleaseMouseCapture();

        _resizeStart = null;
        _resizeEdge = null;
        PersistGeometry();
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
