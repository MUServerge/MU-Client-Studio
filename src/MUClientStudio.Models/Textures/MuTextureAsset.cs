namespace MUClientStudio.Models.Textures;

public enum MuTexturePayloadKind
{
    EncodedImage,
    Bgra32
}

public sealed record MuTextureAsset(
    string SourcePath,
    MuTexturePayloadKind PayloadKind,
    byte[] Data,
    int Width = 0,
    int Height = 0,
    bool FlipVertical = false)
{
    public int Stride => Width > 0 ? checked(Width * 4) : 0;
}
