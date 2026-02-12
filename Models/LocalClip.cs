using System.Windows.Media.Imaging;

namespace yShorts.Models;

public class LocalClip
{
    public string FilePath { get; set; } = string.Empty;
    public BitmapSource? Thumbnail { get; set; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
}
