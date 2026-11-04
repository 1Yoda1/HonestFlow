using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HonestFlow.UI;
using Xunit;
using Xunit.Abstractions;

namespace HonestFlow.Tests;

[Collection("MainWindow WPF fixtures")]
public sealed class MainWindowMemoryTests(ITestOutputHelper output)
{
    [Fact]
    public void TopologyBitmaps_HaveBoundedDecodedPixelBudget_AndOutputHasNoUndoHistory()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(); // No startup, network, probes, identity or installers.
                try
                {
                    long pixelBytes = 0;
                    foreach (string name in new[] { "Gismt", "Esm", "Controller", "Lm", "Kkt" })
                    {
                        var brush = (ImageBrush)window.Resources[name + "TopologyIconMask"];
                        var image = Assert.IsType<BitmapImage>(brush.ImageSource);
                        Assert.InRange(image.PixelWidth, 1, 128);
                        Assert.InRange(image.PixelHeight, 1, 128);
                        pixelBytes += (long)image.PixelWidth * image.PixelHeight * image.Format.BitsPerPixel / 8;
                    }
                    Assert.True(pixelBytes <= 512 * 1024);
                    output.WriteLine($"Decoded topology pixel payload: {pixelBytes:N0} bytes (previous 32-bit payload: 31,450,320 bytes).");
                    var text = (TextBox)typeof(MainWindow).GetField("SectionOutput", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    Assert.False(text.IsUndoEnabled);
                }
                finally { window.Close(); }
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
