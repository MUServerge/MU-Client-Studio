using MUClientStudio.Models.Formats.Bmd;
using MUClientStudio.Models.Textures;

namespace MUClientStudio.Models.Player;

public sealed record PlayerBodyPartSource(
    string Slot,
    string RelativePath,
    BmdDocument Document,
    IReadOnlyList<MuTextureAsset?> MeshTextures);

public sealed record PlayerCharacterSource(
    PlayerClassDefinition Definition,
    BmdDocument SkeletonDocument,
    BmdDocument AnimationDocument,
    IReadOnlyList<PlayerBodyPartSource> BodyParts,
    IReadOnlyList<string> Diagnostics)
{
    public int MeshCount => BodyParts.Sum(part => part.Document.MeshCount);
    public int BoneCount => SkeletonDocument.BoneCount;
    public int ActionCount => AnimationDocument.ActionCount;
    public int LoadedTextureCount => BodyParts.Sum(part => part.MeshTextures.Count(texture => texture is not null));
    public int TextureCount => BodyParts.Sum(part => part.MeshTextures.Count);
}
