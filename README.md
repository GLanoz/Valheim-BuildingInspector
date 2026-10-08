# Building Inspector 0.3.1

A BepInEx plugin for Valheim that adds six free flags to **Hammer → Inspection**: Problem (red), Review (yellow), Verified (green), Other (blue), Structural (purple), and Finish (white). Customize each category's name, description, and color in the configuration. The mod does not scan or analyze buildings automatically.

## Flags and Editor

- Flags align to the aimed surface, including walls and sloped roofs. The attachment point stays fixed while the pole and textured, slightly translucent cloth rotate together. Cloth is visible from both sides.
- Use the regular Hammer to place or dismantle flags. Players can pass through them and cannot stand on them.
- Flags follow Valheim's building support system and collapse when unsupported. They do not support other pieces.
- The six standard flags block one another within the configured spacing: **0.2 m by default**, adjustable from 0.05 to 1 m. The optional test flag is excluded from this rule.
- Aim at a placed flag and press `N` to edit its note and category in a Valheim-style editor. Only the **character who placed it** can edit it; another character on the same account does not gain permission.
- Category buttons highlight on hover, including the selected category. The selection remains marked in gold and changes only when clicked. **Save** applies changes; **Cancel** or `Esc` discards them. Movement and attacks are blocked while editing.
- Notes allow **160 characters excluding line breaks**, with input limited to **five visible lines**, including wrapped lines. Nonempty notes appear when hovering over a flag.
- Placed flags, notes, category changes, and creator names are saved with the world. Older prefab IDs remain supported without recreating flags or clearing their saved data.
- **Inspected by** shows the placing character's saved name in worlds opened through **Start Server**. Once marked as multiplayer, the world keeps displaying it in later local sessions. Fresh local-only worlds hide it, while still saving the author. If an older flag's author cannot be resolved, the display falls back to `Odin`.

### Controls

| Action | Default control |
|---|---|
| Rotate the placement preview around the surface normal | Mouse wheel |
| Tilt forward / backward | `I` / `K` |
| Tilt sideways | `J` / `L` |
| Reset tilt | `Insert` |
| Open a placed flag's editor | `N` |
| Close the editor without saving | `Esc` |

Tilt defaults to 15° per press, with a maximum of 60° in either direction. Resetting tilt does not reset mouse-wheel rotation.

## Configuration

Settings are in `BepInEx/config/Lanoz.BuildingInspector.cfg` in the active profile:

- `[Flag Rotation]`: tilt/reset bindings, tilt step, and maximum angle.
- `[Flag Notes]`: editor key binding.
- `[Flag Placement]`: `BlockRadius` for standard-flag spacing.
- `[Flag Labels]`: `Label1`–`Label6` names, descriptions, and hex colors, in the category order listed above.

Restart the game after changing category labels, colors, spacing, or test-flag settings.

### Debug and Test Flag

`[Debug]` is the final section of the generated configuration:

```makefile
[Debug]
Enabled = false
TestFlagEnabled = false
TestFlagName = Configuration Test
TestFlagDescription = Temporary flag for testing label settings.
TestFlagColor = #FF00FF
```

`TestFlagEnabled` independently controls whether the optional test flag is registered in **Hammer → Inspection**. It is disabled by default and intended only for development/configuration testing. `TestFlagName`, `TestFlagDescription`, and `TestFlagColor` configure it without changing the six standard flags. Remove placed test flags before disabling it, since its prefab is not registered while disabled.

Existing names, descriptions, and colors from the earlier test-flag configuration migrate automatically unless the new settings already exist. The old enable switch is removed; use `TestFlagEnabled` to enable the test flag again.

`Enabled` controls diagnostic logging independently of the test flag. When enabled, editor and network events appear as `[Debug]` messages in `BepInEx/LogOutput.log`, without note contents or player names. Changes through a configuration manager apply immediately; edits to the file require a configuration reload or game restart.

## Requirements

- Valheim for Windows.
- An active Thunderstore/r2modman profile with BepInExPack Valheim and Jötunn; the project targets Jötunn 2.30.2.
- For building: a .NET SDK capable of targeting .NET Framework 4.7.2 (`net472`).
- NuGet packages `BepInEx.Core 5.4.21` and `JotunnLib 2.30.2`, restored from the sources configured in `NuGet.Config`.
- The project also restores `Microsoft.NETFramework.ReferenceAssemblies 1.0.3` for compilation.
- Valheim assemblies for compilation: `Assembly-CSharp.dll`, `assembly_valheim.dll`, `assembly_utils.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.PhysicsModule.dll`, and `UnityEngine.UI.dll`.

## Setup in VS Code

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) and [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) in the Thunderstore/r2modman profile used to launch the game.
2. Open the project folder in VS Code.
3. Create `BuildingInspector.local.props` next to `BuildingInspector.csproj` and set your local game and plugin paths:

   ```xml
   <Project>
     <PropertyGroup>
       <ValheimPath>C:\path\to\Valheim</ValheimPath>
       <PluginDeployPath>C:\path\to\profile\BepInEx\plugins\Lanoz-BuildingInspector</PluginDeployPath>
     </PropertyGroup>
   </Project>
   ```

   `ValheimPath` is the game root containing `valheim.exe` and `valheim_Data`. `PluginDeployPath` is the active profile's Building Inspector plugin folder. Both values are machine-specific, so the local props file is ignored by Git.

4. From the project folder, run:

   ```powershell
   dotnet build
   ```

The build restores compile-time packages, writes `bin/Debug/net472/BuildingInspector.dll`, and copies the DLL to `PluginDeployPath`. BepInEx and Jötunn must be installed in the same active profile when running the game.

## Test In Game

1. Launch Valheim through Thunderstore/r2modman with the profile containing BepInEx, Jötunn, and Building Inspector. Enter a world and equip the regular Hammer.
2. Open the build menu and choose `Inspection`, then select one of the six inspection labels. If enabled, the separate test flag appears there as well.
3. Aim at flat ground, a sloped roof, or a wall. The flag aligns to the targeted surface. Use the mouse wheel to rotate around the surface normal, or use `I`/`K` and `J`/`L` to adjust its tilt. `Insert` resets the tilt.
4. Place the flag and press `N` while aiming at it. Edit its note, hover over category buttons, choose a category, and press **Save**. Reopen the editor to check the saved values; check that **Cancel** or `Esc` leaves them unchanged. Use the regular Hammer removal mode to remove flags.
5. Check standard-flag spacing, walk through a flag, and remove its support to check collapse behavior. Save and reload the world to check that flags and edits remain. For configuration testing, enable `TestFlagEnabled` under `[Debug]`, adjust its name, description, and color, then restart.
6. Check `BepInEx/LogOutput.log` in the active profile. The plugin logs `Building Inspector loading...`, `Inspection Flags registered: 6.`, and `Building Inspector loaded!`; if enabled, the test flag has its own registration message. Registration errors include the cause.

For full multiplayer functionality, use the same Building Inspector version with BepInEx and Jötunn on the host/server and clients using the flags. Note and category edits require the mod's server-side authorization handlers, including on a dedicated server. The current Jötunn configuration does not enforce mod installation or matching versions when connecting.

Check that the placing character can edit after reconnecting, another character cannot edit, and changes appear for other players even when a different client owns the flag's network object.

## API References

- [Jötunn: Pieces and PieceTables](https://valheim-modding.github.io/Jotunn/tutorials/pieces.html)
- [Jötunn: CustomPiece](https://valheim-modding.github.io/Jotunn/api/Jotunn.Entities.CustomPiece.html)
- [Jötunn: PieceManager](https://valheim-modding.github.io/Jotunn/api/Jotunn.Managers.PieceManager.html)
- [Jötunn releases](https://github.com/Valheim-Modding/Jotunn/releases)
