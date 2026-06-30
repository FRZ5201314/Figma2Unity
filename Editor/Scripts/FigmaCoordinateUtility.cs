using UnityEngine;

namespace FigmaUiImporter.Editor
{
    internal static class FigmaCoordinateUtility
    {
        public static Vector2 ToUnityAnchoredPosition(Rect rootBounds, Rect nodeBounds)
        {
            float localLeft = nodeBounds.x - rootBounds.x;
            float localTop = nodeBounds.y - rootBounds.y;
            float unityX = localLeft + nodeBounds.width * 0.5f - rootBounds.width * 0.5f;
            float unityY = rootBounds.height * 0.5f - localTop - nodeBounds.height * 0.5f;
            return new Vector2(unityX, unityY);
        }
    }
}
