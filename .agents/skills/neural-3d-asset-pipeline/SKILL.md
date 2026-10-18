---
name: neural-3d-asset-pipeline
description: >-
  Generate photorealistic, high-poly and game-ready 3D assets (.glb, .blend, rigged .fbx + 2048x2048 PBR Albedo/Normal/Roughness maps)
  locally without paid external 3D API keys. Combines Nano Banana 2D-to-Orthographic Turnaround generation, locally cached
  neural 3D engines on D:\AI_Models (Tencent Hunyuan3D-2.1 / 2mv / PaintPBR / FlashVDM, VAST-AI TripoSG, Microsoft TRELLIS-image-large),
  local U2Net (rembg) foreground segmentation & isotropic edge diffusion, and Blender 5.2 Cycles GPU (OptiX/CUDA) Tri-Planar PBR
  projection, 15K-triangle decimation, single-atlas baking, and skeletal rigging.
---

# Neural 3D High-Poly & Game-Ready Asset Pipeline (Zero Paid API Keys)

## Why This Pipeline Exists (And What NEVER To Do)
- **NEVER hand-code procedural `bmesh` cylinder lofting, metaballs, or sine-wave vertex displacement for organic or sculpted 3D assets** (corpses, creatures, cloth-wrapped props, statues, ornate furniture). Procedural math produces balloon-like tubes or jagged visual-hull spikes.
- **LLMs cannot write 300,000-triangle sculpted coordinates in Python.** Modern 3D tools (Hunyuan3D, TripoSG, TRELLIS, Rodin) condition a **3D Diffusion Transformer (DiT)** on clean 2D/orthographic views to predict a 3D **Signed Distance Field (SDF)** or **Structured 3D Latent (SLAT)**, extract the high-poly mesh, and project/bake PBR textures onto the geometry.
- This skill runs that entire industrial 4-stage pipeline locally using pre-downloaded open-weights models on `D:\AI_Models`.

---

## Local Storage & Models Zoo (`D:\AI_Models`)

- **All models and repositories MUST live on the `D:` drive (`D:\AI_Models\`)**, where over 3 TB of storage is available:
  - **HuggingFace Cache:** `D:\AI_Models\huggingface\hub` (`HF_HOME=D:\AI_Models\huggingface`, with an NTFS directory junction at `C:\Users\adria\.cache\huggingface` $\rightarrow$ `D:\AI_Models\huggingface` so zero weights consume `C:` drive space).
  - **GitHub Repositories:**
    - `D:\AI_Models\3d-models\Hunyuan3D-2`
    - `D:\AI_Models\3d-models\TripoSG`
    - `D:\AI_Models\3d-models\TRELLIS`
- **VRAM Budgeting (16 GB RTX 5080):**
  - **Shape generation only:** ~**6 GB–8 GB VRAM** (`Hunyuan3D-DiT` / `TripoSG`).
  - **Shape + neural texture generation together:** ~**16 GB VRAM** (`--low_vram_mode --enable_flashvdm`).
  - **Best practice on 16 GB VRAM:** When chaining shape generation and `Hunyuan3DPaintPipeline` in a script, save the untextured `.glb` and call `del pipeline_shapegen; torch.cuda.empty_cache()` before loading `Hunyuan3DPaintPipeline` (or use our fast Cycles GPU Tri-Planar PBR baker in Stages 3 & 4).

### 1. Tencent Hunyuan3D Series (`D:\AI_Models\3d-models\Hunyuan3D-2`)

| Series | Model / Subfolder | Repo ID | Params | Purpose & Notes |
|---|---|---|---|---|
| **Hunyuan3D-2.1** | `hunyuan3d-dit-v2-1` | `tencent/Hunyuan3D-2.1` | 3.0B | Latest high-capacity single-image-to-shape DiT + VAE |
| **Hunyuan3D-2.1** | `hunyuan3d-paintpbr-v2-1` | `tencent/Hunyuan3D-2.1` | 1.3B | Native PBR texture synthesis (Albedo, Metallic, Roughness) |
| **Hunyuan3D-2mv** | `hunyuan3d-dit-v2-mv-turbo` | `tencent/Hunyuan3D-2mv` | 1.1B | **Step-distilled Multi-View (Front/Left/Back) -> Shape** (5 steps with `enable_flashvdm()`) |
| **Hunyuan3D-2mv** | `hunyuan3d-dit-v2-mv-fast` | `tencent/Hunyuan3D-2mv` | 1.1B | Guidance-distilled Multi-View -> Shape (halves DiT inference time) |
| **Hunyuan3D-2mv** | `hunyuan3d-dit-v2-mv` | `tencent/Hunyuan3D-2mv` | 1.1B | Full 30–50 step Multi-View -> Shape model |
| **Hunyuan3D-2** | `hunyuan3d-dit-v2-0-turbo` | `tencent/Hunyuan3D-2` | 1.1B | Step-distilled single-image -> Shape |
| **Hunyuan3D-2** | `hunyuan3d-paint-v2-0-turbo` | `tencent/Hunyuan3D-2` | 1.3B | **Distilled Multi-View Texture Synthesizer** (accepts `[front, left, back]` list!) |
| **Hunyuan3D-2** | `hunyuan3d-delight-v2-0` | `tencent/Hunyuan3D-2` | 1.3B | **Image Delight Model** (strips baked lighting/shadows before texturing) |
| **Hunyuan3D-2mini** | `hunyuan3d-dit-v2-mini-turbo` | `tencent/Hunyuan3D-2mini` | 0.6B | Ultra-lightweight fast shape generator (`faster_shape_gen_with_flashvdm_mini_turbo.py`) |

> **Selective Subfolder Download Tip:** Because `tencent/Hunyuan3D-2mv` and `tencent/Hunyuan3D-2` bundle multiple variants in one repo, pass `allow_patterns=["hunyuan3d-dit-v2-mv-turbo/*"]` to `snapshot_download` when downloading a specific subfolder.

### 2. VAST-AI TripoSG Series (`D:\AI_Models\3d-models\TripoSG`)

| Model | Repo ID | Params | Purpose & Key README Features |
|---|---|---|---|
| **TripoSG** | `VAST-AI/TripoSG` (+ `briaai/RMBG-1.4`) | 1.5B | Rectified Flow Transformer + SDF VAE trained on 2M Image-SDF pairs with surface normal + eikonal loss. Supports direct face budget via `--faces 15000`. |
| **TripoSG-scribble** | `VAST-AI/TripoSG-scribble` | 512-tok | CFG-distilled fast shape prototyping from rough sketch + text prompt (`--scribble-conf 0.3`). |

### 3. Microsoft TRELLIS Series (`D:\AI_Models\3d-models\TRELLIS`)

| Model | Repo ID | Params | Purpose & Key README Features |
|---|---|---|---|
| **TRELLIS-image-large** | `microsoft/TRELLIS-image-large` | 1.2B | **Recommended:** Unified Structured LATent (`SLAT`) model (all VAEs included in repo). Decodes simultaneously to **Mesh** (FlexiCubes), **3D Gaussians**, and **Radiance Fields**. |
| **TRELLIS-text-large / xlarge** | `microsoft/TRELLIS-text-xlarge` | 1.1B–2.0B | Text-to-3D SLAT models. *README note: Always prefer generating a 2D image first and feeding `TRELLIS-image-large` for superior detail.* |

---

## Stage 1: Concept -> High-Poly Render -> Orthographic Turnaround Sheet

Always lock the visual design in three steps using `invoke_subagent` with `image-generator`:

1. **2D Concept Sketch Prop Sheet** (optional if concept already exists):
   - Dark atmospheric concept sheet with silhouette and construction callouts.
2. **Photorealistic Blender Cycles High-Poly Hero Render**:
   - Pass the 2D sketch as reference and prompt for a single clean AAA 3D asset hero render in Blender Cycles against a neutral dark studio grey backdrop (`#2a2a2e`), with micro-displacement, specular/roughness contrast, and zero 2D text overlays.
3. **Orthographic 3D Turnaround Sheet (Front / Side / Back)**:
   - Pass the approved High-Poly Hero Render as reference and prompt:
     > *"Using `<hero_render_path>` as the exact visual reference, generate a clean orthographic 3D turnaround reference sheet showing three full orthographic views side by side (Front view / Side profile view / Back view). Requirements: Perfectly aligned horizontal height lines across Front, Side, and Back orthographic views; even studio lighting on a solid neutral grey background (`#2a2a2e`), zero floor puddles or detached shadows, NO text labels, NO arrows, NO borders."*

---

## Stage 2: Neural 3D Mesh Reconstruction (`Hunyuan3D-2mv`, `Hunyuan3D-2.1`, `TripoSG`, or `TRELLIS`)

### Critical Cropping Rule for Multi-View: Square-Pad Wide Views!
Multi-view models resize each view (`front`, `left`, `back`) to `518x518`. For wide props (like a bathtub, bed, or desk), **always pad each cropped view to a square canvas matching the background color** (`crop_padded_square`) before passing to `Hunyuan3D-2mv` so the 3D aspect ratio is preserved.

### Critical Bug in Upstream `Hunyuan3D-2/examples/*.py` to Avoid
In Tencent's `examples/fast_shape_gen_multiview.py` and `shape_gen_v2_1.py`, the demo code calls `image = Image.open(p).convert("RGBA")` and *then* checks `if image.mode == 'RGB': image = rembg(image)`. Because `image.mode` is already `'RGBA'` with a solid `255` alpha channel, background removal is silently skipped! **Always check `if image.mode == 'RGB':` BEFORE `.convert("RGBA")`, or call `rembg(image)` unconditionally.**

### Option A: `Hunyuan3D-2mv` Multi-View + `FlashVDM` (`examples/fast_shape_gen_multiview.py`)
```python
import os, sys, torch
os.environ["HF_HOME"] = r"D:\AI_Models\huggingface"
sys.path.insert(0, r"D:\AI_Models\3d-models\Hunyuan3D-2")
from PIL import Image
from hy3dgen.rembg import BackgroundRemover
from hy3dgen.shapegen import Hunyuan3DDiTFlowMatchingPipeline

rembg = BackgroundRemover()
images = {}
for key, path in [("front", front_sq_path), ("left", side_sq_path), ("back", back_sq_path)]:
    img = Image.open(path)
    img = rembg(img)  # Always strip background so alpha mask is clean!
    images[key] = img

pipeline = Hunyuan3DDiTFlowMatchingPipeline.from_pretrained(
    "tencent/Hunyuan3D-2mv",
    subfolder="hunyuan3d-dit-v2-mv-turbo",
    variant="fp16",
)
pipeline.enable_flashvdm()  # Lightning vecset decoder (~5 steps!)
mesh = pipeline(
    image=images,
    num_inference_steps=5,
    octree_resolution=380,
    num_chunks=20000,
    generator=torch.manual_seed(12345),
    output_type="trimesh",
)[0]
mesh.export(output_glb_path)
```
- **Optional Multi-View Neural Texturing (`examples/fast_texture_gen_multiview.py`):**
  *(Requires compiling `hy3dgen/texgen/custom_rasterizer` and `differentiable_renderer` once via `python setup.py install`)*
  ```python
  del pipeline
  torch.cuda.empty_cache()
  from hy3dgen.texgen import Hunyuan3DPaintPipeline
  paint_pipe = Hunyuan3DPaintPipeline.from_pretrained(
      "tencent/Hunyuan3D-2",
      subfolder="hunyuan3d-paint-v2-0-turbo",
  )
  textured_mesh = paint_pipe(mesh, image=[images["front"], images["left"], images["back"]])
  textured_mesh.export(output_textured_glb_path)
  ```
- **Local API Server & Blender Addon (`Hunyuan3D-2`):**
  You can also launch `python api_server.py --host 0.0.0.0 --port 8080` in `D:\AI_Models\3d-models\Hunyuan3D-2` and connect `blender_addon.py` directly inside Blender.

### Option B: `VAST-AI/TripoSG` (Single Hero Image -> 15K-Face SDF Mesh)
`TripoSG` supports a native `--faces` decimation target right in its inference CLI/script:
```powershell
# CLI invocation from D:\AI_Models\3d-models\TripoSG:
python -m scripts.inference_triposg --image-input "path/to/hero_render.png" --faces 15000 --output-path "path/to/output.glb"
```
Or in Python (for rough sketch + prompt prototyping, use `scripts.inference_triposg_scribble` with `--scribble-conf 0.3`).

### Option C: `microsoft/TRELLIS-image-large` (Single or Multi-Image -> SLAT Mesh + Gaussian Texture Bake)
```python
import os, sys
os.environ["HF_HOME"] = r"D:\AI_Models\huggingface"
os.environ["ATTN_BACKEND"] = "xformers"  # Use 'xformers' on Windows if flash-attn is not compiled
os.environ["SPCONV_ALGO"] = "native"     # 'native' avoids initial benchmarking overhead on single runs
sys.path.insert(0, r"D:\AI_Models\3d-models\TRELLIS")

from PIL import Image
from trellis.pipelines import TrellisImageTo3DPipeline
from trellis.utils import postprocessing_utils

pipeline = TrellisImageTo3DPipeline.from_pretrained("microsoft/TRELLIS-image-large")
pipeline.cuda()

outputs = pipeline.run(
    Image.open(hero_image_path),
    seed=1,
    sparse_structure_sampler_params={"steps": 12, "cfg_strength": 7.5},
    slat_sampler_params={"steps": 12, "cfg_strength": 3.0},
)
# Bake the 3D Gaussian appearance directly onto the simplified FlexiCubes mesh:
glb = postprocessing_utils.to_glb(
    outputs["gaussian"][0],
    outputs["mesh"][0],
    simplify=0.95,
    texture_size=2048,
)
glb.export(output_glb_path)
```

---

## Stage 3: Local Neural Segmentation (`rembg`) + Isotropic Diffusion + PBR Maps

When using multi-view orthographic projection in Blender (or when custom wet-blood / coarse-fabric PBR roughness & normal maps are needed), never project raw turnaround slices directly onto a 3D mesh without stripping the background—studio floor shadows and dark anti-aliased edge pixels project dark bands onto grazing silhouette triangles, and naive 4-neighbor dilation causes vertical/horizontal pixel streaks.

Run this local script via `uv run --with "rembg[cpu]" --with numpy --with pillow`:
```python
import numpy as np
from PIL import Image, ImageFilter
from rembg import remove

def process_slice_smooth(img, x0, x1, y0, y1, out_prefix):
    crop = img.crop((x0, y0, x1, y1)).convert("RGB")
    rgba = remove(crop)
    arr_rgba = np.asarray(rgba, dtype=np.float32)
    alpha = arr_rgba[:, :, 3] > 140

    # Tight bounding box around the exact neural foreground mask
    rows = np.where(np.any(alpha, axis=1))[0]
    cols = np.where(np.any(alpha, axis=0))[0]
    r0, r1, c0, c1 = int(rows[0]), int(rows[-1]), int(cols[0]), int(cols[-1])

    tight_rgb = np.asarray(crop, dtype=np.float32)[r0:r1+1, c0:c1+1, :]
    tight_alpha = alpha[r0:r1+1, c0:c1+1]

    # Erode mask by 3px so zero grey background fringe survives
    a_pil = Image.fromarray((tight_alpha * 255).astype(np.uint8))
    eroded = np.asarray(a_pil.filter(ImageFilter.MinFilter(7))) > 128

    # 1. Row-wise median fallback
    filled = tight_rgb.copy()
    global_med = np.median(tight_rgb[eroded], axis=0)
    for r in range(filled.shape[0]):
        rv = eroded[r]
        filled[r, ~rv] = np.median(tight_rgb[r, rv], axis=0) if np.any(rv) else global_med

    # 2. 8-neighbor isotropic dilation (28px margin) + Gaussian exterior feathering
    dil = tight_rgb.copy()
    v_mask = eroded.copy()
    for _ in range(28):
        if np.all(v_mask): break
        acc_rgb = np.zeros_like(dil)
        acc_w = np.zeros(dil.shape[:2], dtype=np.float32)
        for dy, dx, w in [(-1,0,1.0),(1,0,1.0),(0,-1,1.0),(0,1,1.0),(-1,-1,0.7),(1,-1,0.7),(-1,1,0.7),(1,1,0.7)]:
            s_rgb = np.roll(np.roll(dil, dy, axis=0), dx, axis=1)
            s_v = np.roll(np.roll(v_mask, dy, axis=0), dx, axis=1).astype(np.float32)
            if dy == -1: s_v[-1, :] = 0
            if dy ==  1: s_v[0, :]  = 0
            if dx == -1: s_v[:, -1] = 0
            if dx ==  1: s_v[:, 0]  = 0
            acc_rgb += s_rgb * (s_v * w)[..., None]
            acc_w += s_v * w
        can = (~v_mask) & (acc_w > 0)
        if not np.any(can): break
        dil[can] = acc_rgb[can] / acc_w[can][..., None]
        v_mask[can] = True

    dil[~v_mask] = filled[~v_mask]
    ext_blur = np.asarray(Image.fromarray(np.clip(dil, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(radius=6)), dtype=np.float32)
    final_rgb = np.where(eroded[..., None], tight_rgb, ext_blur)
    Image.fromarray(np.clip(final_rgb, 0, 255).astype(np.uint8)).save(f"{out_prefix}_albedo.png")

    # 3. Multi-band Height Map + Sobel OpenGL Normal Map + Wet/Dry Roughness Map
    arr = np.clip(final_rgb, 0, 255) / 255.0
    gray = 0.299 * arr[:, :, 0] + 0.587 * arr[:, :, 1] + 0.114 * arr[:, :, 2]
    gray_img = Image.fromarray((gray * 255).astype(np.uint8))
    blur_med = np.asarray(gray_img.filter(ImageFilter.GaussianBlur(radius=3)), dtype=np.float32) / 255.0
    blur_fine = np.asarray(gray_img.filter(ImageFilter.GaussianBlur(radius=1)), dtype=np.float32) / 255.0
    height = np.clip(0.45 * gray + 0.35 * (gray - blur_med + 0.5) + 0.20 * (gray - blur_fine + 0.5), 0.0, 1.0)
    Image.fromarray((height * 255).astype(np.uint8)).save(f"{out_prefix}_height.png")

    strength = 4.2
    dzdx, dzdy = np.zeros_like(height), np.zeros_like(height)
    dzdx[:, 1:-1] = (height[:, 2:] - height[:, :-2]) * 0.5 * strength
    dzdy[1:-1, :] = (height[2:, :] - height[:-2, :]) * 0.5 * strength
    nx, ny, nz = -dzdx, dzdy, np.ones_like(height)
    norm = np.sqrt(nx * nx + ny * ny + nz * nz)
    nrm_rgb = np.stack([(nx/norm * 0.5 + 0.5)*255, (ny/norm * 0.5 + 0.5)*255, (nz/norm * 0.5 + 0.5)*255], axis=-1).astype(np.uint8)
    Image.fromarray(nrm_rgb).save(f"{out_prefix}_normal.png")

    r, g, b = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2]
    bloodness = np.clip((r - g * 1.15) * 3.5, 0.0, 1.0)
    dark_wet = np.clip((0.35 - gray) * 2.5, 0.0, 1.0) * np.clip((r - b) * 2.5 + 0.3, 0.0, 1.0)
    wet_mask = np.maximum(bloodness * 0.85, dark_wet * 0.65)
    roughness = np.clip(0.82 - wet_mask * 0.62 - (gray - 0.5) * 0.12, 0.12, 0.94)
    Image.fromarray((roughness * 255).astype(np.uint8)).save(f"{out_prefix}_roughness.png")
```

---

## Stage 4: Local Blender 5.2 GPU Tri-Planar Projection, 15K Decimation, Atlas Baking & Unity Rigging

1. **Coordinate System Gotcha (`glTF` Import in Blender):**
   - `bpy.ops.import_scene.gltf` automatically adds a +90° X rotation to convert glTF `Y-up` to Blender `Z-up`.
   - **NEVER call `bpy.ops.object.transform_apply(rotation=True)` if you also manually rotate the mesh**, or you will double-rotate it onto its stomach! Only apply `location=True` and `scale=True` after centering the world bounding box.
2. **Decimate to ~15,000 Triangles Before UV Unwrapping & Baking:**
   - Apply a `DECIMATE` modifier (`ratio = 15000 / len(obj.data.polygons)`) so the runtime mesh is lightweight (`15,000` triangles) while the `2048x2048` normal/albedo/roughness bake captures all surface detail directly onto the 15K-triangle `Smart UV Project` atlas (`UVMap`).
3. **Tri-Planar Normal-Blended Shader (`UV_Front`, `UV_Back`, `UV_Side`):**
   - Create `UV_Front` `(tx, tz)`, `UV_Back` `(1 - tx, tz)`, and `UV_Side` `(ty, tz)` from normalized vertex coordinates (`[0.004..0.996]`).
   - Blend Front vs. Back with `Smoothstep(object_normal.y, -0.22, +0.22)` and blend in Side only on steep lateral faces with `Smoothstep(abs(object_normal.x), 0.72, 0.92)`.
   - Bake `EMIT` (for Base Color and Roughness) and `NORMAL` (Tangent space) into `2048x2048` PNG textures using Cycles GPU (`OPTIX`/`CUDA`), then replace the material with a clean single-UV `Principled BSDF` referencing the baked textures.
4. **Skeletal Rigging & Critical Unity FBX Scale Rule (`FBX_SCALE_ALL`):**
   - Build an `Armature` named `Joints` with smooth height/region vertex weights, parent the mesh to `Joints`, and export with:
     ```python
     bpy.ops.export_scene.fbx(
         filepath=fbx_out,
         use_selection=False,
         object_types={'ARMATURE', 'MESH'},
         apply_scale_options='FBX_SCALE_ALL',  # CRITICAL: prevents Joints.localScale=(100,100,100) in Unity!
         use_space_transform=True,
         bake_space_transform=False,
         axis_forward='-Z',
         axis_up='Y',
         add_leaf_bones=False,
         bake_anim=False,
     )
     ```
5. **Visual Verification in Blender & Unity (`view_file`):**
   - Always inspect renders with `view_file`. When rendering a posed `SkinnedMeshRenderer` offscreen in Unity Edit mode via `Camera.Render()`, bake the posed skin into a temporary `MeshFilter`/`MeshRenderer` with `smr.BakeMesh(baked)` first so Unity draws the deformed pose cleanly.

---

## Stage 5: Humanoid Characters — 2-Pass HD Head + Hybrid Multi-Part Architecture

When building a humanoid character seen up close (e.g., shopkeepers, NPCs, bosses), **NEVER rely on a single full-body neural 3D pass**. A single full-body pass causes two fatal flaws:
1. **"Printed Mask" Low-Res Face**: In a `1K` full-body turnaround of a `2.0 m` character, the head occupies only `~110×157 px` (~6% of image height and ~6% of a shared UV map).
2. **"Shrink-Wrapped" Garments & Pocket Props**: Image-to-3D neural models (`Hunyuan3D`, `TripoSG`, `TRELLIS`) always output a **single watertight outer shell**, fusing aprons, pockets, straps, dogtags, tools, and shards into the body like a printed plastic toy.

Always follow these **4 mandatory rules** for humanoid characters on the very first pass:

### Rule 1: Two-Pass Neural Reconstruction (Full Body + Dedicated Close-Up HD Head)
1. **Pass A — Full-Body Turnaround $\rightarrow$ `Body` Sub-Mesh (`4096×4096` UV Map):**
   - Reconstruct the full-body `.glb` for the body, limbs, and base garment (`<Character>_Body`).
2. **Pass B — Full-Frame `1K` Close-Up Head & Neck Turnaround $\rightarrow$ `Head` Sub-Mesh (`2048×2048` UV Map):**
   - Generate a dedicated **full-frame `1K` Close-Up Orthographic Head & Neck Turnaround Sheet** (`Front`, `Left Profile`, `Back`) using `image-generator` with the character's hero render as reference (giving the face **`27×` more source pixels**).
   - Run `Hunyuan3D-2mv` (`octree_resolution=384`) on the close-up head views to produce a `~700K`-triangle 3D head sculpt with true 3D brow ridges, upper/lower eyelids, convex eyeballs, hooked nose/nostrils, cheekbones, lips, ears, and neck tendons.
   - Decimate `<Character>_Head` alone to **`25,000–28,000` triangles** and give it its own dedicated `2048×2048` `UVMap` (`<Character>_Head_Diffuse.png` + `<Character>_Head_Normal.png`), providing **`16×` higher UV texel density** than sharing a UV map with the body.

### Rule 2: Hybrid Multi-Part Sub-Meshes for Layered Garments & Props
- **Smooth the Base Body Under Layered Garments**: Before placing separate garments, smooth away any fused lumps on `<Character>_Body` in the garment zone (`0.54 <= z <= 1.575`) and build a `BVHTree` (`bvh_body`) from the cleaned body mesh.
- **Construct Separate 3D Sub-Meshes Raycast Against `bvh_body`**:
  - `<Character>_Apron` / Outer Garment: Real `4.5 mm` `SOLIDIFY` thickness floating `8–9 mm` in front of `bvh_body`, with separate **open-top 3D pocket cavities** (`32–44 mm` flare depth), 3D neck straps (`min()` across strap width so the chest never pokes through), and 360° 3D waist belts. Erase the apron region from `<Character>_Body`'s baked diffuse/normal texture so the apron is never "double-printed" onto the underlying smock.
  - `<Character>_Items`: Separate 3D hard-surface meshes (stamped dogtags with beveled edges + taut V-cord, surgical scissors, copper-top batteries, fuse tubes, hanging keys) placed inside the 3D pocket cavities and assigned a metallic PBR material (`Metallic = 0.72`, `Smoothness = 0.68`).
  - `<Character>_Shards` / Emissives: Separate faceted 3D crystal meshes assigned an HDR emissive URP material (`_EMISSION`).

### Rule 3: Never "Fix" Faces with Blind Math Formulas or Guessed Eyeball Spheres
- **Never apply hardcoded `sin/cos` vertex-pushing formulas** to approximate facial features, and **never spawn separate UV-sphere eyeballs at guessed world coordinates**—even a `5 mm` Y/Z mismatch causes spheres to poke through the lower eyelids ("four-eyes" double-pupil artifact).
- Let the close-up HD `Hunyuan3D-2mv` pass sculpt the real 3D eyelids and eyeballs. If adding a subtle `2 mm` corneal dome and palpebral crease, read the exact normalized `(tx, tz)` coordinates from `UV_Front` (`ecx = 0.360, 0.640`, `ecz = 0.671`) before world-space placement.

### Rule 4: Facial Baking & Neck-to-Collar Seam Rules
- **NEVER use `ShaderNodeBrightContrast` with negative brightness on linear baked albedo**: Subtracting brightness in Blender's linear color space crushes dark eye rings and sockets to pure black (`RGB = 0`). Bake the delighted/raw albedo directly.
- **Gate `UV_Side` on the Head to Outer Temples & Ears Only (`|tx - 0.5| > 0.31` and `|Normal.X| > 0.76`)**: Prevents side-view projection from smearing across the outer corners of the eye sockets.
- **Cut Neck/Collar Seams with `bmesh.ops.bisect_plane`, Never Vertex Deletion**:
  - Deleting vertices (`if v.co.z < cutoff`) leaves stair-stepped sawtooth triangle teeth.
  - Always bisect `<Character>_Body`'s old neck strictly **above** the shoulder/trapezius arch (`z = 1.654 m`) and feather the narrow neck stub (`|x| < 0.062 m`) inward with smooth `X × Z` falloff.
  - Taper `<Character>_Head`'s lower neck (`wz < 1.685 m`) into a sleek cylinder (`rx = 0.044 m -> 0.037 m`), bisect its bottom cleanly with `bisect_plane` (`z = 1.600 m`), and tuck it **inside** the closed collar opening (`uv.y` on the collar rim clamped to clean cloth `<= 0.775` so neck shadow never bleeds onto the collar).

