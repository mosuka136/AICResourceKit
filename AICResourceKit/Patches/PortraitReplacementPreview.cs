using AICResourceKit.BConfigManager;
using AICResourceKit.BLogSpace;
using AICResourceKit.BPatchGUI;
using AICResourceKit.Patches.ReplaceTexture;
using nel;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AICResourceKit.Patches
{
    internal static partial class PortraitControlRuntime
    {
        private static readonly PortraitPreviewSession Preview = new PortraitPreviewSession();
        private static readonly object PreviewNoticeOwner = new object();
        private static ReplacementTarget previewTarget;
        private static UIPictureBodyData previewBody;
        private static ReplacementPreviewPose previewPose, originalPreviewPose;

        private static PortraitControlSession ControlFor(UIPictureBase picture) =>
            Preview.ControlFor(picture, Session);

        internal static void OnReplacementSelectionChanged(IReadOnlyList<ReplacementTarget> targets)
        {
            try
            {
                if (targets.Count == 0 || Session.HasPending || !TryPicture(out var picture))
                {
                    EndReplacementPreview(true);
                    return;
                }
                EnsureCatalog();
                var original = Preview.Active && ReferenceEquals(Preview.Control.Owner, picture) ? Preview.Original.Value
                    : new PortraitSelection(picture.getCurEmot(), picture.getCurrentState(), picture.getAdditionalState());
                if (!PortraitControlLogic.IsMainPose(original.Pose)) return;
                var originalPose = Preview.Active && ReferenceEquals(Preview.Control.Owner, picture) ? originalPreviewPose
                    : ReplacementRuntime.DescribePreview(picture.getBodyData(), original);
                var poses = new List<ReplacementPreviewPose>();
                var requests = new[] { original }.Concat(catalog.SelectMany(entry => entry.Presets)).ToArray();
                var conditioned = requests.SelectMany(request => targets.Where(target => target.PortraitSelection != null)
                    .Select(target => new PortraitSelection(request.Pose,
                        (UIPictureBase.EMSTATE)target.PortraitSelection.PreviewState((uint)request.State), request.Additional)));
                foreach (var requested in requests.Concat(conditioned).Distinct())
                {
                    var state = requested.State;
                    var emotion = picture.GetEmot(requested.Pose, ref state);
                    var paint = emotion?.Get(state);
                    var body = paint?.Body?.getReplaceTerm() ?? paint?.Body;
                    if (body == null) continue;
                    var descriptor = ReplacementRuntime.DescribePreview(body,
                        new PortraitSelection(emotion.emot_id, state, requested.Additional));
                    if (descriptor != null) poses.Add(descriptor);
                }
                var chosen = ReplacementPreview.Choose(targets, poses, out var target, originalPose);
                if (chosen == null || !ReplacementRuntime.PreviewTargetEnabled(target)) { EndReplacementPreview(true); return; }
                ReplacementRuntime.CancelPendingResourcePreview();
                previewTarget = target;
                previewPose = chosen;
                originalPreviewPose = originalPose;
                previewBody = null;
                Preview.Begin(picture, original, chosen.Selection, Time.unscaledTime);
                NoticeGUI.SetStatus(PreviewNoticeOwner, "正在准备资源立绘预览…");
            }
            catch (Exception ex)
            {
                BLog.Error("Could not start the replacement portrait preview.", ex);
                EndReplacementPreview(true);
            }
        }

        private static bool UpdateReplacementPreview()
        {
            if (!Preview.Active) return false;
            var picture = Preview.Control.Owner as UIPicture;
            if (ConfigManager.EnableMod?.Value != true)
            {
                Stop(true);
                return false;
            }
            if (picture == null || picture.Gob == null)
            {
                EndReplacementPreview(false);
                return false;
            }
            if (!TryPicture(out var current))
            {
                EndReplacementPreview(true);
                return false;
            }
            if (!ReferenceEquals(current, picture))
            {
                EndReplacementPreview(false);
                return false;
            }
            try
            {
                if (!ReplacementRuntime.PreviewTargetEnabled(previewTarget)) Preview.Restore(Time.unscaledTime);
                var result = Preview.Tick(Time.unscaledTime, value => PrepareReplacementPreview(picture, value),
                    (value, force) => ApplyReplacementPreview(picture, value, force),
                    () => ReplacementRuntime.PrepareResourcePreview(previewBody, previewTarget, picture.MtrSpine, previewPose));
                if (result == PortraitPreviewResult.Showing)
                    NoticeGUI.SetStatus(PreviewNoticeOwner, "资源立绘预览：" + PortraitControlLogic.Display(Preview.Control.Active.Value.Pose.ToString()) + "（2 秒后恢复）");
                if (!Preview.Active)
                {
                    bool restoreGame = Preview.RestoreFailed && !Session.Locked;
                    FinishReplacementPreview();
                    if (restoreGame) RestoreGamePortrait(picture);
                    if (result == PortraitPreviewResult.Failed)
                    {
                        BLog.Warn("Replacement portrait preview failed: " + Preview.Error?.Message);
                        NoticeGUI.Show("资源预览失败，已结束预览。", 4f, PreviewNoticeOwner);
                    }
                }
            }
            catch (Exception ex)
            {
                BLog.Error("Replacement portrait preview failed.", ex);
                EndReplacementPreview(true);
            }
            return Preview.Active;
        }

        private static void EndReplacementPreview(bool restore)
        {
            if (!Preview.Active) return;
            var picture = Preview.Control.Owner as UIPicture;
            bool restoreGame = false;
            try
            {
                if (restore && picture != null && picture.Gob != null && picture.gob_prepared)
                {
                    Preview.Restore(Time.unscaledTime);
                    Preview.Tick(Time.unscaledTime, value => PrepareReplacementPreview(picture, value),
                        (value, force) => ApplyReplacementPreview(picture, value, force), () => PortraitPreviewReadiness.Ready);
                    if (Preview.Active) return;
                    restoreGame = Preview.RestoreFailed && !Session.Locked;
                }
                else Preview.Reset();
            }
            catch (Exception ex)
            {
                Preview.Reset();
                BLog.Error("Could not restore the portrait after a resource preview.", ex);
                restoreGame = picture != null && picture.Gob != null && picture.gob_prepared && !Session.Locked;
            }
            FinishReplacementPreview();
            if (restoreGame) RestoreGamePortrait(picture);
        }

        private static void FinishReplacementPreview()
        {
            ReplacementRuntime.FinishResourcePreview();
            previewTarget = null;
            previewPose = null;
            originalPreviewPose = null;
            previewBody = null;
            NoticeGUI.RemoveStatus(PreviewNoticeOwner);
            // 成功恢复后沿用原锁定；仅恢复失败时让控制会话重试。
            refreshRequired |= Preview.RestoreFailed;
        }

        private static PortraitSelection? PrepareReplacementPreview(UIPicture picture, PortraitSelection requested)
        {
            var prepared = Prepare(picture, requested);
            if (!prepared.HasValue) return null;
            var state = prepared.Value.State;
            var paint = picture.GetEmot(prepared.Value.Pose, ref state).Get(state);
            var body = paint.Body.getReplaceTerm() ?? paint.Body;
            previewBody = body;
            previewPose = ReplacementRuntime.DescribePreview(body, prepared.Value);
            if (Preview.Restoring)
            {
                if (previewPose.SpineKey == originalPreviewPose?.SpineKey && previewPose.JsonKey == originalPreviewPose?.JsonKey)
                    previewPose.Animation = originalPreviewPose?.Animation;
                return ReplacementRuntime.PreparePreviewReturn(body, picture.MtrSpine, previewPose) ? prepared : null;
            }
            if (previewPose == null || ReplacementPreview.Choose(new[] { previewTarget }, new[] { previewPose }, out _) == null)
                throw new InvalidOperationException("The game cannot display a portrait matching this resource's conditions.");
            return prepared;
        }

        private static void ApplyReplacementPreview(UIPicture picture, PortraitSelection value, bool force)
        {
            if (Preview.Restoring)
            {
                ReplacementRuntime.ActivatePreviewReturn(previewBody);
                Apply(picture, value, force);
                ReplacementRuntime.VerifyPreviewReturn(previewBody);
                return;
            }
            try
            {
                ReplacementRuntime.ActivateResourcePreview(previewBody, previewTarget);
                Apply(picture, value, force);
                ReplacementRuntime.VerifyResourcePreview(previewBody, previewTarget);
            }
            catch
            {
                ReplacementRuntime.RemoveResourcePreview();
                try
                {
                    rollbackOverride = Preview.Original;
                    if (Preview.Original.HasValue) Apply(picture, Preview.Original.Value, true);
                }
                finally { rollbackOverride = null; }
                throw;
            }
        }
    }
}
