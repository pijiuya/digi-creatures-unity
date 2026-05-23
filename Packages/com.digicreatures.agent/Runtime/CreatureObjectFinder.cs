using System.Collections.Generic;
using UnityEngine;

namespace DigiCreatures
{
    public static class CreatureObjectFinder
    {
        public static T[] FindObjectsByType<T>(bool includeInactive) where T : Object
        {
            if (!includeInactive)
            {
#pragma warning disable CS0618
                return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#pragma warning restore CS0618
            }

            List<T> results = new List<T>();
            foreach (T candidate in Resources.FindObjectsOfTypeAll<T>())
            {
                if (IsSceneObject(candidate))
                {
                    results.Add(candidate);
                }
            }

            return results.ToArray();
        }

        public static T FindAnyObjectByType<T>(bool includeInactive) where T : Object
        {
            if (!includeInactive)
            {
#pragma warning disable CS0618
                T[] activeObjects = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#pragma warning restore CS0618
                return activeObjects.Length > 0 ? activeObjects[0] : null;
            }

            foreach (T candidate in Resources.FindObjectsOfTypeAll<T>())
            {
                if (IsSceneObject(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsSceneObject(Object candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (candidate is Component component)
            {
                return component.gameObject != null && component.gameObject.scene.IsValid();
            }

            if (candidate is GameObject gameObject)
            {
                return gameObject.scene.IsValid();
            }

            return false;
        }
    }
}
