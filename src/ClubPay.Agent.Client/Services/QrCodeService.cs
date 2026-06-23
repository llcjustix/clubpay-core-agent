using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace ClubPay.Agent.Client.Services;

public sealed class QrCodeService
{
    public BitmapImage Generate(string content, int pixelSize = 300)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var qr = new BitmapByteQRCode(data);

        var bytes = qr.GetGraphic(pixelSize / data.ModuleMatrix.Count);

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = pixelSize;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public BitmapImage GenerateWifi(string ssid, string password = "", int pixelSize = 108)
    {
        var wifiString = string.IsNullOrEmpty(password)
            ? $"WIFI:T:nopass;S:{ssid};;"
            : $"WIFI:T:WPA;S:{ssid};P:{password};;";
        return Generate(wifiString, pixelSize);
    }
}
