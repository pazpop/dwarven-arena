# PixelLab editable map for Unity 6

Open the downloaded `.unitypackage` file while your Unity 6 project is open,
then choose **Import** in Unity's package dialog. PixelLab imports the source,
installs Unity's official 2D Tilemap Editor package when needed, and builds the
editable map automatically.

The finalized export contains exactly one map root. Terrain maps use
`Assets/PixelLabMap`; Buildings maps use the isolated
`Assets/PixelLabMaps/<map-id>` folder so importing another map cannot replace an
edited scene. That root contains `Source`, `Generated`, and `Support`. The
generated content includes:

- `Generated/PixelLabMap.unity`
- one native logical terrain Tilemap for regular/high top-down and sidescroller maps, plus projection Tilemaps per elevation
- terrain-aware painting for regular top-down, high top-down, sidescroller, square, isometric, hexagonal, and oblique sets
- PixelLab-aligned backgrounds, inpaint overlays, objects, and gameplay annotations
- object BoxCollider2D components live on the same GameObject as their SpriteRenderer
- gameplay rectangles are merged into one CompositeCollider2D per exported area

Gameplay rectangles are grouped into generic Unity 2D trigger areas; their names
and tags do not imply collision or navigation behavior. Point annotations remain
generic markers with their PixelLab ID and annotation layer.

Use **Tools > PixelLab > Paint Map** to choose a Y/elevation layer and any of
its named terrains. Connected 16-tile sheets are rendering rules behind that
single terrain map, so painting replaces the existing material instead of
stacking another pair layer over it. The window starts in **Navigate**, so Unity's normal
selection and transform tools keep working. Choose **Paint** or **Erase** only
while editing terrain; press **Escape**, choose **Navigate**, or select any Unity
Scene tool to stop PixelLab painting.

For Unity's standard Brush, Box, Fill, Picker, and Eraser controls, use **Open
Unity Auto-Terrain Palette** in the painter or **Tools > PixelLab > Open
Auto-Terrain Palette**. The generated terrain brushes still drive PixelLab's
16-tile/projection renderer, so the correct edge and corner variants are chosen
without manually selecting individual source tiles.

The normal painter contains only the easy auto-terrain choices. Projection maps
also expose **Advanced Individual Tiles** for intentional source-variant overrides.
Use **Tools > PixelLab > Rebuild Map** to restore the generated scene from the
packaged source.

To remove an imported export, use **Tools > PixelLab > Remove Imported Map**.
It closes the generated scene and releases the active palette before removing
the imported map root.

Save the currently open scene before importing. PixelLab creates its own scene
and does not replace saved scenes or discard an untitled scene containing objects.

The runtime renderer reads the referenced manifest `TextAsset`; it does not depend on editor filesystem paths and remains functional in player builds.
