# GalacticDisk volumetric renderer

GalacticDisk now supports two renderers through the same `GalacticDiskDefinition` node and in-game toolbar:

- `renderMode = Particles`: the original Unity ParticleSystem renderer. This remains the default when `renderMode` is absent.
- `renderMode = Volumetric`: a ray-marched galaxy/dust/nebula volume plus generated mesh star billboards. It creates no ParticleSystem or ParticleSystemRenderer, and `starCount` is still the exact count of individual stars.

Both attach at the chosen body's scaled-space origin, use layer 10, and share object/render scale, manual rotation, orbital alignment, and simulated-time spin. The root controller handles renderer replacement, resource cleanup, and recovery after a renderer or scaled-space parent is destroyed.

## Install and enable

Keep the new `GalacticDisk.dll` **and** `Assets/galacticdisk-windows.bundle` together in `GameData/GalacticDisk`. The existing Icons folder remains required for the toolbar icon. The Unity scene/project is not installed into KSP.

The supplied shader bundle targets **Windows**, built with **Unity 2019.4.18f1**. Linux and macOS require their matching bundles; see Building below. Legacy particle mode does not require a bundle.

For the Milky Way appearance, rename `ExampleVolumetricConfig.txt` to `.cfg` and adjust `planet`. Multiple definitions may intentionally layer disks around one body, including a mix of particle and volumetric modes. The toolbar edits the last definition for that body. Avoid enabling both examples unless you want both layers.

Alternatively, in the existing toolbar:

1. Select the celestial body and click **Load** if it already has a disk.
2. Choose **Volumetric** at the top of the window.
3. Optionally click **Load Milky Way preset into fields**. It fills the appearance controls, preserving your current scale/rotation settings.
4. Expand **Volume, dust, nebulae and bar** to change the new controls. Common star, arm, bulge, color, rotation, and scale fields remain in their existing sections.
5. Click **Apply** to create or replace the body's disk. A failed build leaves its previous renderer intact and shows the error in the window.
6. Edit the always-visible config text directly, or click **Save to config** to copy field changes into it. Errors, warnings, and field/config differences appear above the text. Save the generated text as a `.cfg` manually for future sessions. Applying settings does not write GameData files. See [the config editor guide](ConfigEditor.md).

New disks still need a useful `objectScale`; the old default of 1 is usually too small to see outside a celestial body. The example uses `objectScale = 1.5e15` and `renderScale = 1e8`; choose scales appropriate to your planetary system.

## Parameters

All original parameters remain supported. The common settings control both rendering modes. Existing configs and obsolete `color1`/`color2` aliases still load, and RGB values default alpha to 1. New color fields also accept either RGB or RGBA.

### Milky Way structure

| Parameter | Meaning |
| --- | --- |
| `logarithmicArms` | Enables logarithmic arms. False uses the original linear-radius winding law. |
| `armPitchAngle` | Logarithmic pitch in degrees. Higher opens the spiral. |
| `armTightness` | Winding multiplier in logarithmic mode; full turns from center to rim in linear mode. |
| `armStartRadius` | Inner arm radius as a fraction of galaxyRadius. |
| `secondaryArmStrength` | Strength of alternate stellar arms. Default 0.45 gives two stronger stellar arms among four gas/nebula arms. |
| `armIrregularity` | Small radial variations in the arm winding. |
| `enableBar`, `barFraction` | Enable the central bar and assign a fraction of bulge stars to it. Does not add to starCount. |
| `barLength`, `barWidth`, `barHeight`, `barAngle` | Bar dimensions and XZ orientation. Gaussian standard deviations are length × 0.25, width × 0.35, height × 0.35. |

### Dust

| Parameter | Meaning |
| --- | --- |
| `enableDustClouds` | Enables/disables the dust density field, including continuous lanes. |
| `dustCloudCount` | Number of irregular cloud complexes. Zero removes discrete clouds while lanes can remain. |
| `minDustCloudSize`, `maxDustCloudSize` | Horizontal cloud size range in unscaled galaxy units. |
| `dustCloudThickness` | Vertical/horizontal radius ratio. |
| `dustCloudClumpiness` | Fragmentation and holes; 0 is smooth, 1 strongly broken. |
| `dustArmBias` | Fraction of clouds following the arms instead of the general disk. |
| `dustLaneStrength`, `dustLaneWidth` | Continuous lane strength and width relative to the arm spread. |
| `dustOpacity` | Absorption strength for volume light and individual mesh stars. |
| `dustColor` | RGBA dust tint. Alpha 0 makes dust transparent; RGB affects reddening/reflected light. |
| `dustScattering` | Small reflected-light contribution. Low values keep clouds dark. |
| `starDustSamples` | Sight-line samples for each star vertex. Higher improves dust occlusion at a GPU cost. Zero disables mesh-star attenuation. |

### Star formation

| Parameter | Meaning |
| --- | --- |
| `enableStarFormation` | Enables/disables nebula emission, association glow, and clustered young-star placement. |
| `starFormingRegionCount` | Number of multi-lobed nebula complexes. |
| `minStarFormingRegionSize`, `maxStarFormingRegionSize` | Region size range in unscaled galaxy units. |
| `starFormationInnerRadius`, `starFormationOuterRadius` | Region placement range as fractions of galaxyRadius. |
| `starFormationArmBias` | Fraction of nebulae following the arms. |
| `starFormingRegionClumpiness` | Internal fragmentation of nebula emission. |
| `starFormingRegionColor`, `starFormingRegionIntensity` | RGBA emission color and luminosity of the pink/red HII regions. |
| `youngStarColor`, `youngStarGlowIntensity` | RGBA color and surrounding blue association glow. |
| `youngStarFraction` | Fraction of arm stars moved into young associations. Included in starCount. |

`armHighlightColor`/`armHighlightChance` still apply to individual arm stars, separately from the extended nebulae. These are static visual regions, not a simulation of star formation over time.

### Appearance and quality

| Parameter | Meaning |
| --- | --- |
| `seed` | Repeatable star/region/cloud generation. |
| `enableVolume` | Toggles continuous light, nebulae, and reflected dust light. Mesh stars can still be drawn/attenuated. |
| `volumeIntensity` | Diffuse disk/bulge/bar luminosity. |
| `volumeExposure` | Galaxy-local exposure; no full-screen camera tonemapping is installed. |
| `volumeSteps` | Ray-march samples, 32–256. Default 192. |
| `cloudResolution` | XZ density-texture resolution, 64–256. Default 192; Y is sized automatically. |
| `noiseFrequency` | Size of diffuse clumps/filaments. |
| `starBrightness` | Individual mesh-star brightness. |

The volume and stars share a generated RGBAHalf 3D density texture. It uses about 9 MiB on the GPU at the example's 192 × 32 × 192 resolution; taller volumes need more Y slices. Mesh data and temporary CPU baking arrays require additional memory. Rebuilding regenerates these assets once; they are not rebaked every frame.

For a slower GPU, try 50,000 stars, 64–96 volume steps, 96–128 cloud resolution, and 6 star-dust samples. Star count is capped at 2,000,000; that is a safety limit, not a performance recommendation.

`usesTexture` and `texturePath` remain particle-renderer options. Volumetric mode uses its procedural field and shader-rendered mesh stars.

## Scaling and cameras

`objectScale` controls the visible size independently of the scaled body's radius. The logical root stays at the body's origin with unit local scale. Legacy particles keep `objectScale * renderScale` on the emitter itself, using Unity's Local scaling mode; their generated positions and sizes divide by `renderScale` to retain the original visible size and culling behavior.

Volumetric rendering uses camera-relative galaxy units and explicit per-camera mesh draw commands. It does not move a rendering proxy through the distant body's transform or depend on its native MeshRenderer culling bounds. Each camera keeps its own projection and depth settings, including when reflection cameras render inside another camera's frame. `renderScale` remains accepted for config compatibility and does not enlarge the visible volume. This renderer supports drawing beyond the scaled camera's far plane and requests camera depth from cameras that draw layer 10, to clip volume light at opaque scaled-space geometry. Diffuse volume light renders before transparent particles, and the galaxy's mesh stars render after them. Disabled MeshRenderer components retain the mesh/material references for inspection; the camera commands submit their geometry.

No demo orbit camera, full-screen bloom, or full-screen color correction is installed. Dust absorption and galaxy-local tone mapping happen inside the galaxy shaders.

## Building

1. Build `GalacticDiskDLL/GalacticDiskDLL.csproj` against KSP's managed assemblies. Adjust the existing HintPaths if your KSP installation differs. Debug and Release builds both copy the resulting DLL into the mod folder. Set `/p:SkipModCopy=true` to suppress that copy.
2. Open `UnityAssets` with Unity **2019.4.18f1**.
3. Run **GalacticDisk > Build Windows shaders**. This writes `GalacticDisk/Assets/galacticdisk-windows.bundle`.
4. Linux/macOS builds use the corresponding menu entries and produce `galacticdisk-linux.bundle` / `galacticdisk-macos.bundle`. Install the Unity platform support if Unity reports it is missing. Bundles are platform-specific; the Windows bundle is the only prebuilt bundle supplied here.

The plugin loads the compiled shaders from the bundle, not raw `.shader` text. Shader sources and the builder are included in the repository; only the DLL, matching bundle, normal mod assets, and configs belong in GameData.

## Verification

`Tests/Run-UnityChecks.ps1` creates an isolated Unity test project from the actual runtime sources and bundled shaders. It uses test-only KSP API substitutes to check legacy/RGB compatibility, toolbar/config round-trips, mode switching, rollback on missing assets, no particles in volume mode, size/alpha/count preservation, scaling, timewarp rotation, scene/renderer recovery, removal, far-plane rendering, and opaque geometry occlusion.

The production DLL is also compiled against the actual installed KSP assemblies. These checks do not replace a live KSP visual test across its multiple cameras or third-party visual mods. A live KSP session was not launched during this implementation.

Run the same script with `-PlayMode` for a separate test of real Unity frames, automatic camera-hook subscription, first-frame depth setup, opaque-object occlusion, and persistent particle-free rendering. KSP APIs are still substitutes in that isolated test; it does not load a KSP save.

Run with `-Scale` to reproduce `objectScale = 1.5e15` and `renderScale = 1e8`, with two body radius scales. It checks visible legacy disks, continuous light with zero mesh stars, volume rendering beyond the camera far plane, and two legacy layers remaining visible alongside a volume.

Run with `-CameraFrames` to check repeated rendered frames with rotating, translated body transforms and multiple cameras. It covers scaled-space distances corresponding to `2.57e20` and `2.08e24` meters and a camera near the floating origin. The latter reproduces the culling failure caused by trying to position a child proxy at the camera through an astronomical parent transform.
