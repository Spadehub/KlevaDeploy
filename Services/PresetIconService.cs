using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class PresetIconService : IPresetIconService
{
    private const int IconSize = 96;

    private readonly string _storageRootDir;
    private readonly string _presetIconsRootDir;
    private readonly string _libraryDir;
    private readonly string _libraryIndexPath;
    private List<PresetIconLibraryItem> _libraryIcons = new();

    public PresetIconService()
    {
        _storageRootDir = Path.Combine(AppContext.BaseDirectory, "Data", "preset_icons");
        _presetIconsRootDir = Path.Combine(_storageRootDir, "presets");
        _libraryDir = Path.Combine(_storageRootDir, "library");
        _libraryIndexPath = Path.Combine(_storageRootDir, "user_icons.json");

        Directory.CreateDirectory(_storageRootDir);
        Directory.CreateDirectory(_presetIconsRootDir);
        Directory.CreateDirectory(_libraryDir);
        LoadLibrary();
    }

    public string ImportLightIcon(string presetId, string sourcePath) =>
        Import(presetId, sourcePath, "light");

    public string ImportDarkIcon(string presetId, string sourcePath) =>
        Import(presetId, sourcePath, "dark");

    public void DeletePresetIcons(string presetId)
    {
        var dir = GetPresetDir(presetId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public IReadOnlyList<PresetIconLibraryItem> GetLibraryIcons() =>
        _libraryIcons.ToList();

    public PresetIconLibraryItem ImportLibraryIcon(string sourcePath)
    {
        ValidateSourcePath(sourcePath);

        var id = Guid.NewGuid().ToString("N");
        var destAbsPath = Path.Combine(_libraryDir, $"{id}.png");
        SaveAsPng(sourcePath, destAbsPath, IconSize);

        var item = new PresetIconLibraryItem
        {
            Id = id,
            LightPath = Path.GetRelativePath(AppContext.BaseDirectory, destAbsPath),
            DarkPath = null
        };

        _libraryIcons.Insert(0, item);
        SaveLibrary();
        return item;
    }

    private string Import(string presetId, string sourcePath, string variant)
    {
        ValidateSourcePath(sourcePath);

        var presetDir = GetPresetDir(presetId);
        Directory.CreateDirectory(presetDir);

        var destAbsPath = Path.Combine(presetDir, $"icon_{variant}.png");
        SaveAsPng(sourcePath, destAbsPath, IconSize);
        return Path.GetRelativePath(AppContext.BaseDirectory, destAbsPath);
    }

    private string GetPresetDir(string presetId) => Path.Combine(_presetIconsRootDir, presetId);

    private void LoadLibrary()
    {
        try
        {
            if (!File.Exists(_libraryIndexPath))
            {
                _libraryIcons = new();
                return;
            }

            var json = File.ReadAllText(_libraryIndexPath);
            _libraryIcons = JsonSerializer.Deserialize<List<PresetIconLibraryItem>>(json) ?? new();
        }
        catch
        {
            _libraryIcons = new();
        }
    }

    private void SaveLibrary()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_libraryIcons, options);
        File.WriteAllText(_libraryIndexPath, json);
    }

    private static void ValidateSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Icon file not found.");
        }

        var ext = (Path.GetExtension(sourcePath) ?? string.Empty).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".ico"))
        {
            throw new InvalidDataException("Unsupported icon file type.");
        }
    }

    private static void SaveAsPng(string sourcePath, string destAbsPath, int size)
    {
        BitmapFrame frame;
        using (var fs = File.OpenRead(sourcePath))
        {
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
        }

        var scale = Math.Min((double)size / frame.PixelWidth, (double)size / frame.PixelHeight);
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1.0;

        var scaledW = frame.PixelWidth * scale;
        var scaledH = frame.PixelHeight * scale;
        var x = (size - scaledW) / 2;
        var y = (size - scaledH) / 2;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
            dc.DrawImage(frame, new Rect(x, y, scaledW, scaledH));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();

        Directory.CreateDirectory(Path.GetDirectoryName(destAbsPath)!);
        using var outStream = File.Create(destAbsPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        encoder.Save(outStream);
    }
}
