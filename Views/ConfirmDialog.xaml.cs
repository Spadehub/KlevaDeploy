using System;
using System.IO;
using System.Media;
using System.Windows;

namespace KlevaDeploy.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();

        TitleText.Text = title;
        BodyText.Text = message;

        ConfirmButton.Click += (_, _) => DialogResult = true;
        CancelButton.Click += (_, _) => DialogResult = false;
        BtnClose.Click += (_, _) => DialogResult = false;
        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1) DragMove();
        };

        try
        {
            TryPlaySoftBeep();
        }
        catch
        {
        }
    }

    private static void TryPlaySoftBeep()
    {
        var wav = CreateSoftBeepWav(sampleRate: 44100, durationMs: 40, frequencyHz: 880, amplitude: 0.03);
        using var stream = new MemoryStream(wav, writable: false);
        using var player = new SoundPlayer(stream);
        player.Load();
        player.Play();
    }

    private static byte[] CreateSoftBeepWav(int sampleRate, int durationMs, int frequencyHz, double amplitude)
    {
        var samples = (int)(sampleRate * (durationMs / 1000.0));
        var bytesPerSample = 2;
        var dataSize = samples * bytesPerSample;

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        bw.Write(36 + dataSize);
        bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
        bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(sampleRate);
        bw.Write(sampleRate * bytesPerSample);
        bw.Write((short)bytesPerSample);
        bw.Write((short)16);
        bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        bw.Write(dataSize);

        var amp = Math.Clamp(amplitude, 0, 1) * short.MaxValue;
        var w = 2 * Math.PI * frequencyHz;

        for (var i = 0; i < samples; i++)
        {
            var t = (double)i / sampleRate;
            var envelope = Math.Sin(Math.PI * i / (samples - 1));
            var value = (short)(amp * envelope * Math.Sin(w * t));
            bw.Write(value);
        }

        return ms.ToArray();
    }
}
