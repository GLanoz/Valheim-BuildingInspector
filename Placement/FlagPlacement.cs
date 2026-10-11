using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BuildingInspector
{
    public partial class BuildingInspector
    {
        private static float tiltX;
        private static float tiltZ;
        private static float surfaceWheelRotation;
        private static GameObject lastTiltedGhost;

        private static void AlignFlagToSurface(Player player)
        {
            GameObject ghost = AccessTools.Field(typeof(Player), "m_placementGhost").GetValue(player) as GameObject;
            if (!ghost || !ghost.name.StartsWith("InspectionFlag", StringComparison.Ordinal))
            {
                lastTiltedGhost = null;
                return;
            }

            Camera camera = Camera.main;
            if (!camera)
            {
                return;
            }

            Ray ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));
            FieldInfo placeRayMaskField = AccessTools.Field(typeof(Player), "m_placeRayMask");
            int placeRayMask = placeRayMaskField != null
                ? (int)placeRayMaskField.GetValue(player)
                : Physics.DefaultRaycastLayers;

            if (!Physics.Raycast(ray, out RaycastHit hit, 50f, placeRayMask, QueryTriggerInteraction.Ignore) ||
                Vector3.Distance(hit.point, ghost.transform.position) > 1.75f)
            {
                return;
            }

            bool isNewGhost = ghost != lastTiltedGhost;
            if (isNewGhost)
            {
                tiltX = 0f;
                tiltZ = 0f;
                surfaceWheelRotation = 0f;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
            {
                surfaceWheelRotation += Mathf.Sign(scroll) * 22.5f;
            }

            // Face the cloth toward the player; mouse-wheel rotation is applied around the surface normal.
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.position - hit.point, hit.normal);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.ProjectOnPlane(-ray.direction, hit.normal);
            }

            if (forward.sqrMagnitude > 0.001f)
            {
                Quaternion surfaceRotation = Quaternion.LookRotation(forward.normalized, hit.normal);
                surfaceRotation = Quaternion.AngleAxis(surfaceWheelRotation, hit.normal) * surfaceRotation;
                if (tiltForwardKey.Value.IsDown())
                {
                    tiltX = Mathf.Clamp(tiltX + tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (tiltBackwardKey.Value.IsDown())
                {
                    tiltX = Mathf.Clamp(tiltX - tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (tiltLeftKey.Value.IsDown())
                {
                    tiltZ = Mathf.Clamp(tiltZ - tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (tiltRightKey.Value.IsDown())
                {
                    tiltZ = Mathf.Clamp(tiltZ + tiltStepDegrees.Value, -maxTiltDegrees.Value, maxTiltDegrees.Value);
                }
                if (resetTiltKey.Value.IsDown())
                {
                    tiltX = 0f;
                    tiltZ = 0f;
                }

                ghost.transform.rotation = surfaceRotation * Quaternion.Euler(tiltX, 0f, tiltZ);
                // Root pivot is the pole base, so rotation leaves the surface anchor in place.
                ghost.transform.position = hit.point;
                lastTiltedGhost = ghost;
            }
        }

        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        private static class PlacementGhostTiltPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                AlignFlagToSurface(__instance);
            }
        }
    }
}
