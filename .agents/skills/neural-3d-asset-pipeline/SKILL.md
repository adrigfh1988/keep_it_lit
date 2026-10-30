---
name: neural-3d-asset-pipeline
description: >-
  Generate stylized-gritty Indie Horror 3D characters and props (.glb, .blend, rigged .fbx + 2048x2048 PBR Albedo/Normal/Roughness maps)
  locally without paid external 3D API keys. Combines Nano Banana 2D-to-Orthographic Turnaround generation, locally cached
  neural 3D engines on D:\AI_Models (Tencent Hunyuan3D-2.1 / 2mv / Part / PaintPBR / FlashVDM, VAST-AI TripoSG / TripoSF / SkinTokens / UniRig,
  Microsoft TRELLIS.2-4B / TRELLIS-image-large), local RMBG-2.0 / U2Net (rembg) foreground segmentation & isotropic edge diffusion, and
  Blender 5.2 Cycles GPU (OptiX/CUDA) Tri-Planar PBR projection, double-wall cleanup, cohesive Indie 2-piece character assembly, and skeletal rigging.
---

# Neural 3D Indie Horror Character & Prop Pipeline (Zero Paid API Keys)

## Core Philosophy: Cohesive Indie Horror Over Surgical AAA Splicing

1. **Work WITH Neural 3D Reconstructors, Never Against Them:**
   - Local 3D Diffusion Transformers (`Hunyuan3D-2mv`, `Hunyuan3D-2.1`, `TRELLIS.2-4B`, `TRELLIS-image-large`, `TripoSG`) excel at **cohesive, bold, readable Indie Horror silhouettes** with strong primary and secondary forms (chunky stylized-gritty proportions, deep shadowed eye sockets, heavy furrowed brows, bold beards/mustaches, iconic hats/masks, and heavy weathered coats/aprons).
   - **NEVER attempt "AAA surgical splicing" of 15–20 tiny anatomical sub-meshes** (never generate separate neural `.glb` files for bare skin head + separate ears + separate eyebrows + separate mustache + separate beard + procedural eyelids + 28 procedural teeth + raycast-wrapped apron shells). Because neural 3D models have slight nonlinear coordinate drift (`~15–25 mm`) between independent runs, stitching 19 separate anatomical pieces produces uncanny alignment flaws, raycast spikes, and double-baked features while taking 10× longer.
2. **Cohesive 2-Piece Indie Character Architecture + Standalone Handheld Props:**
   - **Piece 1 — `<Character>_Head` (Unified Head + Beard + Ears + Hat Bust, `15K–24K` Tris, `2048×2048` UV):**
     - Generated from a single **85%–90% frame Close-Up Orthographic Head/Hat/Beard Bust Turnaround**. Because the face, eyes, brows, nose, beard, ears, hat, and neck are reconstructed together in **one single neural pass**, every facial feature shares one 100% self-consistent 3D coordinate system with zero cross-mesh misalignment!
   - **Piece 2 — `<Character>_Body` (Unified Torso + Apron/Coat + Limbs + Gloves + Boots, `15K–20K` Tris, `2048×2048` UV):**
     - Generated from a single **Full-Body Orthographic Turnaround** with the character's signature outer garment (e.g., butcher's apron, orderly coat) worn directly on the body in A-pose. The body's low-res head is cleanly bisected off at the collar plane (`bmesh.ops.bisect_plane`), and the unified `<Character>_Head` bust's tapered neck column tucks seamlessly into the collar opening.
   - **Standalone Handheld Props (`<Character>_Cleaver`, `<Character>_Ladle`, etc., `2K–4K` Tris each):**
     - Weapons and tools held in the hands or hung on the belt are built as separate models so they can be parented to hand/belt bones, swapped, or dropped independently, with clean per-material sub-meshes (e.g., steel blade vs. wooden handle scales vs. brass rivets).
3. **NEVER hand-code procedural `bmesh` spheres/cylinders for organic anatomy:**
   - Never spawn blind `primitive_uv_sphere_add` eyeballs, procedural torus eyelids, or sine-wave vertex displacement on a character face. Let the close-up neural bust sculpt carry the cohesive Indie Horror facial expression.

---

## Local Storage & Models Zoo (`D:\AI_Models`)

- **All models and repositories MUST live on the `D:` drive (`D:\AI_Models\`)**, where over 3 TB of storage is available:
  - **HuggingFace Cache:** `D:\AI_Models\huggingface\hub` (`HF_HOME=D:\AI_Models\huggingface`, with an NTFS directory junction at `C:\Users\adria\.cache\huggingface` $\rightarrow$ `D:\AI_Models\huggingface`).
  - **Hunyuan3D Cache:** `D:\AI_Models\hy3dgen` (`HY3DGEN_MODELS=D:\AI_Models\hy3dgen`, with an NTFS directory junction at `C:\Users\adria\.cache\hy3dgen` $\rightarrow$ `D:\AI_Models\hy3dgen`).
  - **Rembg / RMBG-2.0 Cache:** `D:\AI_Models\rembg` (NTFS directory junction at `C:\Users\adria\.rembg` $\rightarrow$ `D:\AI_Models\rembg`, containing `briaai/RMBG-2.0` `bria-rmbg.onnx` and `u2net.onnx`).
  - **Python Virtual Environment:** `D:\Unity\essentials\Models\Hunyuan3D-2\.venv\Scripts\python.exe` (PyTorch + CUDA 12, `hy3dgen`, `huggingface_hub`, `hf_xet`, `trimesh`, `pymeshlab`, `rembg`, `onnxruntime`).
  - **GitHub Repositories (`D:\AI_Models\3d-models\`):**
    - `D:\AI_Models\3d-models\Hunyuan3D-2` (`Tencent-Hunyuan/Hunyuan3D-2`)
    - `D:\AI_Models\3d-models\Hunyuan3D-2.1` (`Tencent-Hunyuan/Hunyuan3D-2.1`)
    - `D:\AI_Models\3d-models\Hunyuan3D-Part` (`Tencent-Hunyuan/Hunyuan3D-Part`)
    - `D:\AI_Models\3d-models\TripoSG` (`VAST-AI-Research/TripoSG`)
    - `D:\AI_Models\3d-models\TripoSF` (`VAST-AI-Research/TripoSF`)
    - `D:\AI_Models\3d-models\SkinTokens` (`VAST-AI-Research/SkinTokens`)
    - `D:\AI_Models\3d-models\UniRig` (`VAST-AI-Research/UniRig`)
    - `D:\AI_Models\3d-models\TRELLIS` (`microsoft/TRELLIS`)
    - `D:\AI_Models\3d-models\TRELLIS.2` (`microsoft/TRELLIS.2`)
- **VRAM Budgeting (16 GB RTX 5080):**
  - **Shape generation only:** ~**6 GB–10 GB VRAM** (`Hunyuan3D-DiT` / `TripoSG` / `TRELLIS`).
  - **Shape + neural texture generation together:** ~**16 GB VRAM** (`--low_vram_mode --enable_flashvdm` or `PYTORCH_CUDA_ALLOC_CONF=expandable_segments:True`).
  - **Best practice on 16 GB VRAM:** Save the untextured `.glb` and call `del pipeline_shapegen; torch.cuda.empty_cache()` before loading `Hunyuan3DPaintPipeline`, `SkinTokens`, or running Blender Cycles GPU Tri-Planar PBR baking.

### 1. Tencent Hunyuan3D Series (`D:\AI_Models\3d-models\Hunyuan3D-2`, `Hunyuan3D-2.1`, `Hunyuan3D-Part`)

| Series | Model / Subfolder | Repo ID | Params | Purpose & Notes |
|---|---|---|---|---|
| **Hunyuan3D-2mv** | `hunyuan3d-dit-v2-mv` | `tencent/Hunyuan3D-2mv` | 1.1B | **Recommended Full-Quality Multi-View (Front/Left/Back) -> Shape** (30–50 steps) |
| **Hunyuan3D-2mv** | `hunyuan3d-dit-v2-mv-turbo` | `tencent/Hunyuan3D-2mv` | 1.1B | Fast step-distilled Multi-View -> Shape (5–15 steps with `enable_flashvdm()`) |
| **Hunyuan3D-2.1** | `hunyuan3d-dit-v2-1` + `hunyuan3d-vae-v2-1` | `tencent/Hunyuan3D-2.1` | 3.3B | High-capacity single-image-to-shape DiT + ShapeVAE (`Hunyuan3D-2.1/hy3dshape`) |
| **Hunyuan3D-2.1** | `hunyuan3d-paintpbr-v2-1` | `tencent/Hunyuan3D-2.1` | 2.0B | Native PBR texture synthesis (Albedo, Metallic, Roughness via `Hunyuan3D-2.1/hy3dpaint`) |
| **Hunyuan3D-Part** | `p3sam` + `model` | `tencent/Hunyuan3D-Part` | 1.5B | Native 3D part segmentation (`P3-SAM`) and structure-coherent part decomposition (`X-Part`) |

### 2. VAST-AI Tripo & Auto-Rigging Series (`D:\AI_Models\3d-models\TripoSG`, `TripoSF`, `SkinTokens`, `UniRig`)

| Model | Repo ID | Params | Purpose & Key README Features |
|---|---|---|---|
| **TripoSG** | `VAST-AI/TripoSG` (+ `briaai/RMBG-1.4`) | 1.5B | Rectified Flow Transformer + SDF VAE. Supports direct face budget via `--faces 15000`. |
| **TripoSF** | `VAST-AI/TripoSF` | VAE | SparseFlex ultra-high-resolution ($1024^3$) arbitrary-topology & open-surface mesh reconstruction (`ckpts/vae/pretrained_TripoSFVAE_256i1024o.safetensors`). |
| **SkinTokens (TokenRig)** | `VAST-AI/SkinTokens` | 0.6B + CVAE | **Successor to UniRig (2026):** Unified autoregressive skeleton + per-vertex skinning weight prediction via FSQ-CVAE + Qwen3-0.6B + GRPO (`demo.py --input model.glb --output rigged.glb --use_transfer`, 14 GB VRAM). |
| **UniRig** | `VAST-AI/UniRig` | SIGGRAPH '25 | Two-stage autoregressive skeleton tree + Bone-Point Cross Attention skinning framework. |

### 3. Microsoft TRELLIS Series (`D:\AI_Models\3d-models\TRELLIS.2`, `TRELLIS`)

| Model | Repo ID | Params | Purpose & Key README Features |
|---|---|---|---|
| **TRELLIS.2-4B** | `microsoft/TRELLIS.2-4B` | 4.0B | **Latest (Dec 2025):** Field-free **O-Voxel** representation up to $1536^3$ resolution; supports open surfaces, non-manifold geometry, and full PBR materials (`Base Color`, `Roughness`, `Metallic`, `Opacity`) via `Trellis2ImageTo3DPipeline` + `o_voxel.postprocess.to_glb`. |
| **TRELLIS-image-large** | `microsoft/TRELLIS-image-large` | 1.2B | Unified Structured LATent (`SLAT`) model decoding to Mesh (FlexiCubes) + 3D Gaussians (`to_glb` with `texture_size=2048`). |

### 4. Local Foreground Segmentation (`D:\AI_Models\rembg`)

| Model | Repo ID | File | Purpose & Notes |
|---|---|---|---|
| **RMBG-2.0** | `briaai/RMBG-2.0` | `models/bria-rmbg/bria-rmbg.onnx` (`1.02 GB`) | High-precision BiRefNet-based foreground segmentation (`rembg.new_session("bria-rmbg")`). |
| **U2Net** | `u2net` | `models/u2net/u2net.onnx` (`176 MB`) | Fast default foreground segmentation (`rembg.new_session("u2net")`). |

---

## Stage 1: Indie Horror Art Direction & Orthographic Turnarounds

Use `invoke_subagent` with `image-generator` to lock a bold, stylized-gritty Indie Horror look (think *Little Nightmares* meets gritty 1940s PS2/Indie survival horror — strong silhouettes, chunky readable cloth folds, deep dramatic brow ridges, cohesive facial hair and headgear, zero fragile single-pixel hair strands):

1. **Hero Concept / Reference Render**:
   - Single full-body A-pose character render against a solid neutral dark grey backdrop (`#2a2a2e`), establishing the exact Indie Horror proportions, colors, apron/coat silhouette, headgear, and facial personality.
2. **Pass A — Unified Full-Body Orthographic Turnaround Sheet (Front / Side / Back)**:
   - Pass the Hero Render as reference and generate a 3-view horizontal turnaround (`Front`, `Right or Left Profile`, `Back`) of the complete clothed body in a clean A-pose (arms angled ~35°–40° away from the torso so the underarms and waist are unobstructed, wearing the apron/coat, gloves, trousers, and boots).
   - **Requirements:** Flat, even, shadowless studio albedo lighting on solid `#2a2a2e` grey, horizontally aligned heights across all 3 views, zero text labels, zero horizontal guide lines, zero floor shadows.
3. **Pass B — Unified Close-Up Head + Beard + Hat Bust Turnaround Sheet (Front / Side / Back)**:
   - Pass the Hero Render as reference and generate a **dedicated 85%–90% frame Close-Up Orthographic Bust Turnaround** (`Front`, `Profile`, `Back`) from the base of the neck / collarbones (`clavicles`) up to the top of the hat/head.
   - **CRITICAL INDIE COHESION RULE:** Include the character's **complete head, face, eyes, ears, eyebrows, mustache/beard, and hat together in this single bust turnaround**. Do NOT strip the beard, ears, or hat into separate turnaround sheets! Keeping the entire head+beard+hat bust in one unified turnaround guarantees 100% self-consistent proportions and zero alignment seams.
4. **Pass C — Standalone Handheld Prop Turnarounds (Optional, 1 per Prop)**:
   - Generate clean 3-view orthographic turnarounds (or build precision hard-surface multi-part meshes in Blender projected with the turnaround textures) for handheld weapons/tools (e.g., meat cleaver, soup ladle).

---

## Stage 2: Neural 3D Mesh Reconstruction (`Hunyuan3D-2mv`)

### Critical Rules for Multi-View Cropping & `rembg`
1. **Square-Pad Every Cropped View (`crop_padded_square`):**
   - `Hunyuan3D-2mv` resizes each view (`front`, `left`, `back`) to `518×518`. Always crop the tight foreground bounding box of each view (`front`, `side`, `back`), normalize their vertical pixel height so all 3 views match the exact same top-to-bottom scale, and pad each view to a square canvas before feeding `Hunyuan3D-2mv`.
2. **Orientation of Side View (`left` key in `Hunyuan3D-2mv`):**
   - In `Hunyuan3D-2mv`, the `"left"` view camera looks at the **left side of the character** (meaning in the 2D image, the character's face/nose points to the **LEFT**). If your turnaround's profile view faces right, flip it horizontally (`ImageOps.mirror`) before passing it as `"left"`.
3. **Always Call `rembg(img)` Before Converting to RGBA:**
   - Never convert an RGB image to RGBA before calling `rembg`, or `BackgroundRemover` may skip background removal.

```python
import os, sys, torch
os.environ["HF_HOME"] = r"D:\AI_Models\huggingface"
os.environ["HY3DGEN_MODELS"] = r"D:\AI_Models\hy3dgen"
sys.path.insert(0, r"D:\AI_Models\3d-models\Hunyuan3D-2")
from PIL import Image
from hy3dgen.rembg import BackgroundRemover
from hy3dgen.shapegen import Hunyuan3DDiTFlowMatchingPipeline

rembg = BackgroundRemover()
images = {}
for key, path in [("front", front_sq_path), ("left", left_facing_side_sq_path), ("back", back_sq_path)]:
    img = Image.open(path).convert("RGB")
    images[key] = rembg(img)

pipeline = Hunyuan3DDiTFlowMatchingPipeline.from_pretrained(
    "tencent/Hunyuan3D-2mv",
    subfolder="hunyuan3d-dit-v2-mv",
    variant="fp16",
)
mesh = pipeline(
    image=images,
    num_inference_steps=35,
    octree_resolution=384,
    num_chunks=20000,
    generator=torch.manual_seed(12345),
    output_type="trimesh",
)[0]
mesh.export(output_glb_path)
```

### Alternative Single-Image Engines (`TRELLIS.2-4B` / `Hunyuan3D-2.1` / `TripoSG`)
- **`microsoft/TRELLIS.2-4B` (O-Voxel + Native PBR Materials):**
  ```python
  import os, sys
  os.environ["HF_HOME"] = r"D:\AI_Models\huggingface"
  os.environ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"
  sys.path.insert(0, r"D:\AI_Models\3d-models\TRELLIS.2")
  from PIL import Image
  from trellis2.pipelines import Trellis2ImageTo3DPipeline
  import o_voxel

  pipeline = Trellis2ImageTo3DPipeline.from_pretrained("microsoft/TRELLIS.2-4B")
  pipeline.cuda()
  mesh = pipeline.run(Image.open(hero_image_path))[0]
  glb = o_voxel.postprocess.to_glb(
      vertices=mesh.vertices, faces=mesh.faces, attr_volume=mesh.attrs,
      coords=mesh.coords, attr_layout=mesh.layout, voxel_size=mesh.voxel_size,
      aabb=[[-0.5, -0.5, -0.5], [0.5, 0.5, 0.5]], decimation_target=20000,
  )
  glb.export(output_glb_path)
  ```
- **`VAST-AI/SkinTokens` (TokenRig Automated Skeleton & Skin Weight Prediction):**
  ```powershell
  # From D:\AI_Models\3d-models\SkinTokens:
  python demo.py --input "path/to/character.glb" --output "path/to/rigged.glb" --use_transfer
  ```

---

## Stage 3: Foreground Segmentation, Color-Matched Slices & PBR Map Generation

Before projecting Front, Side, and Back turnaround slices onto the 3D meshes in Blender:
1. **Strip Any Horizontal Guide Lines:** If the image generator drew faint 1–2 px horizontal orthographic alignment lines across the turnaround, scrub them or apply a narrow vertical median filter in background gaps before segmentation.
2. **Segment with `rembg`, Erode 3px, and Apply 28px 8-Neighbor Isotropic Dilation:**
   - Eroding the alpha mask by `3 px` removes dark grey background halos at the silhouette edge, and `28 px` 8-neighbor isotropic dilation feathers the interior border colors outward so grazing triangles never sample grey background pixels.
3. **Color-Match `Side` and `Back` to `Front` Albedo:**
   - Match the per-channel RGB mean and standard deviation of `side` and `back` (within their eroded foreground masks) to `front` so multi-view blending produces zero brightness or hue seams.
4. **Generate Multi-Band Height, OpenGL Normal, and Wet/Dry Roughness Maps:**
   - Compute a multi-band detail height map (`0.45 * gray + 0.35 * med_detail + 0.20 * fine_detail`), convert via Sobel gradients to an OpenGL tangent normal map (`*_normal.png`), and compute a roughness map (`*_roughness.png`, `0.15–0.92`) where bloodstains, grease, metal, and eyes have lower roughness (wetter/shinier) and matte cloth/skin have higher roughness.

---

## Stage 4: Cohesive Indie 2-Piece Assembly, Double-Wall Cleanup, Tri-Planar Baking & Rigging

### 1. Strip the `Hunyuan3D-2mv` Inner Double-Wall Before Decimating (`strip_inner_double_wall_and_decimate`)
`Hunyuan3D-2mv` marching-cubes extracts a thin double-walled shell (an outer surface and an inner offset wall separated by `~0.012`). Running Blender's `DECIMATE` directly on a double-walled mesh collapses edges across the wall and creates holes. Always strip the inner wall and keep only the largest outer connected component before decimating:
- **`<Character>_Head` (Unified Head + Beard + Hat Bust):** Target **`18,000–24,000` triangles** (`2048×2048` dedicated UV map).
- **`<Character>_Body` (Unified Torso + Apron + Limbs + Boots):** Target **`15,000–18,000` triangles** (`2048×2048` dedicated UV map).

```python
def strip_inner_double_wall_and_decimate(obj, target_faces=20000):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    bvh = BVHTree.FromBMesh(bm)
    center = Vector((0.0, 0.0, 0.0))
    to_del = []
    for f in bm.faces:
        c = f.calc_center_median()
        n = f.normal
        rad = (c - center)
        rad.z *= 0.5
        if rad.length > 1e-6:
            rad.normalize()
        if n.dot(rad) < 0.05:
            loc, no, idx, dist = bvh.ray_cast(c - n * 0.001, -n, 0.032)
            if loc is not None and no.dot(n) < -0.25:
                to_del.append(f)
    bmesh.ops.delete(bm, geom=to_del, context='FACES')
    bm.faces.ensure_lookup_table()
    visited, comps = set(), []
    for f in bm.faces:
        if f.index in visited:
            continue
        comp, stack = [], [f]
        visited.add(f.index)
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf.index not in visited:
                        visited.add(nf.index)
                        stack.append(nf)
        comps.append(comp)
    if len(comps) > 1:
        comps.sort(key=len, reverse=True)
        keep = {f.index for f in comps[0]}
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context='FACES')
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    if len(obj.data.polygons) > target_faces:
        dec = obj.modifiers.new(name="Decimate", type='DECIMATE')
        dec.ratio = float(target_faces) / float(len(obj.data.polygons))
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=dec.name)
```

### 2. Clean Collar-Plane Bisection & Neck Tuck (No "Cut Mask" Seam)
- On `<Character>_Body`, remove the low-res full-body head cleanly at the collar line (`z = collar_z`, e.g., `~1.54 m` on a `1.90 m` character) using **`bmesh.ops.bisect_plane(..., clear_outer=True)`** and gently bevel/roll the collar rim inward so there are zero jagged triangle teeth.
- Scale and position the high-detail `<Character>_Head` bust (which includes the hat, head, beard, and lower neck base) so its tapered neck base tucks **`3–5 cm` down inside the body's collar opening** (`z = 1.50 m -> 1.55 m`). Because the beard and collar overlap naturally in depth, the head-to-body transition is 100% seamless from every camera angle.

### 3. Tri-Planar Projection with Strict Side-View Masking on `<Character>_Head`
- Compute `UV_Front` `(tx, tz)`, `UV_Back` `(1 - tx, tz)`, and `UV_Side` `(ty, tz)` from each piece's own tight bounding box (`[0.004..0.996]`), before assembling/rotating, so each piece (`_Head` and `_Body`) maps 1:1 to its own turnaround crops.
- **Strict Side-View Gate on `<Character>_Head` (`|tx - 0.5| > 0.31` and `|Normal.X| > 0.76`):**
  - Never let `UV_Side` project onto the front of the face, eye sockets, nose, or cheeks! Gate `UV_Side` so it only blends in on the far lateral temples/ears/side-hat where both `|tx - 0.5| > 0.31` and `|Normal.X| > 0.76`:
    ```python
    tx_gate = np.clip((np.abs(tx - 0.5) - 0.31) / 0.08, 0.0, 1.0)
    nx_gate = np.clip((np.abs(nx) - 0.76) / 0.12, 0.0, 1.0)
    w_side = tx_gate * nx_gate
    ```
- **NEVER use `ShaderNodeBrightContrast` with negative brightness** on linear baked albedo (it crushes dark shadows to pure black `RGB = 0`).
- Bake `2048×2048` Albedo, Roughness, and Normal maps in Cycles GPU (`OPTIX`/`CUDA`) onto clean Smart-UV islands (`island_margin=0.008`), or compute smooth per-vertex/per-texel multi-view projection directly into the `2048×2048` texture atlas.

### 4. Multi-Component Handheld Props (Material-Separated Sub-Meshes)
- For signature handheld props like a butcher's cleaver or soup ladle, separate the prop into clean sub-meshes by physical material so dielectric wood (`Metallic = 0.0`) and forged steel/brass (`Metallic = 0.88–0.90`) never bleed across shared triangles:
  - Example (`CookBruno_Cleaver`): `Cleaver_Blade` (full-tang forged steel blade with beveled cutting edge + hanging hole, `Metallic = 0.88`), `Cleaver_Handle` (two contoured walnut wood grip scales bolted onto the tang with a real 3D step, `Metallic = 0.0`), and `Cleaver_Rivets` (three proud cylindrical brass pins, `Metallic = 0.90`).

### 5. Skeletal Rigging, BlendShapes Gotcha & Unity FBX Export (`FBX_SCALE_ALL`)
- **Blender 5.2 Python `shape_key_add` Gotcha:** `mesh_obj.shape_key_add(name=..., from_mix=False)` initializes `.value = 1.0` by default in Blender 5.2 Python. **Always set `sk.value = 0.0` immediately after creating each shape key** (`Mouth_Open`, `Jaw_Talk`, `Breathe_In`) so they do not stack at `100%` in the rest pose!
- Build the humanoid `Armature` (`Joints`: `Hips`, `Spine`, `Spine1`, `Spine2`, `Neck`, `Head`, `Jaw`, `Hat_Top`, `Left/RightShoulder`, `Arm`, `ForeArm`, `Hand`, `UpLeg`, `Leg`, `Foot`, `ToeBase`), assign clean bone weights, and export with `apply_scale_options='FBX_SCALE_ALL'`.

---

## Step-by-Step Verification Checklist Before Presenting Character Renders

1. **Cohesive Turnaround Check (`view_file`):** Do we have two clean, unified character turnarounds — (A) **Full-Body A-Pose with Apron/Garments** and (B) **Close-Up Head + Beard + Hat Bust (85%–90% frame)** — with consistent Indie Horror styling, flat studio lighting, and zero text/grid lines?
2. **Double-Wall Stripping & Indie Triangle Budget:** Did the script run `strip_inner_double_wall_and_decimate` on both `<Character>_Head` (`18K–24K` tris) and `<Character>_Body` (`15K–18K` tris)?
3. **Side-View Masking on Head:** Is `UV_Side` strictly gated by `|tx - 0.5| > 0.31` and `|Normal.X| > 0.76` on `<Character>_Head` so side projection never smears across the eyes, nose, or cheeks?
4. **Seamless Neck-into-Collar Tuck:** Was `<Character>_Body`'s low-res head bisected cleanly at the collar plane with `bmesh.ops.bisect_plane`, and does `<Character>_Head`'s neck/beard base tuck cleanly into the collar with zero horizontal mask seam?
5. **Visual Render Check (`view_file`):** Render `front`, `side`, `back`, and `closeup` preview shots in Blender Cycles, inspect every image with `view_file`, and confirm the character reads as a cohesive, creepy, game-ready Indie Horror character before asking the user for approval to import into Unity.
