# Rotation and the editable config window

Both galaxy and ring definitions accept `spinAroundDiskAxis`, which defaults to `false` to preserve existing configs. Set it to `true` to apply the time-based spin from `rotationPeriod` around the disk's own tilted axis. Its tilt stays fixed as it spins. `rotationY` remains a heading around world Y, rather than becoming the time-based local spin angle. A zero rotation period still disables time-based spin.

```cfg
rotationX = 35
rotationY = 17
rotationZ = 50
rotationPeriod = 1000
spinAroundDiskAxis = true
```

If `alignRotationToOrbit` is enabled, the orbital plane supplies the tilt instead of `rotationX/Z`. With the new flag enabled, `rotationY` then rotates that aligned frame around world Y, and the time-based spin follows the resulting disk axis. With the flag disabled, the previous orbital-alignment behavior remains unchanged.

The config text is always present in the toolbar window and can be edited directly:

- Valid text edits update the fields above, including the selected body, definition type, render mode, colors, rotation and volume options. Comments and formatting remain in the text until it is regenerated.
- Incomplete or invalid text remains editable and leaves the last valid field values in place. Errors above the text identify syntax, invalid value types, unknown bodies, invalid radii/sizes/counts, and invalid scale products.
- Warnings identify unknown keys, deprecated aliases, ignored settings, values that will be clamped, and each field/config mismatch. Mismatch messages show the parameter name and both values. Equivalent numbers and RGB/RGBA colors with alpha 1 count as matches.
- **Save to config** copies valid field values into the text. It replaces the old Show config button. Invalid fields prevent replacement, preserving the draft. Regeneration removes comments and unknown keys and writes current parameter names.
- **Load** fills both the fields and text from the selected body's applied disk.
- **Apply** rebuilds the selected disk from the fields. Invalid fields or text block Apply. Valid field/text mismatches remain warnings, so field changes can be applied before regenerating the text. Applying never erases the draft.

These actions do not write GameData files. Save the resulting text to a `.cfg` manually to retain it for later game sessions. Editing text or saving fields to text does not rebuild the disk until Apply is clicked.

The editor accepts one complete `GalacticDiskDefinition` or `GalacticDiskRingDefinition` node, with `//` comments. Duplicate keys, nested nodes and multiple definitions are rejected in this single-disk editor. Existing game config loading keeps its compatibility behavior.

Run `Tests/Run-UnityChecks.ps1 -Editor` to test both spin behaviors, world-Y heading, galaxy/ring schemas, text synchronization, validation, mismatches and Save/Load/Apply preservation in Unity. The isolated harness uses KSP API substitutes.
