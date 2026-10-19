using System;
using System.Collections.Generic;
using HarmonyLib;
using Items.Services;
using MelonLoader;
using Rendering.Services;
using UI.Interface.Model;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace HandyBeaconTracker
{
    // The game renders a tool icon from the side; the tracker's icon shows its screen at three quarters instead.
    [HarmonyPatch(typeof(ItemThumbnailTextureCache), nameof(ItemThumbnailTextureCache.GetThumbnail))]
    static class TrackerThumbnail
    {
        static readonly AccessTools.FieldRef<ItemThumbnailTextureCache, Dictionary<uint, Texture2D>> Cache = AccessTools.FieldRefAccess<ItemThumbnailTextureCache, Dictionary<uint, Texture2D>>("_thumbnails");
        static readonly AccessTools.FieldRef<ItemThumbnailTextureCache, ObjectThumbnailRenderer> Renderer = AccessTools.FieldRefAccess<ItemThumbnailTextureCache, ObjectThumbnailRenderer>("_thumbnailRenderer");
        static readonly AccessTools.FieldRef<ItemThumbnailTextureCache, IThumbnailsResolutionProvider> Resolution = AccessTools.FieldRefAccess<ItemThumbnailTextureCache, IThumbnailsResolutionProvider>("_thumbnailsResolutionProvider");

        static bool Prefix(ItemThumbnailTextureCache __instance, uint itemId, ref Texture2D __result)
        {
            if (itemId != TrackerItem.Id || TrackerItem.Template == null)
                return true;
            try
            {
                Dictionary<uint, Texture2D> cache = Cache(__instance);
                if (cache.TryGetValue(itemId, out Texture2D cached) && cached != null)
                {
                    __result = cached;
                    return false;
                }
                ObjectThumbnailRenderer renderer = Renderer(__instance);
                if (renderer == null)
                    return true;
                IThumbnailsResolutionProvider resolution = Resolution(__instance);
                int size = resolution != null ? resolution.GetItemSmallThumbnailResolution() : 256;
                var texture = new Texture2D(size, size, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None) { wrapMode = TextureWrapMode.Clamp };
                cache[itemId] = texture;
                GameObject copy = Object.Instantiate(TrackerItem.Template);
                copy.layer = LayerMask.NameToLayer("PartThumbnailRender");
                renderer.Render(copy, TrackerGeometry.ThumbnailRotation, texture);
                __result = texture;
                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("The tracker icon could not be drawn: " + ex.Message);
                return true;
            }
        }
    }
}
