using UnityEditor;
using UnityEngine;

/// <summary>
/// F120: the dropped-torch template of the InteractableKit (slot `droppedTorch`), part of KEEP IT LIT &gt; Build &gt; Interactables (build-missing-only).
/// Kit-material primitives, like the battery cell: a gunmetal body, head, grip band and an emissive lens. The axis is local +Z (the beam
/// direction); the root carries a solid BoxCollider so PlayerInteractor's spherecast finds it, and a disabled spot light child "Beam" that
/// DroppedTorch fills in at throw time (Flashlight.CopyBeamTo).
/// </summary>
public static partial class InteractableKitBuilder
{
    private static DroppedTorch BuildDroppedTorch(Transform parent, Vector3 localPosition)
    {
        Material bodyMat = LoadOrCreateMat("Interactable_TorchBody", new Color(0.13f, 0.14f, 0.16f), null, 0.6f);
        Material gripMat = LoadOrCreateMat("Interactable_TorchGrip", new Color(0.05f, 0.05f, 0.055f), null, 0.15f);
        Material lensMat = LoadOrCreateMat("Interactable_TorchLens", new Color(1f, 0.95f, 0.8f), new Color(1f, 0.92f, 0.7f) * 2.2f, 0.9f);

        GameObject root = new GameObject("DroppedTorch");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(0.09f, 0.09f, 0.26f);
        box.center = new Vector3(0f, 0f, 0.02f);

        // Cylinders are built along Y; rotate 90 degrees about X so their axis is the root's +Z.
        Quaternion toZ = Quaternion.Euler(90f, 0f, 0f);
        GameObject body = CylinderRH(root.transform, "Body", new Vector3(0f, 0f, -0.02f), 0.022f, 0.20f, bodyMat, keepCollider: false);
        body.transform.localRotation = toZ;
        GameObject head = CylinderRH(root.transform, "Head", new Vector3(0f, 0f, 0.105f), 0.032f, 0.05f, bodyMat, keepCollider: false);
        head.transform.localRotation = toZ;
        GameObject lens = CylinderRH(root.transform, "Lens", new Vector3(0f, 0f, 0.131f), 0.028f, 0.004f, lensMat, keepCollider: false);
        lens.transform.localRotation = toZ;
        GameObject grip = CylinderRH(root.transform, "Grip", new Vector3(0f, 0f, -0.04f), 0.025f, 0.07f, gripMat, keepCollider: false);
        grip.transform.localRotation = toZ;

        GameObject beamGo = new GameObject("Beam");
        beamGo.transform.SetParent(root.transform, false);
        beamGo.transform.localPosition = new Vector3(0f, 0f, 0.14f);
        Light beam = beamGo.AddComponent<Light>();
        beam.type = LightType.Spot;
        beam.range = 20f;
        beam.spotAngle = 60f;
        beam.intensity = 0f;
        beam.shadows = LightShadows.None;
        beam.enabled = false;

        DroppedTorch torch = root.AddComponent<DroppedTorch>();
        SerializedObject so = new SerializedObject(torch);
        so.FindProperty("beam").objectReferenceValue = beam;
        so.FindProperty("lens").objectReferenceValue = lens.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();
        return torch;
    }
}
