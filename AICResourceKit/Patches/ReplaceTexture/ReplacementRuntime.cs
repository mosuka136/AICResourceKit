using AICResourceKit.Contracts;
using AICResourceKit.BConfigManager;
using AICResourceKit.BLogSpace;
using nel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AICResourceKit.Patches.ReplaceTexture
{
    // 目录扫描、配置选择与运行时调度。Unity 对象和运行时状态只在主线程访问。
    internal static partial class ReplacementRuntime
    {
        private static ReplacementCatalog catalog = new ReplacementCatalog();
        private static readonly ReplacementSelectionDelay selectionDelay = new ReplacementSelectionDelay();
        private static ReplacementSelection selection = new ReplacementSelection(catalog, null, false, false);
        private static ReplacementWork<ReplacementCatalog> scan;
        private static bool lastSensitive;
        private static int revision;
        private static int displayRevision;
        internal static int Revision => displayRevision;
        private static bool stopped;
        private static bool initialized;
        internal static bool HasWork => spineStates.Values.Any(state => state.Shown != null)
            || AllMtiRecords().Any(record => record.Replacement != null)
            || resourceRecords.Values.Any(record => record.Source != null)
            || pxlSurfaces.Values.Any(surface => surface.Applied != null)
            || portraitPxlImages.Any(entry => entry.Image != null)
            || ordinaryViewers.Values.Any(state => state.Current != null)
            || atlasSurfaces.Values.Any(surface => surface.Contents != null);

        internal static void Initialize()
        {
            initialized = true;
            stopped = false;
            spineAvailable = new MemberInfo[]
            {
                svAtlas, svData, depth, allocate, viewerAtlas, viewerData, viewerContainer,
                viewerTexture, animator, reserved, cachedAtlas, initializeData
            }.All(member => member != null);
            if (!spineAvailable) BLog.Warn("Spine resource replacement disabled: game interfaces do not match.");
            if (mtiImage == null) BLog.Warn("MTI resource replacement disabled: image container interface does not match.");
            Directory.CreateDirectory(PatchInfo.ReplaceImagePath);
            Directory.CreateDirectory(PatchInfo.ReplaceSensitiveImagePath);
            lastSensitive = ConfigManager.EnableSensitivities?.Value == true;
            selectionDelay.Reset(Settings);
            // 初次 Resources.Load 的返回对象会被游戏持有；必须在注册补丁前建立完整目录。
            // 仅启动时同步扫描，游戏内开关变化使用目录快照，手动刷新走后台扫描。
            try
            {
                AcceptCatalog(ReplacementCatalog.Discover(PatchInfo.ReplaceImagePath,
                    PatchInfo.ReplaceSensitiveImagePath, lastSensitive));
            }
            catch (Exception ex) { BLog.Error("Initial replacement discovery failed.", ex); }
        }

        private static string Settings => stopped + "|" + ConfigManager.EnableResourceReplacement?.Value + "|"
            + string.Join("\n", EnabledIds()) + "|" + ConfigManager.EnableSensitivities?.Value;

        internal static void PollSettings()
        {
            if (!initialized || stopped) return;
            bool sensitive = ConfigManager.EnableSensitivities?.Value == true;
            if (sensitive != lastSensitive)
            {
                lastSensitive = sensitive;
                scan?.Dispose();
                scan = null;
                // 撤销敏感授权立即生效；重新授权时后台补扫可能未载入的敏感目录。
                ApplySelection(false);
                Reload();
            }
            if (scan != null && scan.TryTake(out var scanned, out var error))
            {
                scan = null;
                if (error != null)
                {
                    scanError = error.Message;
                    BLog.Error("Replacement discovery failed; keeping the previous catalog.", error);
                }
                else AcceptCatalog(scanned);
            }
            if (selectionDelay.Ready(Settings, Time.unscaledTime, selection.RevokedBy(EnabledIds(), Enabled, sensitive))) ApplySelection(false);
            if (selectionDelay.Waiting) return;
            RetryMtiRecords();
            RefreshResourceRecords();
            PumpPxlSurfaces();
            PumpSpines();
            PumpOrdinaryViewers();
            PumpAtlasSurfaces();
            Collect();
        }

        private static void AcceptCatalog(ReplacementCatalog scanned)
        {
            scanError = null;
            SyncPackRows(scanned);
            catalog = scanned;
            DiscoverDiagnosticTargets();
            foreach (string message in catalog.Errors) BLog.Warn("Replacement: " + message);
            ApplySelection(true);
            BLog.Info("Resource replacement catalog refreshed.");
        }

        internal static void DiscoverDiagnosticTargets()
        {
            if (!ReplacementDiagnosticRuntime.Enabled) return;
            foreach (var package in catalog.Packages)
                foreach (var target in package.Targets) ReplacementDiagnosticRuntime.Discover(target);
            DiscoverPxlBindings();
            DiscoverOrdinaryViewers();
            DiscoverAtlasBindings();
            foreach (string error in catalog.Errors)
                ReplacementDiagnosticRuntime.Record(new ReplacementDiagnosticTarget("catalog", null, null, "Manifest"),
                    "candidate-failed", "ReplacementCatalog.Discover", "catalog-error", error);
        }

        internal static void Reload()
        {
            if (!initialized || stopped) return;
            scan?.Dispose();
            scanError = null;
            CancelPendingReplacements();
            string root = PatchInfo.ReplaceImagePath, sensitive = PatchInfo.ReplaceSensitiveImagePath;
            bool allow = ConfigManager.EnableSensitivities?.Value == true;
            scan = new ReplacementWork<ReplacementCatalog>(token => ReplacementCatalog.Discover(root, sensitive, allow, token));
        }

        private static void ApplySelection(bool force)
        {
            var previous = selection;
            selection = new ReplacementSelection(catalog, EnabledIds(), Enabled, ConfigManager.EnableSensitivities?.Value == true);
            selectionDelay.Reset(Settings);
            revision++;
            if (force) statusFailures.Clear();
            InvalidateSpineSelection(previous, force);
            InvalidateMtiSelection(previous, force);
            InvalidateResourceSelection(previous, force);
            InvalidatePxlSelection();
            InvalidateOrdinaryViewers();
            InvalidateAtlasSelection();
            RetryMtiRecords();
            RefreshResourceRecords();
            PumpPxlSurfaces();
            PumpOrdinaryViewers();
            PumpAtlasSurfaces();
            RefreshMtiSpineTextures(previous, force);
            RefreshSpineViewers(previous, force);
            RefreshPortraitPxl();
            RevokeUnauthorizedPreviews();
            PortraitControlRuntime.OnReplacementSelectionChanged(force
                ? new List<ReplacementTarget>() : selection.NewlyEnabledPortraits(previous));
        }

        internal static bool PreviewTargetEnabled(ReplacementTarget target) => target != null && target.PortraitSelection == null && Enabled && spineAvailable
            && selection.Authorizes(new[] { target.Owner }) && selection.Layers(target.Identity).Contains(target)
            && !selection.Invalid(target.Identity)
            && (!target.Owner.Sensitive || ConfigManager.EnableSensitivities?.Value == true);

        internal static PortraitPreviewReadiness PreviewReadiness(UIPictureBodySpine body, ReplacementTarget target)
        {
            if (!PreviewTargetEnabled(target)) return PortraitPreviewReadiness.Failed;
            var viewer = body?.getViewer();
            var texture = viewer?.getSvTexture();
            if (texture == null || texture.key != target.SpineKey
                || (viewer.replace_json_key ?? texture.MtiText.default_json_key) != target.JsonKey)
                return PortraitPreviewReadiness.Failed;
            return spineStates.TryGetValue(texture, out var state) && state.Preview != null
                && ReferenceEquals(state.PreviewTarget, target)
                ? PortraitPreviewReadiness.Ready : PortraitPreviewReadiness.Failed;
        }

        private static List<string> EnabledIds() =>
            ReplacementCatalog.EnabledIds(ConfigManager.EnabledReplacementPacks?.Value);
        private static bool Enabled => !stopped && ConfigManager.EnableResourceReplacement?.Value == true;

        // 清单已从磁盘消失的包，其配置行在每轮扫描后自动清除；敏感开关关闭或清单解析失败都不影响判定，
        // 因为这些包的 id 仍然登记在 DeclaredIds 中。
        private static void SyncPackRows(ReplacementCatalog scanned)
        {
            var entry = ConfigManager.EnabledReplacementPacks;
            if (entry == null) return;
            var current = entry.Value ?? new List<(string, bool)>();
            var synced = scanned.SyncRows(current);
            if (synced.SequenceEqual(current)) return;
            foreach (string removed in RemovedIds(current, synced))
                BLog.Info("Removed the replacement pack row because its manifest no longer exists: " + removed);
            entry.Value = synced;
        }

        private static List<string> RemovedIds(IEnumerable<(string Id, bool Enabled)> current,
            IEnumerable<(string Id, bool Enabled)> synced)
        {
            var kept = new HashSet<string>(synced.Select(row => row.Id?.Trim())
                .Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            var removed = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in current)
            {
                string id = row.Id?.Trim();
                if (!string.IsNullOrEmpty(id) && !kept.Contains(id) && seen.Add(id)) removed.Add(id);
            }
            return removed;
        }

        internal static void Refresh()
        {
            Reload();
        }

        internal static void Stop()
        {
            if (!initialized || stopped) return;
            stopped = true;
            scan?.Dispose();
            scan = null;
            ApplySelection(true);
            RestoreOrdinary();
        }

        internal static void Resume()
        {
            if (!initialized || !stopped) return;
            stopped = false;
            lastSensitive = ConfigManager.EnableSensitivities?.Value == true;
            ApplySelection(false);
            Reload();
        }

        private static void RestoreOrdinary()
        {
            foreach (var record in AllMtiRecords()) Restore(record);
            foreach (var record in resourceRecords.Values) RestoreResource(record);
            foreach (var surface in pxlSurfaces.Values) RestorePxlSurface(surface);
        }

        private static bool CanRetain(IEnumerable<ReplacementPackage> packages, string identity)
        {
            if (!Enabled || !selection.Authorizes(packages)) return false;
            foreach (var package in packages.Where(package => package != null).Distinct())
            {
                if (!File.Exists(package.ManifestPath)) return false;
                if (package.Sensitive && ConfigManager.EnableSensitivities?.Value != true) return false;
                try
                {
                    PortraitCatalog.Resolve(PatchInfo.ReplaceImagePath, Path.GetDirectoryName(package.ManifestPath),
                        Path.GetFileName(package.ManifestPath));
                }
                catch { return false; }
                var current = catalog.Packages.FirstOrDefault(candidate => candidate.Id == package.Id);
                if (current != null && !current.Targets.Any(target => target.Identity == identity)
                    && !current.InvalidTargetIdentities.Contains(identity)
                    && !current.HasUnidentifiedTargetErrors) return false;
            }
            return true;
        }

        private static void ValidateCurrent(ReplacementTarget target)
        {
            ReplacementPreparation.Validate(new[] { target }, PatchInfo.ReplaceImagePath,
                PatchInfo.ReplaceSensitiveImagePath, ConfigManager.EnableSensitivities?.Value == true);
        }

        private static bool HasUnidentifiedErrors(IEnumerable<ReplacementPackage> packages, string identity)
        {
            foreach (var source in packages.Where(package => package != null))
            {
                var current = catalog.Packages.FirstOrDefault(package => package.Id == source.Id);
                if (current != null && current.HasUnidentifiedErrorFor(identity)) return true;
            }
            return false;
        }

        private static bool HasInvalidLayer(string identity)
        {
            return Enabled && selection.Invalid(identity);
        }

        private static bool HasInvalidTextureLayer(string loader, string key, string imageKey, string objectType)
        {
            return Enabled && selection.InvalidTexture(loader, key, imageKey, objectType);
        }

        private static List<ReplacementTarget> ActiveLayers(string identity)
        {
            if (!Enabled) return new List<ReplacementTarget>();
            return selection.Layers(identity).ToList();
        }

        private static bool HasLayers(string identity) => ActiveLayers(identity).Count > 0;
        private static string SpineIdentity(string key, string jsonKey) => ResourceIdentity.Spine(key, jsonKey);
    }
}
