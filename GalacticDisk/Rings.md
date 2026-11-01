# Ring definitions

`GalacticDiskRingDefinition` creates a circular disk with an optional central opening and one RGBA color. It supports the same `Particles` and `Volumetric` modes as galaxy definitions. Use `ExampleRingConfig.txt` as a starting point; rename it to `.cfg` and set `planet` to enable it.

`innerRingRadius` is the only new appearance parameter. It defaults to 0 (a full disk), must be nonnegative, and must be smaller than `ringRadius`. Particle centers, diffuse fog and dust stay within the annulus. Finite-sized particle billboards can overlap its boundary slightly.

The other ring-specific names map to existing galaxy controls:

| Ring key | Galaxy equivalent |
| --- | --- |
| `ringRadius` | `galaxyRadius` (outer radius) |
| `ringThickness` | `diskThickness` (vertical spread) |
| `particleCount` | `starCount` |
| `minParticleSize`, `maxParticleSize` | `minStarSize`, `maxStarSize` |
| `particleBrightness` | `starBrightness` |
| `color` | One color for all particles and diffuse fog |

`color` and `dustColor` accept RGB or RGBA. Omitted alpha defaults to 1. Rings do not add random particle color variations, spiral arms, a bulge, a bar, highlights, or star-forming regions.

`diskScaleLengthFraction` controls exponential radial falloff across the ring, measured using the outer radius. Particle placement uses the distribution restricted to the inner and outer radii. `seed` makes placement repeatable in both render modes. `ringThickness` has the same meaning as the galaxy setting: the particle height standard deviation is thickness × 0.22, rather than a hard vertical cutoff.

In particle mode, `particleCount`, sizes, brightness, color and seed drive the existing ParticleSystem renderer. Optional `usesTexture` and `texturePath` are supported. Fog and dust controls apply in volumetric mode.

In volumetric mode, generated mesh billboards replace Unity particles. `enableVolume = true` adds diffuse fog throughout the annulus, even with `particleCount = 0`. `enableDustClouds`, `dustCloudCount`, cloud sizes/thickness/clumpiness, `dustOpacity`, `dustColor`, and `dustScattering` use the existing dust renderer. Cloud centers are distributed around the ring; dust is clipped at its inner and outer radii. `enableVolume = false` leaves the mesh particles and their dust attenuation visible.

The existing `volumeIntensity`, `volumeExposure`, `volumeSteps`, `cloudResolution`, `noiseFrequency`, and `starDustSamples` controls retain their meanings. `particleBrightness` affects individual particles in both modes. Dust keeps its separate configured tint. The DLL and matching shader bundle must be installed together.

`objectScale`, `renderScale`, `rotationX/Y/Z`, `alignRotationToOrbit`, and `rotationPeriod` work as they do for galaxy disks. Volumetric rings use the same per-camera drawing method that avoids distant-body proxy culling.

The toolbar has **Galaxy / Ring** and **Particles / Volumetric** selectors. Loading, applying, and exporting a ring preserves its node type and renamed fields. Galaxy-only controls are hidden for rings. The [config editor guide](ConfigEditor.md) covers direct text editing, validation, mismatches and `spinAroundDiskAxis`.

Galaxy and ring definitions can coexist on the same body. Galaxy nodes load first, followed by ring nodes; the toolbar edits the last loaded definition for that body, following the existing layered-disk behavior. Applying toolbar changes does not write config files.

Verification: `Tests/Run-UnityChecks.ps1 -Rings` checks config aliases, exact colors, annular particle placement, thin and invalid radii, round-trips, coexistence, and rendered fog/dust/transparent/full-disk cases. `-RingPlayMode` checks the ring in actual Unity frames, camera depth, floating-origin rendering and cleanup. These isolated tests use KSP API substitutes; they do not load a KSP save.
