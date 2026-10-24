using UnityEngine;

/// <summary>
/// F111: the exit gate template (InteractableKit.escapeGate). Authored in local space with the origin at the floor centre of the wall
/// segment and +Z pointing into the maze; MazeGenerator.BuildExitGates places two per gate floor and ExitGate drives them, finding the
/// parts by name (the contract is on the kit slot's tooltip). Primitives and the kit's own materials, no pack, so the slot can always be filled.
/// </summary>
public static partial class InteractableKitBuilder
{
    // The frame is 3.5 m wide in all (two 0.3 m posts at x = +-1.6), leaving a 2.9 m x 3.2 m opening; MazeGenerator fills the rest of the
    // outer wall segment on each side with themed wall and the part above the lintel with a themed header.
    private const float GatePostX = 1.6f;
    private const float GatePostWidth = 0.3f;
    private const float GateHeight = 3.2f;
    private const float GateLintel = 0.3f;
    private const float GateWallDepth = 0.6f;
    private const float GateOpeningWidth = (GatePostX - GatePostWidth * 0.5f) * 2f;
    // Beside the frame, on the inside face of the wall (the wall is 0.5 m thick, so its inner face is at z = 0.25).
    private const float GateBoxX = 2.12f;
    private const float GateBoxY = 1.3f;
    private const float GateBoxZ = 0.34f;

    private static GameObject BuildEscapeGate(Transform parent, Vector3 localPosition)
    {
        Material steel = LoadOrCreateMat("Interactable_GateFrame", new Color(0.2f, 0.21f, 0.23f), null, 0.5f);
        Material slatMat = LoadOrCreateMat("Interactable_Shutter", new Color(0.24f, 0.25f, 0.27f), null, 0.4f);
        Material plate = LoadOrCreateMat("Interactable_Plate", new Color(0.16f, 0.17f, 0.19f), null, 0.35f);
        Material handleMat = LoadOrCreateMat("Interactable_GateLever", new Color(0.7f, 0.12f, 0.08f), null, 0.5f);
        Material bulbMat = LoadOrCreateMat("Interactable_GateLamp", new Color(0.75f, 0.7f, 0.55f), Color.black, 0.6f);
        Material pitMat = LoadOrCreateMat("Interactable_HatchPit", Color.black, null, 0f);
        Material stepMat = LoadOrCreateMat("Interactable_GateStep", new Color(0.32f, 0.33f, 0.34f), null, 0.3f);
        Material chainMat = LoadOrCreateMat("Interactable_GateChain", new Color(0.12f, 0.12f, 0.13f), null, 0.6f);

        GameObject root = new GameObject("EscapeGate");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        // The frame: two posts and a lintel, all solid, so with the shutter and the themed pieces MazeGenerator adds they seal the segment.
        Primitive(root.transform, PrimitiveType.Cube, "PostL", new Vector3(-GatePostX, GateHeight * 0.5f, 0f), new Vector3(GatePostWidth, GateHeight, GateWallDepth), steel, keepCollider: true);
        Primitive(root.transform, PrimitiveType.Cube, "PostR", new Vector3(GatePostX, GateHeight * 0.5f, 0f), new Vector3(GatePostWidth, GateHeight, GateWallDepth), steel, keepCollider: true);
        Primitive(root.transform, PrimitiveType.Cube, "Lintel", new Vector3(0f, GateHeight + GateLintel * 0.5f, 0f), new Vector3((GatePostX + GatePostWidth * 0.5f) * 2f, GateLintel, GateWallDepth), steel, keepCollider: true);

        // The shutter: a slatted roll-up door recessed in the wall, with one solid box over the whole opening that rises with it.
        GameObject shutter = new GameObject("Shutter");
        shutter.transform.SetParent(root.transform, false);
        BoxCollider shutterCollider = shutter.AddComponent<BoxCollider>();
        shutterCollider.center = new Vector3(0f, GateHeight * 0.5f, 0f);
        shutterCollider.size = new Vector3(GateOpeningWidth + 0.1f, GateHeight, 0.2f);
        const int slats = 16;
        float slatPitch = GateHeight / slats;
        for (int i = 0; i < slats; i++)
        {
            Primitive(shutter.transform, PrimitiveType.Cube, "Slat" + i, new Vector3(0f, slatPitch * (i + 0.5f), 0f), new Vector3(GateOpeningWidth, slatPitch * 0.82f, 0.08f), slatMat, keepCollider: false);
        }
        Primitive(shutter.transform, PrimitiveType.Cube, "Bar", new Vector3(0f, 0.07f, 0.02f), new Vector3(GateOpeningWidth, 0.14f, 0.14f), steel, keepCollider: false);

        // The padlocked look (Key Hunt): crossed chains and a lock in front of the shutter. Hidden until the gates are locked.
        GameObject chain = new GameObject("Chain");
        chain.transform.SetParent(root.transform, false);
        Primitive(chain.transform, PrimitiveType.Cylinder, "LinkA", new Vector3(0f, GateHeight * 0.45f, 0.12f), new Vector3(0.06f, 1.9f, 0.06f), chainMat, keepCollider: false, localRotation: Quaternion.Euler(0f, 0f, 55f));
        Primitive(chain.transform, PrimitiveType.Cylinder, "LinkB", new Vector3(0f, GateHeight * 0.45f, 0.12f), new Vector3(0.06f, 1.9f, 0.06f), chainMat, keepCollider: false, localRotation: Quaternion.Euler(0f, 0f, -55f));
        Primitive(chain.transform, PrimitiveType.Cube, "Padlock", new Vector3(0f, GateHeight * 0.45f, 0.17f), new Vector3(0.22f, 0.26f, 0.1f), steel, keepCollider: false);
        chain.SetActive(false);

        // The lever box on the wall beside the frame. LeverFocus is the solid box PlayerInteractor's probe hits; MazeGenerator puts the GateLever on the box.
        GameObject box = new GameObject("LeverBox");
        box.transform.SetParent(root.transform, false);
        box.transform.localPosition = new Vector3(GateBoxX, GateBoxY, GateBoxZ);
        Primitive(box.transform, PrimitiveType.Cube, "Plate", Vector3.zero, new Vector3(0.4f, 0.55f, 0.18f), plate, keepCollider: false);
        Primitive(box.transform, PrimitiveType.Cube, "Slot", new Vector3(0f, 0f, 0.092f), new Vector3(0.05f, 0.34f, 0.01f), chainMat, keepCollider: false);

        GameObject focus = new GameObject("LeverFocus");
        focus.transform.SetParent(box.transform, false);
        BoxCollider focusCollider = focus.AddComponent<BoxCollider>();
        focusCollider.center = new Vector3(0f, 0f, 0.1f);
        focusCollider.size = new Vector3(0.5f, 0.7f, 0.4f);

        // The handle hinges at the bottom of the slot and swings toward the player about local X as progress fills.
        GameObject handle = new GameObject("LeverHandle");
        handle.transform.SetParent(box.transform, false);
        handle.transform.localPosition = new Vector3(0f, -0.14f, 0.1f);
        Primitive(handle.transform, PrimitiveType.Cylinder, "Rod", new Vector3(0f, 0.15f, 0f), new Vector3(0.04f, 0.15f, 0.04f), steel, keepCollider: false);
        Primitive(handle.transform, PrimitiveType.Sphere, "Knob", new Vector3(0f, 0.31f, 0f), Vector3.one * 0.1f, handleMat, keepCollider: false);

        // Three lamps over the box, each a bulb on a small plate with a disabled light that comes on with it.
        for (int i = 0; i < 3; i++)
        {
            GameObject lamp = new GameObject("Lamp" + i);
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localPosition = new Vector3(GateBoxX + (i - 1) * 0.13f, GateBoxY + 0.5f, GateBoxZ - 0.02f);
            Primitive(lamp.transform, PrimitiveType.Sphere, "Bulb", new Vector3(0f, 0f, 0.05f), Vector3.one * 0.09f, bulbMat, keepCollider: false);
            Primitive(lamp.transform, PrimitiveType.Cylinder, "Cage", new Vector3(0f, 0f, 0.0f), new Vector3(0.13f, 0.01f, 0.13f), chainMat, keepCollider: false, localRotation: Quaternion.Euler(90f, 0f, 0f));

            GameObject lightGo = new GameObject("Light");
            lightGo.transform.SetParent(lamp.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.2f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.25f, 1f, 0.7f);
            light.range = 3.5f;
            light.intensity = 0.7f;
            light.shadows = LightShadows.None;
            light.enabled = false;
        }

        // Outside the wall: the head of a stairwell going down, a black pit with lit step edges and handrails (no colliders, the stub's floor is under it).
        GameObject stairs = new GameObject("Stairwell");
        stairs.transform.SetParent(root.transform, false);
        stairs.transform.localPosition = new Vector3(0f, 0f, -3.2f);
        Primitive(stairs.transform, PrimitiveType.Cube, "Pit", new Vector3(0f, 0.012f, 0f), new Vector3(2.6f, 0.02f, 2.2f), pitMat, keepCollider: false);
        for (int i = 0; i < 4; i++)
        {
            Primitive(stairs.transform, PrimitiveType.Cube, "Step" + i, new Vector3(0f, 0.024f, 0.75f - i * 0.5f), new Vector3(2.4f, 0.01f, 0.1f - i * 0.015f), stepMat, keepCollider: false);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            Primitive(stairs.transform, PrimitiveType.Cylinder, "Rail" + (side < 0 ? "L" : "R"), new Vector3(side * 1.4f, 0.45f, 0f), new Vector3(0.05f, 0.45f, 0.05f), steel, keepCollider: false);
            Primitive(stairs.transform, PrimitiveType.Cylinder, "RailBar" + (side < 0 ? "L" : "R"), new Vector3(side * 1.4f, 0.9f, 0f), new Vector3(0.04f, 1.1f, 0.04f), steel, keepCollider: false, localRotation: Quaternion.Euler(90f, 0f, 0f));
        }

        // The light that spills out of the stub once the gate is open. Off until then.
        GameObject stubLight = new GameObject("StubLight");
        stubLight.transform.SetParent(root.transform, false);
        stubLight.transform.localPosition = new Vector3(0f, 2.3f, -2.4f);
        Light spill = stubLight.AddComponent<Light>();
        spill.type = LightType.Point;
        spill.color = new Color(0.25f, 1f, 0.7f);
        spill.range = 9f;
        spill.intensity = 3f;
        spill.shadows = LightShadows.None;
        spill.enabled = false;

        return root;
    }
}
