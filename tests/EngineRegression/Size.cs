// Engine.cs only uses this value type for arithmetic; no WinUI window is created.
namespace Windows.Foundation;
internal readonly struct Size(double width, double height) {
    internal double Width { get; } = width;
    internal double Height { get; } = height;
}
