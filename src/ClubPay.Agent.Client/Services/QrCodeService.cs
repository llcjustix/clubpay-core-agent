using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace ClubPay.Agent.Client.Services;

public sealed class QrCodeService(ILogger<QrCodeService> logger)
{
    public BitmapImage? Generate(string content, int pixelSize = 300)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
            using var qr = new PngByteQRCode(data);

            int moduleSize = Math.Max(1, pixelSize / data.ModuleMatrix.Count);
            byte[] bytes = qr.GetGraphic(moduleSize);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "QR generatsiya xatosi: {Content}", content);
            return null;
        }
    }

    public BitmapImage? GenerateWifi(string ssid, string password = "", int pixelSize = 108)
    {
        var wifiString = string.IsNullOrEmpty(password)
            ? $"WIFI:T:nopass;S:{ssid};;"
            : $"WIFI:T:WPA;S:{ssid};P:{password};;";
        return Generate(wifiString, pixelSize);
    }
}
