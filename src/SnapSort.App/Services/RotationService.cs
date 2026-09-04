using System.IO;
using System.Windows.Media.Imaging;

namespace SnapSort.App.Services;

public sealed class RotationService
{
    public async Task<(bool Success, string Message)> RotateAsync(string path, int degrees, CancellationToken token)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return await Task.Run(() => RotateJpegMetadata(path, degrees), token);

        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return await Task.Run(() => RotateBitmapLossless(path, degrees), token);

        return (false, "Ten format nie ma jeszcze bezpiecznego obrotu bez utraty jakości. Użyj kopii pliku albo narzędzia obsługującego bezstratny obrót tego formatu.");
    }

    private static (bool Success, string Message) RotateJpegMetadata(string path, int degrees)
    {
        try
        {
            var turns = ((degrees % 360) + 360) % 360 / 90;
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
            var orientation = PhotoDisplayOrientation.ReadExifOrientation(decoder.Frames[0]);
            for (var i = 0; i < turns; i++)
                orientation = RotateExifClockwise(orientation);

            var writer = decoder.Frames[0].CreateInPlaceBitmapMetadataWriter();
            writer.SetQuery("/app1/ifd/{ushort=274}", (ushort)orientation);
            return writer.TrySave()
                ? (true, "Obrócono bezstratnie przez metadane EXIF.")
                : (false, "Ten JPEG nie pozwala zapisać orientacji bez przebudowy obrazu. Plik nie został zmieniony.");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "JPEG metadata rotation");
            return (false, "Nie udało się bezpiecznie zmienić orientacji JPEG. Plik nie został zmieniony.");
        }
    }

    private static int RotateExifClockwise(int orientation) => orientation switch
    {
        1 => 6,
        6 => 3,
        3 => 8,
        8 => 1,
        2 => 7,
        7 => 4,
        4 => 5,
        5 => 2,
        _ => 6
    };

    private static (bool Success, string Message) RotateBitmapLossless(string path, int degrees)
    {
        using var input = File.OpenRead(path);
        var image = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var rotated = BitmapFrame.Create(new TransformedBitmap(image, new System.Windows.Media.RotateTransform(degrees)));
        BitmapEncoder encoder = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? new PngBitmapEncoder()
            : new BmpBitmapEncoder();
        encoder.Frames.Add(rotated);

        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}{Path.GetExtension(path)}");
        using (var output = File.Create(temp))
            encoder.Save(output);
        File.Move(temp, path, true);
        return (true, "Obrócono bez utraty jakości.");
    }

}
