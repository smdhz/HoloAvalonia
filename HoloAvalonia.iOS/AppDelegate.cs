using Avalonia;
using Avalonia.iOS;
using Foundation;

namespace HoloAvalonia;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    /// <summary>
    /// Adds shared fonts to the iOS app builder.
    /// </summary>
    /// <param name="builder">The platform app builder.</param>
    /// <returns>The customized <see cref="AppBuilder"/> instance.</returns>
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .WithInterFont();
}
