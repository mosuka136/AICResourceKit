using AICResourceKit.Patches.ReplaceTexture;

namespace AICResourceKit.Test.Patches.ReplaceTexture
{
    public sealed class PortraitSpineVariantsTests
    {
        [Fact]
        public void Take_ReturnsParkedCompositionForSameLayersOnce()
        {
            // 复现：切换立绘时当前组合被丢弃，返回同一状态只能等后台重建并先显示原版。
            var variants = new PortraitSpineVariants<object>();
            var normal = Target("normal");
            var composition = new object();
            Assert.Empty(variants.Park(new[] { normal }, composition));
            Assert.Same(composition, variants.Take(new List<ReplacementTarget> { normal }));
            Assert.Null(variants.Take(new[] { normal }));
            Assert.Equal(0, variants.Count);
        }

        [Fact]
        public void Take_RequiresSameTargetsInSameOrder()
        {
            var variants = new PortraitSpineVariants<object>();
            var low = Target("low");
            var high = Target("high");
            var composition = new object();
            variants.Park(new[] { low, high }, composition);
            Assert.Null(variants.Take(new[] { high, low }));
            Assert.Null(variants.Take(new[] { low }));
            Assert.Null(variants.Take(new[] { Target("low"), high }));
            Assert.Same(composition, variants.Take(new[] { low, high }));
        }

        [Fact]
        public void Park_EvictsOldestAndReturnsReplacedComposition()
        {
            var variants = new PortraitSpineVariants<object>(2);
            var a = Target("a");
            var b = Target("b");
            var c = Target("c");
            object first = new object(), second = new object(), third = new object(), replaced = new object();
            variants.Park(new[] { a }, first);
            variants.Park(new[] { b }, second);
            Assert.Equal(new[] { first }, variants.Park(new[] { c }, third));
            Assert.Equal(new[] { second }, variants.Park(new[] { b }, replaced));
            Assert.Empty(variants.Park(new[] { b }, replaced));
            Assert.Equal(2, variants.Count);
            Assert.Null(variants.Take(new[] { a }));
            Assert.Same(replaced, variants.Take(new[] { b }));
        }

        [Fact]
        public void Failures_AreClearedByParkAndClear()
        {
            var variants = new PortraitSpineVariants<object>();
            var torn = Target("torn");
            var normal = Target("normal");
            var composition = new object();
            variants.Fail(new[] { torn });
            Assert.True(variants.Failed(new[] { torn }));
            Assert.False(variants.Failed(new[] { normal }));
            variants.Park(new[] { torn }, composition);
            Assert.False(variants.Failed(new[] { torn }));
            variants.Fail(new[] { normal });
            Assert.Equal(new[] { composition }, variants.Clear());
            Assert.False(variants.Failed(new[] { normal }));
            Assert.Equal(0, variants.Count);
        }

        private static ReplacementTarget Target(string id) =>
            new ReplacementTarget { Owner = new ReplacementPackage { Id = id }, PackageId = id, Type = "spine", SpineKey = "weak", JsonKey = "weak" };
    }
}
