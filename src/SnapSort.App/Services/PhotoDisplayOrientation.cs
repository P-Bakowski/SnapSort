using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapSort.App.Services;

public static class PhotoDisplayOrientation
{
    public static DisplayOrientation GetDisplayOrientation(int exifOrientation) => exifOrientation switch
    {
        2 => new(2, 0, true, false),
        3 => new(3, 180, false, false),
        4 => new(4, 0, false, true),
        5 => new(5, 270, true, false),
        6 => new(6, 90, false, false),
        7 => new(7, 90, true, false),
        8 => new(8, 270, false, false),
        _ => new(1, 0, false, false)
    };

    public static int ReadExifOrientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata)
            return 1;

        foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                if (metadata.ContainsQuery(query))
                    return Convert.ToInt32(metadata.GetQuery(query));
            }
            catch
            {
            }
        }

        return 1;
    }

    public static BitmapSource ApplyForDisplay(BitmapSource image, int exifOrientation)
    {
        var orientation = GetDisplayOrientation(exifOrientation);
        if (orientation is { RotationDegrees: 0, FlipHorizontal: false, FlipVertical: false })
            return image;

        var transforms = new TransformGroup();
        if (orientation.FlipHorizontal || orientation.FlipVertical)
            transforms.Children.Add(new ScaleTransform(orientation.FlipHorizontal ? -1 : 1, orientation.FlipVertical ? -1 : 1));
        if (orientation.RotationDegrees != 0)
            transforms.Children.Add(new RotateTransform(orientation.RotationDegrees));

        var transformed = new TransformedBitmap(image, transforms);
        transformed.Freeze();
        return transformed;
    }
}

public readonly record struct DisplayOrientation(
    int ExifOrientation,
    int RotationDegrees,
    bool FlipHorizontal,
    bool FlipVertical)
{
    public bool SwapsDimensions => RotationDegrees is 90 or 270;
}
