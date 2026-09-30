using System.Linq;
using UnityEngine;

namespace AICResourceKit.Patches.ReplaceTexture
{
    internal static partial class ReplacementRuntime
    {
        // 刷新开始即淘汰旧任务；正在显示且仍获授权的资源保留到新目录/候选准备完成。
        private static void CancelPendingReplacements()
        {
            CancelPreviewBuild();
            foreach (var state in spineStates.Values) { CancelSpinePreparation(state); state.Attempt = -1; }
            foreach (var record in AllMtiRecords()) { record.Pending?.Dispose(); record.Pending = null; record.Attempt = -1; }
            foreach (var record in resourceRecords.Values) { record.Pending?.Dispose(); record.Pending = null; record.Attempt = -1; }
            InvalidatePxlSelection();
            InvalidateOrdinaryViewers();
            InvalidateAtlasSelection();
        }

        private static bool HasPxlEdits(Texture texture) => pxlSurfaces.TryGetValue(texture, out var surface)
            && surface.Bindings.Any(binding => selection.Layers(binding.Address.Identity).Any(target => target.PortraitSelection == null));

        private static bool HasAtlasEdits(Texture texture) => texture is Texture2D image
            && atlasSurfaces.TryGetValue(image, out var surface) && SelectAtlasEdits(surface).Count > 0;
    }
}
