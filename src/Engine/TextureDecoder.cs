using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Crowbar.Engine;

internal static class TextureDecoder
{
    internal static ModelTexture? Decode(byte[] encoded)
    {
        try
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(encoded);
            byte[] pixels = new byte[checked(image.Width * image.Height * 4)];
            image.CopyPixelDataTo(pixels);
            return new ModelTexture { Width = image.Width, Height = image.Height, Pixels = pixels };
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
    }
}
