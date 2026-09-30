using System;
using System.Collections.Generic;
using System.Linq;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal sealed class ReplacementPreviewPose
    {
        internal PortraitSelection Selection;
        internal string SpineKey;
        internal string JsonKey;
        internal string[] Animations = new string[0];
        internal string Animation;
        internal HashSet<string> PxlIdentities = new HashSet<string>(StringComparer.Ordinal);

        internal bool Matches(ReplacementTarget target) => target.PortraitSelection == null
            || target.PortraitSelection.Matches(Selection.Pose.ToString(), (uint)Selection.State, Animation);
    }

    internal static class ReplacementPreview
    {
        internal static bool Supports(ReplacementTarget target) => target != null
            && (target.Type == "spine" || (target.Loader == "pxl" && target.PortraitSelection != null));

        internal static List<ReplacementTarget> Layers(ReplacementSelection selection, ReplacementTarget target,
            ReplacementPreviewPose pose = null)
        {
            if (target == null || !selection.Authorizes(new[] { target.Owner }) || selection.Invalid(target.Identity)
                || (target.PortraitSelection != null && (pose == null || !pose.Matches(target))))
                return new List<ReplacementTarget>();
            var layers = selection.Layers(target.Identity);
            if (!layers.Contains(target)) return new List<ReplacementTarget>();
            // 临时提升本次启用的目标，同时排除不符合预览状态的其他条件层。
            return layers.Where(layer => !ReferenceEquals(layer, target)
                && (layer.PortraitSelection == null || (pose != null && pose.Matches(layer))))
                .Concat(new[] { target }).ToList();
        }

        internal static bool MatchesCurrent(ReplacementTarget target, ReplacementPreviewPose current) =>
            Supports(target) && current != null && current.Matches(target)
            && (target.Type == "spine" ? target.SpineKey == current.SpineKey && target.JsonKey == current.JsonKey
                : current.PxlIdentities.Contains(target.Identity));

        internal static ReplacementPreviewPose Choose(IReadOnlyList<ReplacementTarget> targets,
            IReadOnlyList<ReplacementPreviewPose> poses, out ReplacementTarget target, ReplacementPreviewPose current = null)
        {
            foreach (var package in targets.Where(Supports).GroupBy(value => value.PackageId).Reverse())
            {
                // 当前实际动作已能显示该包时，按正常优先级应用，不切换、不重启动画。
                if (package.Any(candidate => MatchesCurrent(candidate, current))) break;
                foreach (var pose in poses)
                {
                    if (!PortraitControlLogic.IsMainPose(pose.Selection.Pose)) continue;
                    foreach (var candidate in package)
                    {
                        if (candidate.Type == "spine")
                        {
                            if (candidate.SpineKey != pose.SpineKey || candidate.JsonKey != pose.JsonKey) continue;
                            string animation = pose.Animations.FirstOrDefault(name => candidate.PortraitSelection == null
                                || candidate.PortraitSelection.Matches(pose.Selection.Pose.ToString(), (uint)pose.Selection.State, name));
                            if (candidate.PortraitSelection?.Animations.Count > 0 && animation == null) continue;
                            pose.Animation = animation;
                        }
                        else if (!pose.PxlIdentities.Contains(candidate.Identity)) continue;
                        if (!pose.Matches(candidate)) continue;
                        target = candidate;
                        return pose;
                    }
                }
            }
            target = null;
            return null;
        }
    }
}
