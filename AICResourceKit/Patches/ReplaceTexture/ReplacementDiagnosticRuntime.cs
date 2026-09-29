using AICResourceKit.BConfigManager;
using AICResourceKit.BLogSpace;
using HarmonyLib;
using nel;
using PixelLiner;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using XX;

namespace AICResourceKit.Patches.ReplaceTexture
{
    // Optional development observer. It never selects packs or changes game resources.
    internal static class ReplacementDiagnosticRuntime
    {
        private static ReplacementDiagnosticSession session;
        private static readonly List<object> hooks = new List<object>();
        private static readonly Dictionary<string, object> assemblies = new Dictionary<string, object>();
        private static bool hooksInstalled;
        private static bool exportErrorReported;
        private static float nextExport;
        private static string gameRoot;

        internal static bool Enabled => session != null;

        internal static void Poll()
        {
            bool enabled = ConfigManager.EnableMod?.Value == true
                && ConfigManager.EnableResourceDiagnostics?.Value == true;
            string filter = ConfigManager.ResourceDiagnosticFilter?.Value ?? "";
            if (session != null && (!enabled || session.Filter != filter))
            {
                Flush();
                session = null;
            }
            if (!enabled) return;
            if (session == null)
            {
                session = new ReplacementDiagnosticSession(filter);
                ReplacementDiagnosticHooks.ClearObservations();
                gameRoot = Path.GetDirectoryName(Application.dataPath);
                if (!hooksInstalled)
                {
                    foreach (var assembly in new[] { typeof(PR).Assembly, typeof(MTI).Assembly, typeof(PxlCharacter).Assembly })
                        assemblies[assembly.GetName().Name] = assembly.ManifestModule.ModuleVersionId.ToString();
                    ReplacementDiagnosticHooks.Install(new Harmony(PatchInfo.HarmonyPluginId + ".resource-diagnostics"), hooks);
                    hooksInstalled = true;
                }
                ReplacementRuntime.DiscoverDiagnosticTargets();
                nextExport = 0f;
                exportErrorReported = false;
            }
            if (Time.unscaledTime < nextExport) return;
            nextExport = Time.unscaledTime + 5f;
            Flush();
        }

        internal static void Stop()
        {
            Flush();
            session = null;
            ReplacementDiagnosticHooks.ClearObservations();
        }

        internal static void Flush()
        {
            if (session == null || !session.Dirty) return;
            try
            {
                session.Export(Path.Combine(PatchInfo.LoggerPath, "resource-diagnostics.json"),
                    PatchInfo.BepInPluginVersion, assemblies, hooks);
                exportErrorReported = false;
            }
            catch (Exception ex)
            {
                if (!exportErrorReported) BLog.Error("Resource diagnostic export failed; observations remain in memory.", ex);
                exportErrorReported = true;
            }
        }

        internal static void Record(ReplacementDiagnosticTarget target, string stage, string entry,
            string outcome, string reason = null, Dictionary<string, object> details = null)
        {
            if (session == null) return;
            try { session.Record(target, stage, entry, outcome, Sanitize(reason), details); }
            catch (Exception ex) { BLog.Error("Resource diagnostic observation failed.", ex); }
        }

        internal static void Guard(Action action)
        {
            if (!Enabled) return;
            try { action(); }
            catch (Exception ex) { BLog.Error("Resource diagnostic hook failed.", ex); }
        }

        internal static string Sanitize(string value)
        {
            if (value == null) return null;
            value = value.Replace(PatchInfo.PluginPath, "<plugin>");
            if (!string.IsNullOrEmpty(gameRoot)) value = value.Replace(gameRoot, "<game>");
            return value;
        }

        internal static void Discover(ReplacementTarget target)
        {
            if (!Enabled) return;
            Record(ReplacementDiagnosticTarget.FromManifest(target), "discovered", "ReplacementCatalog",
                "manifest-target", details: new Dictionary<string, object> { ["packageId"] = target.PackageId });
        }

        internal static void Spine(string key, string jsonKey, string stage, string outcome, string reason = null)
        {
            if (Enabled) Record(ReplacementDiagnosticTarget.Spine(key, jsonKey), stage,
                "BetobetoManager.SvTexture.prepareAtlasAssets / ReplacementRuntime", outcome, reason);
        }

        internal static void Mti(string assetKey, string imageKey, string stage, string outcome, string reason = null)
        {
            if (Enabled) Record(ReplacementDiagnosticTarget.Mti(assetKey, imageKey), stage,
                "MTI.LoadImage / LoadContainerOneImage / ReplacementRuntime", outcome, reason);
        }

        internal static void Resource(string path, string objectType, string stage, string outcome, string reason = null)
        {
            if (Enabled) Record(ReplacementDiagnosticTarget.Resources(path, objectType), stage,
                "Resources.Load / ReplacementRuntime", outcome, reason);
        }

        internal static string MtiKey(MTI container) => container == null ? null : MtiKey(container.resources_path);

        internal static string MtiKey(string resourcesPath) => MtiResourceAddress.ContainerKey(resourcesPath);
    }
}
