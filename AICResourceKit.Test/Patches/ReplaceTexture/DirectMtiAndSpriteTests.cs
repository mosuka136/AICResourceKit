using AICResourceKit.Patches.ReplaceTexture;
using System.Runtime.CompilerServices;
using UnityEngine;
using XX;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class DirectMtiAndSpriteTests
    {
        [Theory]
        [InlineData("MTI_title", "key_noel")]
        [InlineData("MTI_title", "difficulty")]
        // 这里只验证通用身份匹配；wplmode_ 的实际调用容器仍需游戏证据。
        [InlineData("MTI_title_wpl", "wplmode_")]
        public void DirectImage_MatchesOnlyTheConfiguredContainerAndKey(string container, string imageKey)
        {
            var package = new ReplacementPackage { Id = "title" };
            var target = new ReplacementTarget
            {
                Type = "texture", Loader = "mti", AssetKey = container, ImageKey = imageKey,
                Image = "candidate.png", Owner = package, PackageId = package.Id
            };
            package.Targets.Add(target);
            var catalog = new ReplacementCatalog();
            catalog.Packages.Add(package);
            var selection = new ReplacementSelection(catalog, new[] { package.Id }, true, false);

            Assert.Same(target, selection.Texture("mti", container, imageKey, null));
            Assert.Null(selection.Texture("mti", "Other/" + container, imageKey, null));
            Assert.Null(selection.Texture("mti", container, imageKey.ToUpperInvariant(), null));
            Assert.Null(selection.Texture("mti", container, "key_bg", null));
        }

        [Fact]
        public void DirectImage_LeavesSingleImageContainersWithTheirExistingEntry()
        {
            // 不启动 Unity 加载；这里只检查两个入口的归属规则。
            var ordinary = (MTI)RuntimeHelpers.GetUninitializedObject(typeof(MTI));
            var single = (MTIOneImage)RuntimeHelpers.GetUninitializedObject(typeof(MTIOneImage));
            Assert.True(MtiResourceAddress.UsesDirectImageEntry(ordinary));
            Assert.False(MtiResourceAddress.UsesDirectImageEntry(single));
            Assert.False(MtiResourceAddress.UsesDirectImageEntry(null));
        }

        [Fact]
        public void MtiRelease_ObservesBothDisposeAndExplicitUnload()
        {
            var methods = ReplacementMtiReleasePatch.TargetMethods().ToArray();
            Assert.Equal(new[] { "Dispose", "UnloadAll" }, methods.Select(method => method.Name));
            Assert.All(methods, method =>
            {
                Assert.Equal(typeof(MTI), method.DeclaringType);
                Assert.Empty(method.GetParameters());
            });
        }

        [Fact]
        public void SpriteLayout_PreservesCroppedRectPivotBorderAndNonRectangularMesh()
        {
            var rect = new Rect(20, 30, 40, 50);
            var pivot = new Vector2(10, 15);
            var border = new Vector4(2, 3, 4, 5);
            var vertices = new[] { new Vector2(-1, -1.5f), new Vector2(3, -1.5f), new Vector2(1, 3.5f) };
            var triangles = new ushort[] { 0, 1, 2 };
            var uv = new[] { new Vector2(20f / 128, 30f / 256), new Vector2(60f / 128, 30f / 256), new Vector2(40f / 128, 80f / 256) };

            var layout = new ReplacementSpriteLayout(rect, pivot, 10, border, 128, 256, false, vertices, triangles, uv);

            Assert.Equal(rect, layout.Rect);
            Assert.Equal(new Vector2(0.25f, 0.3f), layout.Pivot);
            Assert.Equal(10, layout.PixelsPerUnit);
            Assert.Equal(border, layout.Border);
            Assert.Equal(vertices, layout.Vertices);
            Assert.Equal(triangles, layout.Triangles);
        }

        [Fact]
        public void SpriteLayout_KeepsLogicalCanvasWhenMeshOccupiesOnlyPartOfTexture()
        {
            var rect = new Rect(0, 0, 512, 1024);
            var vertices = new[] { new Vector2(-2.56f, -1.28f), new Vector2(2.56f, -1.28f), new Vector2(0, 5.12f) };
            var uv = new[] { new Vector2(0, 0.375f), new Vector2(1, 0.375f), new Vector2(0.5f, 1) };
            var layout = new ReplacementSpriteLayout(rect, new Vector2(256, 512), 100, new Vector4(),
                512, 1024, false, vertices, new ushort[] { 0, 1, 2 }, uv);

            Assert.Equal(1024, layout.Rect.height);
            Assert.Equal(new Vector2(0.5f, 0.5f), layout.Pivot);
            Assert.Equal(vertices, layout.Vertices);
        }

        [Theory]
        [InlineData("packed")]
        [InlineData("rotated-uv")]
        [InlineData("triangle-index")]
        [InlineData("missing-mesh")]
        [InlineData("cropped-texture")]
        [InlineData("invalid-scale")]
        public void SpriteLayout_RejectsUnsupportedOrIncompleteInput(string failure)
        {
            var vertices = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(0, 1) };
            var triangles = new ushort[] { 0, 1, 2 };
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1) };
            if (failure == "rotated-uv") uv[0] = new Vector2(1, 0);
            if (failure == "triangle-index") triangles[2] = 8;
            if (failure == "missing-mesh") vertices = null;

            Assert.Throws<InvalidDataException>(() => new ReplacementSpriteLayout(new Rect(0, 0, 20, 20),
                new Vector2(10, 10), failure == "invalid-scale" ? float.NaN : 10, new Vector4(),
                20, failure == "cropped-texture" ? 10 : 20, failure == "packed", vertices, triangles, uv));
        }
    }
}
