using UIKit;

namespace HoloAvalonia;

internal class Program
{
    /// <summary>
    /// Starts the UIKit application with the Avalonia app delegate.
    /// </summary>
    /// <param name="args">Application startup arguments.</param>
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
