using Spine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AICResourceKit.Patches.ReplaceTexture
{
    /// <summary>沿用 AnimationState 与 TrackEntry，保留队列、混合、事件订阅和播放进度。</summary>
    internal static class SpinePlayback
    {
        private static readonly FieldInfo animation = typeof(TrackEntry).GetField("animation", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo changed = typeof(AnimationState).GetField("animationsChanged", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void Rebind(AnimationState state, AnimationStateData next, bool restoring = false)
        {
            if (state == null || next == null) return;
            var entries = new HashSet<TrackEntry>();
            foreach (var entry in state.Tracks) Visit(entry, entries);
            var mapped = new Dictionary<TrackEntry, Animation>();
            foreach (var entry in entries)
            {
                if (entry.Animation == null || entry.Animation.Name == "<empty>") continue;
                var replacement = next.SkeletonData.FindAnimation(entry.Animation.Name);
                if (replacement == null && restoring) replacement = next.SkeletonData.Animations.FirstOrDefault();
                if (replacement == null) throw new InvalidDataException("Active/queued animation is missing: " + entry.Animation.Name);
                mapped.Add(entry, replacement);
            }
            foreach (var pair in mapped)
            {
                var entry = pair.Key;
                if (entry.AnimationEnd == entry.Animation.Duration) entry.AnimationEnd = pair.Value.Duration;
                entry.AnimationStart = Math.Min(entry.AnimationStart, pair.Value.Duration);
                animation.SetValue(entry, pair.Value);
                entry.ResetRotationDirections();
            }
            state.Data = next;
            changed.SetValue(state, true);
        }

        private static void Visit(TrackEntry entry, HashSet<TrackEntry> visited)
        {
            if (entry == null || !visited.Add(entry)) return;
            Visit(entry.Next, visited);
            Visit(entry.MixingFrom, visited);
        }

        internal static Skeleton CreateSkeleton(Skeleton previous, SkeletonData next)
        {
            var skeleton = new Skeleton(next);
            if (previous == null) return skeleton;
            skeleton.X = previous.X; skeleton.Y = previous.Y;
            skeleton.ScaleX = previous.ScaleX; skeleton.ScaleY = previous.ScaleY;
            skeleton.R = previous.R; skeleton.G = previous.G; skeleton.B = previous.B; skeleton.A = previous.A;
            bool first = true;
            foreach (string name in ReplacementRuntime.CaptureSkinNames(previous.SkinList))
            {
                var skin = next.FindSkin(name);
                if (skin == null) continue;
                if (first) skeleton.SetSkin(skin, true);
                else skeleton.MergeSkin(skin);
                first = false;
            }
            skeleton.SetSlotsToSetupPose();
            return skeleton;
        }
    }
}
