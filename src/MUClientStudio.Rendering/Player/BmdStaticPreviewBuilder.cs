using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MUClientStudio.Models.Formats.Bmd;

namespace MUClientStudio.Rendering.Player;

public sealed record BmdStaticPreviewScene(
    Model3DGroup Model,
    Rect3D Bounds,
    int RenderedTriangles,
    int SkippedTriangles);

public sealed class BmdStaticPreviewBuilder
{
    public BmdStaticPreviewScene Build(
        BmdDocument document,
        int actionIndex = 0,
        int frameIndex = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var poses = BuildPoses(document, actionIndex, frameIndex);
        var model = new Model3DGroup();
        var renderedTriangles = 0;
        var skippedTriangles = 0;

        foreach (var mesh in document.Meshes)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            var diffuseBrush = new SolidColorBrush(Color.FromRgb(168, 180, 194));
            diffuseBrush.Freeze();
            var specularBrush = new SolidColorBrush(Color.FromRgb(90, 98, 108));
            specularBrush.Freeze();

            var diffuse = new DiffuseMaterial(diffuseBrush);
            diffuse.Freeze();
            var specular = new SpecularMaterial(specularBrush, 20);
            specular.Freeze();

            var material = new MaterialGroup();
            material.Children.Add(diffuse);
            material.Children.Add(specular);
            material.Freeze();

            var geometryModel = new GeometryModel3D(geometry, material)
            {
                BackMaterial = material
            };
            geometryModel.Freeze();
            model.Children.Add(geometryModel);
        }

        var sceneTransform = new RotateTransform3D(
            new AxisAngleRotation3D(new Vector3D(1, 0, 0), -90));
        sceneTransform.Freeze();
        model.Transform = sceneTransform;

        var bounds = model.Bounds;
        model.Freeze();

        return new BmdStaticPreviewScene(model, bounds, renderedTriangles, skippedTriangles);
    }

    private static BonePose[] BuildPoses(BmdDocument document, int actionIndex, int frameIndex)
    {
        var poses = new BonePose[document.Bones.Count];

        for (var index = 0; index < document.Bones.Count; index++)
        {
            var bone = document.Bones[index];
            if (bone.IsDummy || bone.Animations.Count == 0)
            {
                poses[index] = new BonePose(bone.ParentIndex, Vector3.Zero, Quaternion.Identity);
                continue;
            }

            var safeAction = Math.Clamp(actionIndex, 0, bone.Animations.Count - 1);
            var animation = bone.Animations[safeAction];
            var position = animation.Positions.Count == 0
                ? Vector3.Zero
                : ToVector(animation.Positions[Math.Clamp(frameIndex, 0, animation.Positions.Count - 1)]);
            var rotation = animation.Rotations.Count == 0
                ? Vector3.Zero
                : ToVector(animation.Rotations[Math.Clamp(frameIndex, 0, animation.Rotations.Count - 1)]);

            poses[index] = new BonePose(bone.ParentIndex, position, EulerToQuaternion(rotation));
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
            pendingUvs[outputIndex] = new Point(uv.U, uv.V);
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
