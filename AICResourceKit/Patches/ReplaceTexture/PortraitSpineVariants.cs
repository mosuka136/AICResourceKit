using System.Collections.Generic;
using System.Linq;

namespace AICResourceKit.Patches.ReplaceTexture
{
    /// <summary>
    /// 主立绘一个 SvTexture 的非当前组合缓存；切换姿态或状态时在同一次绘制内复用已构建的组合。
    /// 层序列按目标引用和顺序比较；不创建或销毁 Unity 对象，淘汰项交由调用方回收。
    /// </summary>
    internal sealed class PortraitSpineVariants<T> where T : class
    {
        private sealed class Entry
        {
            internal ReplacementTarget[] Layers;
            internal T Value;
        }

        private readonly int capacity;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<ReplacementTarget[]> failures = new List<ReplacementTarget[]>();

        internal PortraitSpineVariants(int capacity = 2)
        {
            this.capacity = capacity < 1 ? 1 : capacity;
        }

        internal int Count => entries.Count;

        /// <summary>暂存组合为最近使用项，返回被替换或超出容量需要回收的对象。</summary>
        internal List<T> Park(IReadOnlyList<ReplacementTarget> layers, T value)
        {
            var released = new List<T>();
            if (layers == null || value == null) return released;
            failures.RemoveAll(item => item.SequenceEqual(layers));
            foreach (var entry in entries.Where(item => item.Layers.SequenceEqual(layers)).ToArray())
            {
                entries.Remove(entry);
                if (!ReferenceEquals(entry.Value, value)) released.Add(entry.Value);
            }
            entries.RemoveAll(item => ReferenceEquals(item.Value, value));
            entries.Add(new Entry { Layers = layers.ToArray(), Value = value });
            while (entries.Count > capacity)
            {
                released.Add(entries[0].Value);
                entries.RemoveAt(0);
            }
            return released;
        }

        /// <summary>取出与层序列完全一致的组合；没有时返回 null。</summary>
        internal T Take(IReadOnlyList<ReplacementTarget> layers)
        {
            if (layers == null) return null;
            var entry = entries.FirstOrDefault(item => item.Layers.SequenceEqual(layers));
            if (entry == null) return null;
            entries.Remove(entry);
            return entry.Value;
        }

        internal void Fail(IReadOnlyList<ReplacementTarget> layers)
        {
            if (layers != null && !Failed(layers)) failures.Add(layers.ToArray());
        }

        internal bool Failed(IReadOnlyList<ReplacementTarget> layers) =>
            layers != null && failures.Any(item => item.SequenceEqual(layers));

        /// <summary>清空缓存和失败记录，返回全部缓存对象。</summary>
        internal List<T> Clear()
        {
            var released = entries.Select(entry => entry.Value).ToList();
            entries.Clear();
            failures.Clear();
            return released;
        }
    }
}
