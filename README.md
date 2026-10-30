# GalacticDisk

Adds galactic disks to KSP from just configs!

Supports the original particle renderer and an optional volumetric renderer with dust clouds,
star-forming nebulae, a central bar, and mesh-based stars. Existing configs default to particles.

## Dependencies
Nothing except for maybe ModuleManager but everyone has it anyway...

## Adding disks
See `GalacticDisk/ExampleConfig.txt` for the original renderer and
`GalacticDisk/ExampleVolumetricConfig.txt` for a Milky Way-style volumetric disk.
Rename the desired example to `.cfg` to enable it. Multiple definitions can intentionally
layer disks around one body; the toolbar edits the last definition for that body.

See [the volumetric guide](GalacticDisk/Volumetric.md) for installation, controls,
all new parameters, platform bundles, building, and verification.

`GalacticDiskRingDefinition` adds a single-color ring with an optional central opening,
particle or volumetric rendering, and dust. See [the ring guide](GalacticDisk/Rings.md)
and `GalacticDisk/ExampleRingConfig.txt` for the config and renamed parameters.

## Downloads
SpaceDock: https://spacedock.info/mod/4384/GalacticDisk <br>
CKAN: soon <br>
or just download ZIP from GitHub and move the GalacticDisk folder inside the GalacticDisk-main repo folder to GameData.
