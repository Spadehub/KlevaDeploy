using System.IO;
using System.Threading;
using System.Windows.Media.Imaging;
using KlevaDeploy.Services;

namespace KlevaDeploy.Tests;

public sealed class IconLibraryTests
{
    [Fact]
    public void ImportLibraryIcon_CreatesPng96x96UnderDataFolder()
    {
        RunOnStaThread(() =>
        {
            var tempSourcePath = Path.Combine(Path.GetTempPath(), $"klevadeploy_icon_test_{Guid.NewGuid():N}.png");
            try
            {
                File.WriteAllBytes(tempSourcePath, OneByOneTransparentPng);

                var service = new PresetIconService();
                var item = service.ImportLibraryIcon(tempSourcePath);

                Assert.False(string.IsNullOrWhiteSpace(item.LightPath));

                var absPath = Path.IsPathRooted(item.LightPath!)
                    ? item.LightPath!
                    : Path.Combine(AppContext.BaseDirectory, item.LightPath!);

                Assert.True(File.Exists(absPath), $"Icon file not found: {absPath}");

                using var fs = File.OpenRead(absPath);
                var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                Assert.Equal(96, frame.PixelWidth);
                Assert.Equal(96, frame.PixelHeight);
            }
            finally
            {
                try { File.Delete(tempSourcePath); } catch { }
            }
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }
    }

    private static readonly byte[] OneByOneTransparentPng =
    [
        137, 80, 78, 71, 13, 10, 26, 10,
        0, 0, 0, 13, 73, 72, 68, 82,
        0, 0, 0, 1, 0, 0, 0, 1,
        8, 6, 0, 0, 0, 31, 21, 196, 137,
        0, 0, 0, 13, 73, 68, 65, 84,
        120, 156, 99, 96, 0, 0, 0, 2, 0, 1,
        229, 39, 212, 161,
        0, 0, 0, 0, 73, 69, 78, 68,
        174, 66, 96, 130
    ];
}

