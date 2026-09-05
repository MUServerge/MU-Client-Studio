using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using MUClientStudio.Models.Formats.Bmd;
using MUClientStudio.Models.Player;
using MUClientStudio.Models.Textures;
using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace MUClientStudio.Rendering.Player;

public sealed record BmdStaticPreviewScene(
    Model3DGroup Model,
    Rect3D Bounds,
    int RenderedTriangles,
    int SkippedTriangles,
    int RenderedParts,
    int LoadedTextures,
    int MissingTextures);

public sealed class BmdStaticPreviewBuilder
{
    public BmdStaticPreviewScene Build(
        PlayerCharacterSource character,
        int actionIndex = 0,
        int frameIndex = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);

        var poses = BuildPoses(
            character.SkeletonDocument,
            character.AnimationDocument,
            actionIndex,
            frameIndex);

        var model = new Model3DGroup();
        var renderedTriangles = 0;
        var skippedTriangles = 0;
        var renderedParts = 0;
        var loadedTextures = 0;
        var missingTextures = 0;
        var bitmapCache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in character.BodyParts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partRendered = false;

            for (var meshIndex = 0; meshIndex < part.Document.Meshes.Count; meshIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mesh = part.Document.Meshes[meshIndex];
                var geometry = new MeshGeometry3D();

                foreach (var triangle in mesh.Triangles)
                {
                    if (TryAppendTriangle(mesh, triangle, poses, geometry, 0, 2, 1))
                        renderedTriangles++;
                    else
                        skippedTriangles++;

                    if (triangle.Polygon == 4)
                    {
                        if (TryAppendTriangle(mesh, triangle, poses, geometry, 0, 2, 3))
                            renderedTriangles++;
                        else
                            skippedTriangles++;
                    }
                }

                if (geometry.Positions.Count == 0)
                    continue;

                geometry.Freeze();

                var texture = meshIndex < part.MeshTextures.Count
                    ? part.MeshTextures[meshIndex]
                    : null;
                var material = BuildMaterial(texture, bitmapCache, out var hasTexture);
                if (hasTexture)
                    loadedTextures++;
                else if (!string.IsNullOrWhiteSpace(mesh.TexturePath))
                    missingTextures++;

                var geometryModel = new GeometryModel3D(geometry, material)
                {
                    BackMaterial = material
                };
                geometryModel.Freeze();
                model.Children.Add(geometryModel);
                partRendered = true;
            }

            if (partRendered)
                renderedParts++;
        }

        var sceneTransform = new RotateTransform3D(
            new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90));
        sceneTransform.Freeze();
        model.Transform = sceneTransform;

        var bounds = model.Bounds;
        model.Freeze();

        return new BmdStaticPreviewScene(
            model,
            bounds,
            renderedTriangles,
            skippedTriangles,
            renderedParts,
            loadedTextures,
            missingTextures);
    }

    private static Material BuildMaterial(
        MuTextureAsset? texture,
        IDictionary<string, BitmapSource> cache,
        out bool hasTexture)
    {
        Brush diffuseBrush;
        hasTexture = false;

        if (texture is not null)
        {
            try
            {
                if (!cache.TryGetValue(texture.SourcePath, out var bitmap))
                {
                    bitmap = CreateBitmap(texture);
                    cache[texture.SourcePath] = bitmap;
                }

                var imageBrush = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.Fill,
                    TileMode = TileMode.None
                };
                imageBrush.Freeze();
                diffuseBrush = imageBrush;
                hasTexture = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or IOException or ArgumentException)
            {
                var fallback = new SolidColorBrush(Color.FromRgb(168, 180, 194));
                fallback.Freeze();
                diffuseBrush = fallback;
            }
        }
        else
        {
            var fallback = new SolidColorBrush(Color.FromRgb(168, 180, 194));
            fallback.Freeze();
            diffuseBrush = fallback;
        }

        var diffuse = new DiffuseMaterial(diffuseBrush);
        diffuse.Freeze();

        var specularBrush = new SolidColorBrush(Color.FromRgb(65, 70, 78));
        specularBrush.Freeze();
        var specular = new SpecularMaterial(specularBrush, 10);
        specular.Freeze();

        var material = new MaterialGroup();
        material.Children.Add(diffuse);
        material.Children.Add(specular);
        material.Freeze();
        return material;
    }

    private static BitmapSource CreateBitmap(MuTextureAsset texture)
    {
        if (texture.PayloadKind == MuTexturePayloadKind.Bgra32)
        {
            if (texture.Width <= 0 || texture.Height <= 0 || texture.Data.Length < texture.Width * texture.Height * 4)
                throw new InvalidDataException("Invalid BGRA texture payload.");

            var rawPixels = texture.FlipVertical
                ? FlipRows(texture.Data, texture.Width, texture.Height)
                : texture.Data;

            var bitmap = BitmapSource.Create(
                texture.Width,
                texture.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                rawPixels,
                texture.Width * 4);
            bitmap.Freeze();
            return bitmap;
        }

        using var stream = new MemoryStream(texture.Data, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("Encoded texture contains no image frame.");

        BitmapSource source = decoder.Frames[0];
        if (source.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();
            source = converted;
        }

        var stride = checked(source.PixelWidth * 4);
        var decodedPixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(decodedPixels, stride, 0);
        if (texture.FlipVertical)
            decodedPixels = FlipRows(decodedPixels, source.PixelWidth, source.PixelHeight);

        var bitmapSource = BitmapSource.Create(
            source.PixelWidth,
            source.PixelHeight,
            source.DpiX > 0 ? source.DpiX : 96,
            source.DpiY > 0 ? source.DpiY : 96,
            PixelFormats.Bgra32,
            null,
            decodedPixels,
            stride);
        bitmapSource.Freeze();
        return bitmapSource;
    }

    private static byte[] FlipRows(byte[] source, int width, int height)
    {
        var stride = checked(width * 4);
        var output = new byte[checked(stride * height)];
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(source, y * stride, output, (height - 1 - y) * stride, stride);
        return output;
    }

    private static BonePose[] BuildPoses(
        BmdDocument skeletonDocument,
        BmdDocument animationDocument,
        int actionIndex,
        int frameIndex)
    {
        var poses = new BonePose[skeletonDocument.Bones.Count];

        for (var index = 0; index < skeletonDocument.Bones.Count; index++)
        {
            var skeletonBone = skeletonDocument.Bones[index];
            if (skeletonBone.IsDummy)
            {
                poses[index] = new BonePose(skeletonBone.ParentIndex, Vector3.Zero, Quaternion.Identity);
                continue;
            }

            var animationBone = index < animationDocument.Bones.Count
                ? animationDocument.Bones[index]
                : skeletonBone;

            if (animationBone.IsDummy || animationBone.Animations.Count == 0)
            {
                poses[index] = new BonePose(skeletonBone.ParentIndex, Vector3.Zero, Quaternion.Identity);
                continue;
            }

            var safeAction = Math.Clamp(actionIndex, 0, animationBone.Animations.Count - 1);
            var animation = animationBone.Animations[safeAction];
            var position = animation.Positions.Count == 0
                ? Vector3.Zero
                : ToVector(animation.Positions[Math.Clamp(frameIndex, 0, animation.Positions.Count - 1)]);
            var rotation = animation.Rotations.Count == 0
                ? Vector3.Zero
                : ToVector(animation.Rotations[Math.Clamp(frameIndex, 0, animation.Rotations.Count - 1)]);

            poses[index] = new BonePose(
                skeletonBone.ParentIndex,
                position,
                EulerToQuaternion(rotation));
        }

        return poses;
    }

    private static bool TryAppendTriangle(
        BmdMesh mesh,
        BmdTriangle triangle,
        IReadOnlyList<BonePose> poses,
        MeshGeometry3D geometry,
        int cornerA,
        int cornerB,
        int cornerC)
    {
        Span<int> corners = stackalloc int[3] { cornerA, cornerB, cornerC };
        var pendingPositions = new Point3D[3];
        var pendingNormals = new Vector3D[3];
        var pendingUvs = new Point[3];

        for (var outputIndex = 0; outputIndex < 3; outputIndex++)
        {
            var corner = corners[outputIndex];
            if (corner >= triangle.VertexIndices.Count || corner >= triangle.NormalIndices.Count || corner >= triangle.TexCoordIndices.Count)
                return false;

            var vertexIndex = triangle.VertexIndices[corner];
            var normalIndex = triangle.NormalIndices[corner];
            var texCoordIndex = triangle.TexCoordIndices[corner];
            if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count ||
                normalIndex < 0 || normalIndex >= mesh.Normals.Count ||
                texCoordIndex < 0 || texCoordIndex >= mesh.TexCoords.Count)
                return false;

            var vertex = mesh.Vertices[vertexIndex];
            var normal = mesh.Normals[normalIndex];
            var uv = mesh.TexCoords[texCoordIndex];

            var position = TransformPosition(ToVector(vertex.Position), vertex.BoneIndex, poses);
            var transformedNormal = TransformNormal(ToVector(normal.Normal), vertex.BoneIndex, poses);

            pendingPositions[outputIndex] = new Point3D(position.X, position.Y, position.Z);
            pendingNormals[outputIndex] = new Vector3D(transformedNormal.X, transformedNormal.Y, transformedNormal.Z);
            pendingUvs[outputIndex] = new Point(uv.U, 1.0 - uv.V);
        }

        var firstIndex = geometry.Positions.Count;
        for (var i = 0; i < 3; i++)
        {
            geometry.Positions.Add(pendingPositions[i]);
            geometry.Normals.Add(pendingNormals[i]);
            geometry.TextureCoordinates.Add(pendingUvs[i]);
            geometry.TriangleIndices.Add(firstIndex + i);
        }

        return true;
    }

    private static Vector3 TransformPosition(Vector3 value, short boneIndex, IReadOnlyList<BonePose> poses)
    {
        if (boneIndex < 0 || boneIndex >= poses.Count)
            return value;

        var result = value;
        var current = boneIndex;
        var guard = 0;

        while (current >= 0 && current < poses.Count && guard++ <= poses.Count)
        {
            var pose = poses[current];
            result = Vector3.Transform(result, pose.Rotation) + pose.Position;
            current = pose.ParentIndex;
        }

        return result;
    }

    private static Vector3 TransformNormal(Vector3 value, short boneIndex, IReadOnlyList<BonePose> poses)
    {
        if (boneIndex < 0 || boneIndex >= poses.Count)
            return NormalizeSafe(value);

        var result = value;
        var current = boneIndex;
        var guard = 0;

        while (current >= 0 && current < poses.Count && guard++ <= poses.Count)
        {
            var pose = poses[current];
            result = Vector3.Transform(result, pose.Rotation);
            current = pose.ParentIndex;
        }

        return NormalizeSafe(result);
    }

    private static Quaternion EulerToQuaternion(Vector3 euler)
    {
        var halfX = euler.X * 0.5f;
        var halfY = euler.Y * 0.5f;
        var halfZ = euler.Z * 0.5f;

        var sinX = MathF.Sin(halfX);
        var cosX = MathF.Cos(halfX);
        var sinY = MathF.Sin(halfY);
        var cosY = MathF.Cos(halfY);
        var sinZ = MathF.Sin(halfZ);
        var cosZ = MathF.Cos(halfZ);

        var quaternion = new Quaternion(
            sinX * cosY * cosZ - cosX * sinY * sinZ,
            cosX * sinY * cosZ + sinX * cosY * sinZ,
            cosX * cosY * sinZ - sinX * sinY * cosZ,
            cosX * cosY * cosZ + sinX * sinY * sinZ);

        return Quaternion.Normalize(quaternion);
    }

    private static Vector3 NormalizeSafe(Vector3 value)
    {
        var lengthSquared = value.LengthSquared();
        return lengthSquared > 0.000001f ? Vector3.Normalize(value) : Vector3.UnitZ;
    }

    private static Vector3 ToVector(BmdVector3 value) => new(value.X, value.Y, value.Z);

    private readonly record struct BonePose(short ParentIndex, Vector3 Position, Quaternion Rotation);
}
