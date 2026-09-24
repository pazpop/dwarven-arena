#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PixelLab.MapExport;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace PixelLab.MapExport.Editor
{
    [InitializeOnLoad]
    internal static class PixelLabMapImporter
    {
        private const string TilemapEditorPackage = "com.unity.2d.tilemap@1.0.0";
        private const string ImporterSchemaVersion = "32";
        internal const string PackageRoot = "Assets/PixelLabMapPackage";
        private const string PackageSourcePrefix = "Assets/PixelLabMapPackage/Source/";
        internal const string PackageManifestAssetPath =
            "Assets/PixelLabMapPackage/Source/engine-map.json";
        private const string LegacyGeneratedRoot = "Assets/PixelLabMap";
        private const string GeneratedSceneSuffix = "/Generated/PixelLabMap.unity";
        private static string activeGeneratedRoot = string.Empty;
        private static string pendingSceneBuildRoot = string.Empty;
        internal static string GeneratedRoot => string.IsNullOrEmpty(activeGeneratedRoot)
            ? LegacyGeneratedRoot
            : activeGeneratedRoot;
        private static string GeneratedSourcePrefix => GeneratedRoot + "/Source/";
        private static string GeneratedManifestAssetPath =>
            GeneratedRoot + "/Source/engine-map.json";
        private static string GeneratedImporterVersionAssetPath =>
            GeneratedRoot + "/importer-version.txt";
        private static string GeneratedDirectory => GeneratedRoot + "/Generated";
        private static string GeneratedTileDirectory =>
            GeneratedDirectory + "/Tiles";
        private static string GeneratedSpriteDirectory =>
            GeneratedDirectory + "/Sprites";
        private static string PaletteDirectory =>
            GeneratedDirectory + "/Palettes";
        internal static string SceneAssetPath =>
            GeneratedDirectory + "/PixelLabMap.unity";
        private const int MaximumTextureSize = 16384;
        private const float PaletteCardPadding = 0.25f;
        private static readonly Vector3 ProjectionTransparencySortAxis =
            new Vector3(-0.001f, 1f, -0.26f);
        private static AddRequest tilemapEditorRequest;
        private static bool sceneBuildScheduled;

        static PixelLabMapImporter()
        {
            // Applied on EVERY domain load, not only at import: the statics
            // don't survive editor restarts, and without the Y axis the
            // skirted-tile seam order falls back to Default sorting and
            // resolves arbitrarily ("wrong connecting borders").
            GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
            GraphicsSettings.transparencySortAxis = ProjectionTransparencySortAxis;
            ScheduleImport();
        }

        private static string SafeMapSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    "Buildings export requires a stable map id."
                );
            }
            var characters = value.Select(character =>
                char.IsLetterOrDigit(character) || character == '_' || character == '-'
                    ? character
                    : '-'
            ).ToArray();
            var segment = new string(characters).Trim('-');
            if (string.IsNullOrEmpty(segment))
            {
                throw new InvalidOperationException(
                    "Buildings export requires a stable map id."
                );
            }
            return segment;
        }

        private static string GeneratedRootForManifest(PixelLabMapManifest manifest)
        {
            return (manifest?.buildingKits?.Length ?? 0) == 0
                ? LegacyGeneratedRoot
                : "Assets/PixelLabMaps/" + SafeMapSegment(manifest.mapId);
        }

        private static string GeneratedRootForScene(Scene scene)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)
                || !scene.path.EndsWith(GeneratedSceneSuffix, StringComparison.Ordinal))
            {
                return string.Empty;
            }
            return scene.path.Substring(
                0,
                scene.path.Length - GeneratedSceneSuffix.Length
            );
        }

        internal static void ActivateEditorContext()
        {
            var selected = Selection.activeGameObject;
            var selectedRoot = selected == null
                ? string.Empty
                : GeneratedRootForScene(selected.scene);
            var sceneRoot = GeneratedRootForScene(SceneManager.GetActiveScene());
            var resolved = string.IsNullOrEmpty(selectedRoot) ? sceneRoot : selectedRoot;
            if (!string.IsNullOrEmpty(resolved))
            {
                activeGeneratedRoot = resolved;
            }
        }

        private static PixelLabMapManifest ParseManifest(TextAsset asset)
        {
            return asset == null
                ? null
                : JsonUtility.FromJson<PixelLabMapManifest>(asset.text);
        }

        private static bool PackageTargetsActiveRoot(TextAsset packageManifest)
        {
            var manifest = ParseManifest(packageManifest);
            return manifest != null
                && string.Equals(
                    GeneratedRootForManifest(manifest),
                    GeneratedRoot,
                    StringComparison.Ordinal
                );
        }

        internal static void ScheduleImport()
        {
            EditorApplication.delayCall -= ImportIfNeeded;
            EditorApplication.delayCall += ImportIfNeeded;
        }

        [MenuItem("Tools/PixelLab/Rebuild Map")]
        private static void RebuildMap()
        {
            ActivateEditorContext();
            ImportMap(true);
        }

        [MenuItem("Tools/PixelLab/Rebuild Map (Replace Buildings Edits)")]
        private static void RebuildMapReplacingBuildingEdits()
        {
            ActivateEditorContext();
            if (!EditorUtility.DisplayDialog(
                "Replace native Buildings edits?",
                "This explicitly discards saved semantic edits for every "
                    + "Buildings kit, then rebuilds from the newly imported PixelLab map. "
                    + "This cannot be undone after the assets are replaced.",
                "Replace Buildings Edits",
                "Cancel"
            ))
            {
                return;
            }
            ExplicitlyReplaceBuildingEditStates();
            ImportMap(true);
        }

        public static void ImportFromCommandLine()
        {
            activeGeneratedRoot = string.Empty;
            InstallTilemapEditorBlocking();
            ImportMap(true, true);
        }

        public static void ImportFromCommandLineReplacingBuildingEdits()
        {
            activeGeneratedRoot = string.Empty;
            InstallTilemapEditorBlocking();
            ExplicitlyReplaceBuildingEditStates();
            ImportMap(true, true);
        }

        [MenuItem("Tools/PixelLab/Paint Map")]
        private static void OpenMapPainter()
        {
            PixelLabMapPainterWindow.OpenWindow();
        }

        [MenuItem("Tools/PixelLab/Open Auto-Terrain Palette")]
        private static void OpenAutoTerrainPalette()
        {
            OpenSelectedPalette(false);
        }

        [MenuItem("Tools/PixelLab/Open Tile Palette (Advanced)")]
        private static void OpenAdvancedTilePalette()
        {
            OpenSelectedPalette(true);
        }

        private static void OpenSelectedPalette(bool advanced)
        {
            var selected = Selection.activeGameObject == null
                ? null
                : Selection.activeGameObject.GetComponent<PixelLabTileRenderer>();
            var renderer = selected != null
                ? selected
                : UnityEngine.Object.FindFirstObjectByType<PixelLabTileRenderer>();
            if (renderer == null)
            {
                PixelLabMapPainterWindow.OpenWindow();
                return;
            }

            OpenPalette(renderer, advanced);
        }

        internal static void OpenPalette(
            PixelLabTileRenderer renderer,
            bool advanced
        )
        {
            PixelLabMapPainterWindow.StopAllPainting();
            var path = advanced && !string.IsNullOrEmpty(renderer.advancedPaletteAssetPath)
                ? renderer.advancedPaletteAssetPath
                : renderer.paletteAssetPath;
            var palette = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (palette == null)
            {
                Debug.LogWarning("The selected PixelLab layer has no generated palette.");
                return;
            }

            Selection.activeGameObject = renderer.gameObject;
            if (advanced && renderer.gridKind == "iso")
            {
                EditorApplication.ExecuteMenuItem("Window/2D/Tile Palette");
                EditorApplication.delayCall += () =>
                {
                    SetGridPaintingState("palette", palette);
                    SetGridPaintingState("scenePaintTarget", renderer.gameObject);
                    RefreshTilePalettePreview();
                };
                return;
            }

            SetGridPaintingState("palette", palette);
            SetGridPaintingState("scenePaintTarget", renderer.gameObject);
            EditorApplication.ExecuteMenuItem("Window/2D/Tile Palette");
        }

        private static void RefreshTilePalettePreview()
        {
            var windowType = FindEditorType("UnityEditor.Tilemaps.GridPaintPaletteWindow");
            if (windowType == null)
            {
                return;
            }
            var clipboardProperty = windowType.GetProperty(
                "clipboardView",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            if (clipboardProperty == null)
            {
                return;
            }
            foreach (var window in Resources.FindObjectsOfTypeAll(windowType))
            {
                var clipboard = clipboardProperty.GetValue(window);
                if (clipboard != null)
                {
                    clipboard.GetType().GetMethod("ResetPreviewInstance")?.Invoke(clipboard, null);
                    clipboard.GetType().GetMethod("FrameEntirePalette")?.Invoke(clipboard, null);
                }
                (window as EditorWindow)?.Repaint();
            }
        }

        internal static void OpenBuildingPalette(
            PixelLabBuildingController controller
        )
        {
            PixelLabMapPainterWindow.StopAllPainting();
            ActivateEditorContext();
            var path = BuildingPaletteAssetPath(controller.Kit);
            var palette = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (palette == null)
            {
                Debug.LogWarning("The selected PixelLab Buildings kit has no generated palette.");
                return;
            }

            Selection.activeGameObject = controller.gameObject;
            SetGridPaintingState("palette", palette);
            SetGridPaintingState("scenePaintTarget", null);
            EditorApplication.ExecuteMenuItem("Window/2D/Tile Palette");
        }

        [MenuItem("Tools/PixelLab/Remove Imported Map")]
        private static void RemoveImportedMap()
        {
            if (!EditorUtility.DisplayDialog(
                "Remove imported PixelLab map?",
                "This removes the generated map, palettes, source images, and PixelLab "
                    + "editor tools from this Unity project. This cannot be undone.",
                "Remove PixelLab Map",
                "Cancel"
            ))
            {
                return;
            }
            RemoveImportedMapAssets(true);
        }

        public static void RemoveFromCommandLine()
        {
            RemoveImportedMapAssets(false);
        }

        internal static bool RemoveImportedMapAssets(bool showDialogs)
        {
            try
            {
                ActivateEditorContext();
                PixelLabMapPainterWindow.CloseAll();
                CloseTilePaletteWindows();
                SetGridPaintingState("scenePaintTarget", null);
                Selection.activeObject = null;

                for (var index = SceneManager.sceneCount - 1; index >= 0; index--)
                {
                    var scene = SceneManager.GetSceneAt(index);
                    if (!scene.IsValid() || scene.path != SceneAssetPath)
                    {
                        continue;
                    }
                    if (scene.isDirty && showDialogs && !EditorUtility.DisplayDialog(
                        "Discard PixelLab scene changes?",
                        "The imported PixelLab scene has unsaved tile edits. Removing the "
                            + "map will discard them.",
                        "Discard and Remove",
                        "Cancel"
                    ))
                    {
                        return false;
                    }
                    if (SceneManager.sceneCount == 1)
                    {
                        EditorSceneManager.NewScene(
                            NewSceneSetup.EmptyScene,
                            NewSceneMode.Single
                        );
                        break;
                    }
                    EditorSceneManager.CloseScene(scene, true);
                }

                EditorBuildSettings.scenes = EditorBuildSettings.scenes
                    .Where(scene => scene.path != SceneAssetPath)
                    .ToArray();
                EditorUtility.UnloadUnusedAssetsImmediate();

                var packageManifest = AssetDatabase.LoadAssetAtPath<TextAsset>(
                    PackageManifestAssetPath
                );
                var removeCurrentPackageSource = PackageTargetsActiveRoot(
                    packageManifest
                );
                var failedPaths = new List<string>();
                var paths = new List<string> { GeneratedRoot };
                var packageSource = PackageRoot + "/Source";
                if (removeCurrentPackageSource
                    && !packageSource.StartsWith(
                        GeneratedRoot + "/",
                        StringComparison.Ordinal
                    ))
                {
                    paths.Add(packageSource);
                }
                var deleted = AssetDatabase.DeleteAssets(
                    paths.ToArray(),
                    failedPaths
                );
                AssetDatabase.Refresh();
                if (!deleted || failedPaths.Count > 0)
                {
                    var message = "Unity could not remove: "
                        + string.Join(", ", failedPaths);
                    if (showDialogs)
                    {
                        EditorUtility.DisplayDialog("PixelLab removal incomplete", message, "OK");
                    }
                    Debug.LogError(message);
                    return false;
                }

                activeGeneratedRoot = string.Empty;
                Debug.Log(
                    "Removed the selected imported PixelLab map. Shared PixelLab editor "
                        + "tools remain available for other imported maps."
                );
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (showDialogs)
                {
                    EditorUtility.DisplayDialog(
                        "PixelLab removal failed",
                        exception.Message,
                        "OK"
                    );
                }
                return false;
            }
        }

        private static void CloseTilePaletteWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                var typeName = window.GetType().FullName ?? string.Empty;
                if (typeName.Contains("GridPaintPaletteWindow")
                    || typeName.Contains("TilePalette"))
                {
                    window.Close();
                }
            }
        }

        private static void ImportIfNeeded()
        {
            if (!TilemapEditorAvailable())
            {
                InstallTilemapEditor();
                return;
            }

            var packageManifest = AssetDatabase.LoadAssetAtPath<TextAsset>(
                PackageManifestAssetPath
            );
            if (packageManifest == null)
            {
                return;
            }

            var packageDefinition = ParseManifest(packageManifest);
            ValidateManifest(packageDefinition);
            activeGeneratedRoot = GeneratedRootForManifest(packageDefinition);

            var generatedManifest = AssetDatabase.LoadAssetAtPath<TextAsset>(
                GeneratedManifestAssetPath
            );
            var generatedImporterVersion = AssetDatabase.LoadAssetAtPath<TextAsset>(
                GeneratedImporterVersionAssetPath
            );
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneAssetPath);
            var expectedGeneratedJson = packageManifest.text.Replace(
                PackageSourcePrefix,
                GeneratedSourcePrefix
            );
            if (scene == null && generatedManifest == null)
            {
                ImportMap(false);
                return;
            }
            if (
                scene == null
                || generatedManifest == null
                || generatedImporterVersion == null
                || !string.Equals(
                    generatedImporterVersion.text.Trim(),
                    ImporterSchemaVersion,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    generatedManifest.text,
                    expectedGeneratedJson,
                    StringComparison.Ordinal
                )
            )
            {
                ImportMap(true);
            }
        }

        private static bool TilemapEditorAvailable()
        {
            return FindEditorType("UnityEditor.Tilemaps.GridPaletteUtility") != null;
        }

        private static void InstallTilemapEditorBlocking()
        {
            // Batchmode never pumps EditorApplication.update, so the
            // interactive installer below would start the request and return
            // before it lands — the import then dies on the missing palette
            // API. Command-line callers wait for the package here instead.
            if (TilemapEditorAvailable())
            {
                return;
            }
            Debug.Log("PixelLab is installing Unity's 2D Tilemap Editor package.");
            var request = Client.Add(TilemapEditorPackage);
            while (!request.IsCompleted)
            {
                System.Threading.Thread.Sleep(100);
            }
            if (request.Status == StatusCode.Failure)
            {
                throw new InvalidOperationException(
                    "PixelLab could not install Unity's 2D Tilemap Editor "
                    + "package (" + request.Error.message + "). Install it "
                    + "from Window > Package Manager > Unity Registry, then "
                    + "run Tools > PixelLab > Rebuild Map."
                );
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // Unity only loads a freshly added package's editor assemblies
            // after a domain reload, which cannot happen inside this same
            // batch invocation — so tell the caller to run it again rather
            // than dying on the missing palette API further down.
            throw new InvalidOperationException(
                "PixelLab installed Unity's 2D Tilemap Editor package. Unity "
                + "must reload before it can be used — run this import again."
            );
        }

        private static void InstallTilemapEditor()
        {
            if (tilemapEditorRequest != null)
            {
                return;
            }
            Debug.Log("PixelLab is installing Unity's 2D Tilemap Editor package.");
            tilemapEditorRequest = Client.Add(TilemapEditorPackage);
            EditorApplication.update += WaitForTilemapEditor;
        }

        private static void WaitForTilemapEditor()
        {
            if (tilemapEditorRequest == null || !tilemapEditorRequest.IsCompleted)
            {
                return;
            }
            EditorApplication.update -= WaitForTilemapEditor;
            if (tilemapEditorRequest.Status == StatusCode.Failure)
            {
                Debug.LogError(
                    "PixelLab could not install Unity's 2D Tilemap Editor package: "
                    + tilemapEditorRequest.Error.message
                );
            }
            tilemapEditorRequest = null;
            EditorApplication.delayCall += ImportIfNeeded;
        }

        private static Type FindEditorType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null);
        }

        internal static void SetGridPaintingState(string propertyName, UnityEngine.Object value)
        {
            var type = FindEditorType("UnityEditor.Tilemaps.GridPaintingState");
            var property = type == null ? null : type.GetProperty(propertyName);
            if (property != null)
            {
                try
                {
                    property.SetValue(null, value);
                }
                catch (TargetInvocationException) when (value == null)
                {
                    // Unity 6 rejects a null palette even after its window closes.
                }
                catch (ArgumentException) when (value == null)
                {
                    // Clearing the target is best-effort across Tilemap Editor versions.
                }
            }
        }

        private static void ImportMap(bool rebuild, bool throwOnFailure = false)
        {
            var packageManifest = AssetDatabase.LoadAssetAtPath<TextAsset>(
                PackageManifestAssetPath
            );
            if (string.IsNullOrEmpty(activeGeneratedRoot) && packageManifest != null)
            {
                var packageDefinition = ParseManifest(packageManifest);
                ValidateManifest(packageDefinition);
                activeGeneratedRoot = GeneratedRootForManifest(packageDefinition);
            }
            var usePackageSource = PackageTargetsActiveRoot(packageManifest);
            var sourceManifest = usePackageSource
                ? packageManifest
                : AssetDatabase.LoadAssetAtPath<TextAsset>(GeneratedManifestAssetPath);
            if (sourceManifest == null)
            {
                return;
            }

            try
            {
                var sourceManifestJson = sourceManifest.text;
                var manifest = ParseManifest(sourceManifest);
                ValidateManifest(manifest);
                if (rebuild)
                {
                    AssertBuildingRefreshAllowed(
                        sourceManifestJson,
                        usePackageSource
                    );
                    PrepareForRebuild();
                    AssetDatabase.DeleteAsset(GeneratedDirectory);
                    if (usePackageSource
                        && !string.Equals(
                            PackageRoot,
                            GeneratedRoot,
                            StringComparison.Ordinal
                        ))
                    {
                        AssetDatabase.DeleteAsset(GeneratedRoot + "/Source");
                    }
                }

                if (usePackageSource
                    && !string.Equals(
                        PackageSourcePrefix,
                        GeneratedSourcePrefix,
                        StringComparison.Ordinal
                    ))
                {
                    MaterializeSource(manifest, sourceManifestJson);
                }
                // ConfigureTextures classifies textures by comparing manifest
                // asset paths against on-disk paths under GeneratedRoot/Source.
                // The package manifest stores Assets/PixelLabMapPackage/Source
                // paths, so it must be re-parsed in materialized space first —
                // raw paths never match, which silently gave building sprites
                // the tile pivot and failed the top-left pivot assert at scene
                // build (real map 21d758d0, kit 7aba9ccd).
                var materializedManifest = usePackageSource
                    ? JsonUtility.FromJson<PixelLabMapManifest>(
                        sourceManifestJson.Replace(
                            PackageSourcePrefix,
                            GeneratedSourcePrefix
                        )
                    )
                    : manifest;
                ConfigureTextures(manifest.pixelsPerUnit, materializedManifest);
                if (!Application.isBatchMode && !throwOnFailure)
                {
                    ScheduleSceneBuild();
                    return;
                }
                BuildMaterializedScene();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (throwOnFailure)
                {
                    throw;
                }
            }
        }

        private static void AssertBuildingRefreshAllowed(
            string sourceManifestJson,
            bool packageSource
        )
        {
            var generatedJson = packageSource
                ? sourceManifestJson.Replace(PackageSourcePrefix, GeneratedSourcePrefix)
                : sourceManifestJson;
            var manifest = JsonUtility.FromJson<PixelLabMapManifest>(generatedJson);
            foreach (var kit in manifest.buildingKits ??
                Array.Empty<PixelLabBuildingKit>())
            {
                PixelLabBuildingImporterSupport.AssertRefreshAllowed(
                    manifest.mapId,
                    kit,
                    StableHash(generatedJson + "|" + kit.id)
                );
            }
        }

        private static void ExplicitlyReplaceBuildingEditStates()
        {
            var packageManifest = AssetDatabase.LoadAssetAtPath<TextAsset>(
                PackageManifestAssetPath
            );
            if (string.IsNullOrEmpty(activeGeneratedRoot) && packageManifest != null)
            {
                var packageDefinition = ParseManifest(packageManifest);
                ValidateManifest(packageDefinition);
                activeGeneratedRoot = GeneratedRootForManifest(packageDefinition);
            }
            var usePackageSource = PackageTargetsActiveRoot(packageManifest);
            var sourceManifest = usePackageSource
                ? packageManifest
                : AssetDatabase.LoadAssetAtPath<TextAsset>(GeneratedManifestAssetPath);
            if (sourceManifest == null)
            {
                throw new InvalidOperationException(
                    "The PixelLab package manifest is missing."
                );
            }
            var generatedJson = usePackageSource
                ? sourceManifest.text.Replace(PackageSourcePrefix, GeneratedSourcePrefix)
                : sourceManifest.text;
            var manifest = JsonUtility.FromJson<PixelLabMapManifest>(generatedJson);
            ValidateManifest(manifest);
            foreach (var kit in manifest.buildingKits ??
                Array.Empty<PixelLabBuildingKit>())
            {
                PixelLabBuildingImporterSupport.ExplicitlyReplaceEditState(
                    manifest.mapId,
                    kit,
                    StableHash(generatedJson + "|" + kit.id)
                );
            }
            AssetDatabase.SaveAssets();
        }

        private static void ScheduleSceneBuild()
        {
            sceneBuildScheduled = true;
            pendingSceneBuildRoot = GeneratedRoot;
            EditorApplication.update -= CompleteScheduledImport;
            EditorApplication.update += CompleteScheduledImport;
        }

        private static void CompleteScheduledImport()
        {
            if (!sceneBuildScheduled
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
            {
                return;
            }
            EditorApplication.update -= CompleteScheduledImport;
            sceneBuildScheduled = false;
            try
            {
                activeGeneratedRoot = pendingSceneBuildRoot;
                pendingSceneBuildRoot = string.Empty;
                BuildMaterializedScene();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void BuildMaterializedScene()
        {
            var generatedManifest = AssetDatabase.LoadAssetAtPath<TextAsset>(
                GeneratedManifestAssetPath
            );
            if (generatedManifest == null)
            {
                throw new InvalidOperationException(
                    "Unity did not import the materialized PixelLab manifest."
                );
            }
            var manifest = JsonUtility.FromJson<PixelLabMapManifest>(
                generatedManifest.text
            );
            ValidateManifest(manifest);
            BuildScene(manifest, generatedManifest);
        }

        private static void PrepareForRebuild()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || activeScene.path != SceneAssetPath)
            {
                return;
            }
            if (activeScene.isDirty)
            {
                throw new InvalidOperationException(
                    "Save or discard changes to the current PixelLab map scene, then use "
                    + "Tools > PixelLab > Rebuild Map. PixelLab will not discard painted tiles."
                );
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void ValidateManifest(PixelLabMapManifest manifest)
        {
            if (manifest == null || manifest.format != "pixellab-engine-map")
            {
                throw new InvalidOperationException(
                    "The PixelLab Unity package contains an invalid map manifest."
                );
            }
            if (!manifest.hasSeparateTileLayers && string.IsNullOrEmpty(manifest.baseImage))
            {
                throw new InvalidOperationException(
                    "The PixelLab map manifest is missing its base image."
                );
            }
            if (manifest.pixelsPerUnit <= 0f)
            {
                throw new InvalidOperationException(
                    "The PixelLab map pixels-per-unit value must be positive."
                );
            }
            if ((manifest.buildingKits?.Length ?? 0) > 0
                && string.IsNullOrEmpty(manifest.mapId))
            {
                throw new InvalidOperationException(
                    "The PixelLab Buildings manifest is missing its stable map id."
                );
            }
            foreach (var kit in manifest.buildingKits
                ?? Array.Empty<PixelLabBuildingKit>())
            {
                if (kit.gridKind != "iso"
                    && kit.gridKind != "oblique"
                    && kit.gridKind != "square-topdown")
                {
                    throw new InvalidOperationException(
                        "PixelLab Buildings supports only isometric, oblique, and "
                            + "square-topdown projections."
                    );
                }
                if (kit.projection == null
                    || kit.semanticAssets == null
                    || kit.assets == null
                    || kit.assets.Length == 0
                    || kit.storeys == null
                    || kit.storeys.Length == 0
                    || kit.structureVariants == null
                    || kit.structureVariants.Length != 256
                    || kit.partitionVariants == null
                    || kit.partitionVariants.Length != 512)
                {
                    throw new InvalidOperationException(
                        "The PixelLab Buildings kit is missing native editing data: "
                            + kit.name
                    );
                }
            }
        }

        private static void MaterializeSource(
            PixelLabMapManifest manifest,
            string packageManifestJson
        )
        {
            var paths = new HashSet<string>(manifest.sourceFiles ?? Array.Empty<string>());
            AddSourcePath(paths, manifest.baseImage);
            if (manifest.background != null)
            {
                AddSourcePath(paths, manifest.background.image);
            }
            foreach (var overlay in manifest.overlays ?? Array.Empty<PixelLabOverlay>())
            {
                AddSourcePath(paths, overlay.image);
            }
            foreach (var item in manifest.objects ?? Array.Empty<PixelLabObject>())
            {
                AddSourcePath(paths, item.image);
            }
            foreach (var layer in manifest.projectionLayers
                ?? Array.Empty<PixelLabProjectionLayer>())
            {
                foreach (var asset in layer.tileAssets ?? Array.Empty<PixelLabTileAsset>())
                {
                    AddSourcePath(paths, asset.assetPath);
                }
            }
            foreach (var kit in manifest.buildingKits
                ?? Array.Empty<PixelLabBuildingKit>())
            {
                foreach (var asset in kit.assets ?? Array.Empty<PixelLabTileAsset>())
                {
                    AddSourcePath(paths, asset.assetPath);
                }
            }
            if (manifest.legacyTerrain != null)
            {
                foreach (var layer in manifest.legacyTerrain.layers
                    ?? Array.Empty<PixelLabLegacyLayer>())
                {
                    AddSourcePath(paths, layer.image);
                }
            }

            var orderedPaths = paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
            foreach (var sourcePath in orderedPaths)
            {
                if (!sourcePath.StartsWith(PackageSourcePrefix, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Unexpected PixelLab package source path: " + sourcePath
                    );
                }
                var destinationPath = GeneratedSourcePrefix
                    + sourcePath.Substring(PackageSourcePrefix.Length);
                Directory.CreateDirectory(
                    AbsoluteAssetPath(Path.GetDirectoryName(destinationPath).Replace('\\', '/'))
                );
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            foreach (var sourcePath in orderedPaths)
            {
                var destinationPath = GeneratedSourcePrefix
                    + sourcePath.Substring(PackageSourcePrefix.Length);
                if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null)
                {
                    AssetDatabase.DeleteAsset(destinationPath);
                }
                if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                {
                    throw new InvalidOperationException(
                        "Could not copy PixelLab package asset to " + destinationPath
                    );
                }
            }

            var generatedJson = packageManifestJson.Replace(
                PackageSourcePrefix,
                GeneratedSourcePrefix
            );
            File.WriteAllText(
                AbsoluteAssetPath(GeneratedManifestAssetPath),
                generatedJson,
                new UTF8Encoding(false)
            );
            File.WriteAllText(
                AbsoluteAssetPath(GeneratedImporterVersionAssetPath),
                ImporterSchemaVersion,
                new UTF8Encoding(false)
            );
            AssetDatabase.ImportAsset(
                GeneratedManifestAssetPath,
                ImportAssetOptions.ForceSynchronousImport
            );
            AssetDatabase.ImportAsset(
                GeneratedImporterVersionAssetPath,
                ImportAssetOptions.ForceSynchronousImport
            );
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void AddSourcePath(HashSet<string> paths, string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        private static float FootprintHeight(PixelLabMapManifest manifest, string texturePath)
        {
            if (manifest == null)
            {
                return 32f;
            }
            if (manifest.projectionLayers != null && manifest.projectionLayers.Length > 0)
            {
                foreach (var proj in manifest.projectionLayers)
                {
                    if ((proj.tileAssets != null && proj.tileAssets.Any(a => a.assetPath == texturePath))
                        || (proj.placements != null && proj.placements.Any(p => p.assetPath == texturePath)))
                    {
                        if (proj.gridKind == "iso")
                        {
                            if (proj.projection != null && proj.projection.cellSize != null && proj.projection.cellSize.height > 0)
                            {
                                return proj.projection.cellSize.height;
                            }
                            if (proj.projection != null && proj.projection.qBasis != null && proj.projection.rBasis != null)
                            {
                                return Mathf.Abs(proj.projection.qBasis.y) + Mathf.Abs(proj.projection.rBasis.y);
                            }
                            return manifest.pixelsPerUnit * 0.5f;
                        }
                        if (proj.projection != null && proj.projection.cellSize != null && proj.projection.cellSize.height > 0)
                        {
                            return proj.projection.cellSize.height;
                        }
                        return manifest.pixelsPerUnit;
                    }
                }
                var defaultProj = manifest.projectionLayers[0];
                if (defaultProj.gridKind == "iso")
                {
                    if (defaultProj.projection != null && defaultProj.projection.cellSize != null && defaultProj.projection.cellSize.height > 0)
                    {
                        return defaultProj.projection.cellSize.height;
                    }
                    if (defaultProj.projection != null && defaultProj.projection.qBasis != null && defaultProj.projection.rBasis != null)
                    {
                        return Mathf.Abs(defaultProj.projection.qBasis.y) + Mathf.Abs(defaultProj.projection.rBasis.y);
                    }
                    return manifest.pixelsPerUnit * 0.5f;
                }
                if (defaultProj.projection != null && defaultProj.projection.cellSize != null && defaultProj.projection.cellSize.height > 0)
                {
                    return defaultProj.projection.cellSize.height;
                }
            }
            if (manifest.tileSize != null && manifest.tileSize.height > 0)
            {
                return manifest.tileSize.height;
            }
            return manifest.pixelsPerUnit;
        }

        private static bool TryProjectionTexturePivot(
            PixelLabMapManifest manifest,
            string texturePath,
            int textureWidth,
            int textureHeight,
            out Vector2 pivot
        )
        {
            pivot = Vector2.zero;
            if (manifest == null || textureWidth <= 0 || textureHeight <= 0)
            {
                return false;
            }
            foreach (var layer in manifest.projectionLayers
                ?? Array.Empty<PixelLabProjectionLayer>())
            {
                var layerOwnsTexture = (layer.tileAssets
                        ?? Array.Empty<PixelLabTileAsset>())
                    .Any(asset => asset.assetPath == texturePath)
                    || (layer.placements ?? Array.Empty<PixelLabPlacement>())
                        .Any(placement => placement.assetPath == texturePath);
                if (!layerOwnsTexture)
                {
                    continue;
                }
                foreach (var placement in layer.placements
                    ?? Array.Empty<PixelLabPlacement>())
                {
                    if (placement.assetPath == texturePath
                        && TryProjectionPlacementPivot(
                            layer,
                            placement,
                            textureWidth,
                            textureHeight,
                            out pivot
                        ))
                    {
                        return true;
                    }
                }
                // Every variant in one projected tile group is drawn at the
                // same frame top-left in the map editor. A transition variant
                // can be absent from the saved map, so it has no placement of
                // its own from which to recover that shared cell anchor. Use
                // any same-size placed sibling before falling back to the
                // generic centre pivot; otherwise Auto painting shifts only
                // the newly resolved variants (6 px on the real oblique set).
                foreach (var placement in layer.placements
                    ?? Array.Empty<PixelLabPlacement>())
                {
                    if (TryProjectionPlacementPivot(
                        layer,
                        placement,
                        textureWidth,
                        textureHeight,
                        out pivot
                    ))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool TryProjectionPlacementPivot(
            PixelLabProjectionLayer layer,
            PixelLabPlacement placement,
            int textureWidth,
            int textureHeight,
            out Vector2 pivot
        )
        {
            pivot = Vector2.zero;
            var projection = layer.projection;
            if (projection == null || projection.qBasis == null
                || projection.rBasis == null
                || !Mathf.Approximately(placement.width, textureWidth)
                || !Mathf.Approximately(placement.height, textureHeight))
            {
                return false;
            }
            var origin = projection.editorOrigin ?? projection.origin;
            if (origin == null)
            {
                return false;
            }
            var anchorX = origin.x
                + placement.q * projection.qBasis.x
                + placement.r * projection.rBasis.x;
            var anchorY = origin.y
                + placement.q * projection.qBasis.y
                + placement.r * projection.rBasis.y;
            if (projection.stackBasis != null)
            {
                anchorX += placement.layerY * projection.stackBasis.x;
                anchorY += placement.layerY * projection.stackBasis.y;
            }
            pivot = new Vector2(
                (anchorX - placement.xPx) / textureWidth,
                (placement.yPx + textureHeight - anchorY) / textureHeight
            );
            return true;
        }

        private static void ConfigureTextures(float pixelsPerUnit, PixelLabMapManifest manifest)
        {
            var guids = AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { GeneratedRoot + "/Source" }
            );
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                importer.GetSourceTextureWidthAndHeight(out var width, out var height);
                var sourceMaximum = Mathf.Max(width, height);
                if (sourceMaximum > MaximumTextureSize)
                {
                    throw new InvalidOperationException(
                        "PixelLab texture " + path + " is " + width + "x" + height
                        + "; Unity 6 supports at most " + MaximumTextureSize + "px here."
                    );
                }
                var maximumTextureSize = Mathf.Clamp(
                    Mathf.NextPowerOfTwo(Mathf.Max(1, sourceMaximum)),
                    32,
                    MaximumTextureSize
                );
                var isBuildingTexture = manifest.buildingKits != null
                    && manifest.buildingKits.Any(k => k.assets != null
                        && k.assets.Any(a => a.assetPath == path));
                var isTileTexture = (path.Contains("/tile-layers/") || path.Contains("/tilesets/"))
                    && !isBuildingTexture;
                var desiredPivot = new Vector2(0f, 1f);
                if (isTileTexture)
                {
                    if (!TryProjectionTexturePivot(
                        manifest,
                        path,
                        width,
                        height,
                        out desiredPivot
                    ))
                    {
                        var cellHeight = FootprintHeight(manifest, path);
                        var pivotY = height > cellHeight
                            ? (height - cellHeight * 0.5f) / height
                            : 0.5f;
                        desiredPivot = new Vector2(0.5f, pivotY);
                    }
                }

                var textureSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(textureSettings);
                var changed = importer.textureType != TextureImporterType.Sprite
                    || importer.spriteImportMode != SpriteImportMode.Single
                    || !Mathf.Approximately(importer.spritePixelsPerUnit, pixelsPerUnit)
                    || importer.filterMode != FilterMode.Point
                    || importer.mipmapEnabled
                    || importer.textureCompression != TextureImporterCompression.Uncompressed
                    || textureSettings.spriteAlignment != (int)SpriteAlignment.Custom
                    || textureSettings.spritePivot != desiredPivot
                    || importer.maxTextureSize != maximumTextureSize
                    || !importer.alphaIsTransparency
                    || importer.wrapMode != TextureWrapMode.Clamp
                    || importer.npotScale != TextureImporterNPOTScale.None;
                if (!changed)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = pixelsPerUnit;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = maximumTextureSize;

                importer.ReadTextureSettings(textureSettings);
                textureSettings.spriteAlignment = (int)SpriteAlignment.Custom;
                textureSettings.spritePivot = desiredPivot;
                importer.SetTextureSettings(textureSettings);
                importer.SaveAndReimport();
            }
        }

        private static void BuildScene(
            PixelLabMapManifest manifest,
            TextAsset manifestAsset
        )
        {
            AssetDatabase.DeleteAsset(GeneratedDirectory);
            EnsureAssetFolder(GeneratedDirectory);
            EnsureAssetFolder(GeneratedTileDirectory);
            EnsureAssetFolder(GeneratedSpriteDirectory);
            EnsureAssetFolder(PaletteDirectory);

            // Skirted tiles (hex prisms, iso blocks, oblique walls) rely on
            // painter's order along screen Y — the same semantic the Godot
            // export ships as y_sort_enabled. Individual-mode tilemaps only
            // get that order from a custom transparency sort axis, exactly
            // what Unity's own 2D templates configure.
            GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
            GraphicsSettings.transparencySortAxis = ProjectionTransparencySortAxis;

            var previousScene = SceneManager.GetActiveScene();
            var replaceUntitledScene = CanReplaceUntitledScene(previousScene);
            if (previousScene.IsValid()
                && string.IsNullOrEmpty(previousScene.path)
                && !replaceUntitledScene)
            {
                throw new InvalidOperationException(
                    "Save the current untitled scene, then use Tools > PixelLab > Rebuild Map. "
                    + "PixelLab will not discard unsaved scene objects."
                );
            }
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var openScene = SceneManager.GetSceneAt(index);
                if (openScene.IsValid() && openScene.path == SceneAssetPath)
                {
                    EditorSceneManager.CloseScene(openScene, true);
                    break;
                }
            }
            var generatedScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                replaceUntitledScene ? NewSceneMode.Single : NewSceneMode.Additive
            );
            SceneManager.SetActiveScene(generatedScene);

            var root = new GameObject(
                string.IsNullOrEmpty(manifest.name) ? "PixelLab Map" : manifest.name
            );
            BuildBackground(root.transform, manifest.background, manifest.dimensions, manifest.pixelsPerUnit);
            if (manifest.hasSeparateTileLayers)
            {
                BuildTileLayers(root.transform, manifest, manifestAsset);
                BuildOverlays(root.transform, manifest.overlays, manifest.pixelsPerUnit);
            }
            else
            {
                BuildBaseImage(root.transform, manifest.baseImage);
            }
            if (!manifest.baseIncludesObjects)
            {
                BuildObjects(root.transform, manifest.objects, manifest.pixelsPerUnit);
            }
            BuildAnnotations(root.transform, manifest.annotations, manifest.pixelsPerUnit);

            var hasProjectedLayer =
                (manifest.projectionLayers != null && manifest.projectionLayers.Any())
                || (manifest.buildingKits != null && manifest.buildingKits.Any());
            if (hasProjectedLayer)
            {
                ConfigureActiveRenderer2DTransparency();
                var camera = Camera.main ?? UnityEngine.Object.FindAnyObjectByType<Camera>();
                if (camera != null)
                {
                    camera.transparencySortMode = TransparencySortMode.CustomAxis;
                    camera.transparencySortAxis = ProjectionTransparencySortAxis;
                }
                GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
                GraphicsSettings.transparencySortAxis = ProjectionTransparencySortAxis;
            }

            EditorSceneManager.MarkSceneDirty(generatedScene);
            var saved = string.IsNullOrEmpty(generatedScene.path)
                ? EditorSceneManager.SaveScene(generatedScene, SceneAssetPath)
                : EditorSceneManager.SaveScene(generatedScene);
            if (!saved)
            {
                throw new InvalidOperationException(
                    "Unity could not save the generated PixelLab map scene."
                );
            }
            AppendBuildScene();
            AssetDatabase.SaveAssets();
            ValidateGeneratedPaintTiles();

            if (replaceUntitledScene)
            {
                SceneManager.SetActiveScene(generatedScene);
                FocusGeneratedMap(root);
            }
            else
            {
                EditorSceneManager.CloseScene(generatedScene, true);
                if (previousScene.IsValid())
                {
                    SceneManager.SetActiveScene(previousScene);
                }
            }
            Debug.Log(
                "PixelLab map imported to " + SceneAssetPath
                + ". Use Tools > PixelLab > Paint Map to edit terrain."
            );
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += PixelLabMapPainterWindow.OpenWindow;
            }
        }

        private static void ConfigureActiveRenderer2DTransparency()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                return;
            }

            var pipelineObject = new SerializedObject(pipeline);
            var rendererDataList = pipelineObject.FindProperty("m_RendererDataList");
            if (rendererDataList == null || !rendererDataList.isArray)
            {
                return;
            }

            var changed = false;
            for (var index = 0; index < rendererDataList.arraySize; index++)
            {
                var rendererData = rendererDataList
                    .GetArrayElementAtIndex(index)
                    .objectReferenceValue;
                if (rendererData == null)
                {
                    continue;
                }

                var rendererObject = new SerializedObject(rendererData);
                var sortMode = rendererObject.FindProperty("m_TransparencySortMode");
                var sortAxis = rendererObject.FindProperty("m_TransparencySortAxis");
                if (sortMode == null || sortAxis == null)
                {
                    continue;
                }

                if (sortMode.intValue == (int)TransparencySortMode.CustomAxis
                    && sortAxis.vector3Value == ProjectionTransparencySortAxis)
                {
                    continue;
                }

                sortMode.intValue = (int)TransparencySortMode.CustomAxis;
                sortAxis.vector3Value = ProjectionTransparencySortAxis;
                rendererObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(rendererData);
                changed = true;
            }

            if (changed)
            {
                Debug.Log(
                    "PixelLab configured the active 2D renderer transparency axis for "
                    + "projection tile sorting."
                );
            }
        }

        private static void ValidateGeneratedPaintTiles()
        {
            foreach (var guid in AssetDatabase.FindAssets(
                "t:PixelLabPaintTile",
                new[] { GeneratedTileDirectory }
            ))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tile = AssetDatabase.LoadAssetAtPath<PixelLabPaintTile>(path);
                if (tile == null || tile.sprite == null)
                {
                    throw new InvalidOperationException(
                        "Unity generated an invalid PixelLab paint tile: " + path
                    );
                }
            }
        }

        private static void FocusGeneratedMap(GameObject root)
        {
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return;
            }
            sceneView.in2DMode = true;
            sceneView.FrameSelected();
            sceneView.Repaint();
        }

        private static bool CanReplaceUntitledScene(Scene scene)
        {
            if (!scene.IsValid() || !string.IsNullOrEmpty(scene.path))
            {
                return false;
            }

            var roots = scene.GetRootGameObjects();
            if (roots.Length == 0)
            {
                return true;
            }
            return roots.All(root =>
                root.transform.childCount == 0
                && (
                    (root.name == "Main Camera" && root.GetComponent<Camera>() != null)
                    || (
                        root.name == "Directional Light"
                        && root.GetComponent<Light>() != null
                    )
                    || (
                        root.name == "Global Light 2D"
                        && root.GetComponents<Component>().Any(component =>
                            component != null && component.GetType().Name == "Light2D"
                        )
                    )
                )
            );
        }

        private static void BuildTileLayers(
            Transform root,
            PixelLabMapManifest manifest,
            TextAsset manifestAsset
        )
        {
            var parent = new GameObject("Tile Layers");
            parent.transform.SetParent(root, false);
            var tileCache = new Dictionary<string, PixelLabPaintTile>();
            var spriteCache = new Dictionary<string, Sprite>();

            var legacyLayers = manifest.legacyTerrain == null
                ? Array.Empty<PixelLabLegacyLayer>()
                : manifest.legacyTerrain.layers ?? Array.Empty<PixelLabLegacyLayer>();
            if (legacyLayers.Length > 0)
            {
                BuildSharedLegacyTerrainLayer(
                    parent.transform,
                    root.gameObject,
                    manifest,
                    manifestAsset,
                    tileCache,
                    spriteCache
                );
            }

            var projectionLayers = manifest.projectionLayers
                ?? Array.Empty<PixelLabProjectionLayer>();
            for (var index = 0; index < projectionLayers.Length; index++)
            {
                BuildProjectionTileLayers(
                    parent.transform,
                    root.gameObject,
                    manifest,
                    manifestAsset,
                    projectionLayers[index],
                    index,
                    tileCache,
                    spriteCache
                );
            }
            BuildBuildingKits(parent.transform, manifest, manifestAsset);
        }

        private static void BuildBuildingKits(
            Transform parent,
            PixelLabMapManifest manifest,
            TextAsset manifestAsset
        )
        {
            foreach (var kit in manifest.buildingKits ??
                Array.Empty<PixelLabBuildingKit>())
            {
                var controller = PixelLabBuildingImporterSupport.BuildNativeKit(
                    parent,
                    kit,
                    manifest.pixelsPerUnit,
                    manifest.mapId,
                    StableHash(manifestAsset.text + "|" + kit.id),
                    GeneratedDirectory
                );
                CreateBuildingPalette(kit, controller.NativeTiles);
            }
        }

        private static void BuildProjectionTileLayers(
            Transform parent,
            GameObject mapRoot,
            PixelLabMapManifest manifest,
            TextAsset manifestAsset,
            PixelLabProjectionLayer layer,
            int layerIndex,
            Dictionary<string, PixelLabPaintTile> tileCache,
            Dictionary<string, Sprite> spriteCache
        )
        {
            var directTiles = ProjectionTiles(
                layer,
                manifest.pixelsPerUnit,
                tileCache,
                spriteCache,
                manifest
            );
            var terrainBrushes = TerrainBrushes(
                layer.tileGroupId,
                layer.unityTileRules,
                directTiles,
                tileCache
            );
            // Keep the exact authored variants in the Tilemap. Some generated
            // projection maps intentionally place solid masks next to each
            // other, so reverse-engineering a shared vertex field at import
            // time is ambiguous and used to make the result depend on cell
            // iteration order. Painting still auto-connects: terrain cards
            // stamp a canonical source tile and PixelLabTileRenderer resolves
            // only that edited cell and its neighbours.
            var logicalTerrain = false;
            var cellsByHeight = new SortedDictionary<int, List<PixelLabCell>>();
            foreach (var cell in layer.cells ?? Array.Empty<PixelLabCell>())
            {
                List<PixelLabCell> cells;
                if (!cellsByHeight.TryGetValue(cell.layerY, out cells))
                {
                    cells = new List<PixelLabCell>();
                    cellsByHeight[cell.layerY] = cells;
                }
                cells.Add(cell);
            }
            if (cellsByHeight.Count == 0)
            {
                cellsByHeight[0] = new List<PixelLabCell>();
            }

            foreach (var pair in cellsByHeight)
            {
                var layerY = pair.Key;
                var layerLabel = string.IsNullOrEmpty(layer.tileGroupName)
                    ? string.IsNullOrEmpty(layer.tileGroupId) ? layer.gridKind : layer.tileGroupId
                    : layer.tileGroupName;
                var gridObject = new GameObject(
                    HeightLabel(layerY) + " - " + layerLabel
                );
                gridObject.transform.SetParent(parent, false);
                var grid = gridObject.AddComponent<Grid>();
                ConfigureProjectionGrid(grid, layer, layerY, manifest.pixelsPerUnit);
                var tilemapObject = CreateTilemapObject(gridObject.transform, gridObject.name, layer.gridKind);
                var nativeTilemapRenderer = tilemapObject.GetComponent<TilemapRenderer>();
                if (nativeTilemapRenderer != null)
                {
                    nativeTilemapRenderer.mode = TilemapRenderer.Mode.Individual;
                    nativeTilemapRenderer.sortOrder =
                        layer.gridKind == "iso" || layer.gridKind == "hex-flat-top"
                            ? TilemapRenderer.SortOrder.TopRight
                            : TilemapRenderer.SortOrder.TopLeft;
                    nativeTilemapRenderer.sortingOrder = (int)(layerY * 1000 + layerIndex * 10 + layer.drawOrderOffset);
                }
                var tilemap = tilemapObject.GetComponent<Tilemap>();
                CalibrateProjectionGridToPlacements(
                    grid,
                    tilemap,
                    layer,
                    layerY,
                    manifest.pixelsPerUnit,
                    directTiles
                );
                if (logicalTerrain)
                {
                    var terrainValues = layer.unityTileRules.ruleType == "corner"
                        ? InitialCornerTerrainValues(pair.Value, layer.unityTileRules)
                        : InitialEdgeTerrainValues(pair.Value, layer.unityTileRules);
                    foreach (var terrainValue in terrainValues)
                    {
                        PixelLabPaintTile brush;
                        if (terrainBrushes.TryGetValue(terrainValue.Value, out brush))
                        {
                            tilemap.SetTile(
                                PixelLabGridCoordinates.ToUnityCell(
                                    layer.gridKind,
                                    terrainValue.Key.x,
                                    terrainValue.Key.y
                                ),
                                brush
                            );
                        }
                    }
                }
                else
                {
                    foreach (var cell in pair.Value)
                    {
                        PixelLabPaintTile tile;
                        if (!directTiles.TryGetValue(cell.tileIndex, out tile))
                        {
                            continue;
                        }
                        tilemap.SetTile(
                            PixelLabGridCoordinates.ToUnityCell(
                                layer.gridKind,
                                cell.q,
                                cell.r
                            ),
                            tile
                        );
                    }
                }

                var autoPaintTiles = terrainBrushes.Values
                    .OrderBy(tile => tile.paintTerrain)
                    .ToList();
                var directPaletteTiles = directTiles.Values
                    .OrderBy(tile => tile.tileIndex)
                    .ToList();
                var availableTiles = autoPaintTiles
                    .Concat(directPaletteTiles)
                    .ToList();
                var renderer = ConfigureRenderer(
                    tilemapObject,
                    mapRoot,
                    manifestAsset,
                    layer.gridKind,
                    layerIndex,
                    layerY,
                    false,
                    manifest.pixelsPerUnit,
                    availableTiles,
                    logicalTerrain
                );
                renderer.paletteAssetPath = CreatePalette(
                    layerLabel + " Y" + layerY + " Auto Paint",
                    grid,
                    autoPaintTiles.Count > 0 ? autoPaintTiles : directPaletteTiles,
                    true
                );
                renderer.advancedPaletteAssetPath = CreatePalette(
                    layerLabel + " Y" + layerY + " Advanced Variants",
                    grid,
                    directPaletteTiles,
                    true,
                    // Every projected palette needs the projection-aware
                    // placement.  Compacting Iso sprites into a rectangular
                    // selection palette clips/omits variants whose sprite
                    // bounds extend beyond a rectangle cell; Hex already uses
                    // this collision-aware layout.
                    false
                );
            }
        }

        private static Dictionary<Vector2Int, int> InitialCornerTerrainValues(
            IEnumerable<PixelLabCell> cells,
            PixelLabUnityTileRules rules
        )
        {
            var values = new Dictionary<Vector2Int, int>();
            foreach (var cell in cells)
            {
                var entry = RuleEntry(rules, cell.tileIndex);
                if (entry == null || !entry.hasMask)
                {
                    continue;
                }
                var origin = new Vector2Int(cell.q, cell.r);
                values[origin] = CornerTerrainFromBit((entry.mask >> 3) & 1);
                values[origin + new Vector2Int(1, 0)] = CornerTerrainFromBit(
                    (entry.mask >> 2) & 1
                );
                values[origin + new Vector2Int(0, 1)] = CornerTerrainFromBit(
                    (entry.mask >> 1) & 1
                );
                values[origin + new Vector2Int(1, 1)] = CornerTerrainFromBit(
                    entry.mask & 1
                );
            }
            return values;
        }

        private static Dictionary<Vector2Int, int> InitialEdgeTerrainValues(
            IEnumerable<PixelLabCell> cells,
            PixelLabUnityTileRules rules
        )
        {
            var values = new Dictionary<Vector2Int, int>();
            foreach (var cell in cells)
            {
                var entry = RuleEntry(rules, cell.tileIndex);
                var feature = entry != null
                    && !(rules.connectivity == "same" && entry.hasMask && entry.mask == 0);
                values[new Vector2Int(cell.q, cell.r)] = feature ? 0 : 1;
            }
            return values;
        }

        private static int CornerTerrainFromBit(int bit)
        {
            return bit == 1 ? 0 : 1;
        }

        private static void BuildSharedLegacyTerrainLayer(
            Transform parent,
            GameObject mapRoot,
            PixelLabMapManifest manifest,
            TextAsset manifestAsset,
            Dictionary<string, PixelLabPaintTile> tileCache,
            Dictionary<string, Sprite> spriteCache
        )
        {
            var tileSize = manifest.legacyTerrain.tileSize;
            var layers = manifest.legacyTerrain.layers
                ?? Array.Empty<PixelLabLegacyLayer>();
            var directTilesByLayer = new List<Dictionary<int, PixelLabPaintTile>>();
            var allDirectTiles = new List<PixelLabPaintTile>();
            for (var index = 0; index < layers.Length; index++)
            {
                var directTiles = LegacyTiles(
                    layers[index],
                    tileSize,
                    manifest.pixelsPerUnit,
                    tileCache,
                    spriteCache,
                    manifest
                );
                foreach (var tile in directTiles.Values)
                {
                    tile.sourceLayerIndex = index;
                    EditorUtility.SetDirty(tile);
                }
                directTilesByLayer.Add(directTiles);
                allDirectTiles.AddRange(directTiles.Values.OrderBy(tile => tile.tileIndex));
            }

            var terrainBrushes = SharedLegacyTerrainBrushes(
                manifest.legacyTerrain,
                directTilesByLayer,
                tileCache
            );
            var gridObject = new GameObject("Y0 - GROUND - Terrain Map");
            gridObject.transform.SetParent(parent, false);
            var grid = gridObject.AddComponent<Grid>();
            ConfigureLegacyGrid(grid, tileSize, manifest.pixelsPerUnit);
            var tilemapObject = CreateTilemapObject(gridObject.transform, gridObject.name);
            var tilemap = tilemapObject.GetComponent<Tilemap>();
            for (var index = 0; index < layers.Length; index++)
            {
                var directTiles = directTilesByLayer[index];
                foreach (var placement in layers[index].placements ?? Array.Empty<PixelLabPlacement>())
                {
                    PixelLabPaintTile tile;
                    if (directTiles.TryGetValue(placement.tileIndex, out tile))
                    {
                        tilemap.SetTile(
                            PixelLabGridCoordinates.ToUnityCell(
                                "square-topdown",
                                placement.q,
                                placement.r
                            ),
                            tile
                        );
                    }
                }
            }

            var autoPaintTiles = terrainBrushes.Values
                .Where(tile => tile.paintableTerrain)
                .OrderBy(tile => tile.paintTerrain)
                .ToList();
            var hiddenTerrainTiles = terrainBrushes.Values
                .Where(tile => !tile.paintableTerrain)
                .ToList();
            var renderer = ConfigureRenderer(
                tilemapObject,
                mapRoot,
                manifestAsset,
                "square-topdown",
                -1,
                0,
                true,
                manifest.pixelsPerUnit,
                autoPaintTiles.Concat(hiddenTerrainTiles).Concat(allDirectTiles).ToList()
            );
            renderer.legacyShared = true;
            renderer.paletteAssetPath = CreatePalette(
                "Y0 Terrain Auto Paint",
                grid,
                autoPaintTiles
            );
            renderer.advancedPaletteAssetPath = string.Empty;
            renderer.Reload();
        }

        private static Dictionary<int, PixelLabPaintTile> ProjectionTiles(
            PixelLabProjectionLayer layer,
            float pixelsPerUnit,
            Dictionary<string, PixelLabPaintTile> tileCache,
            Dictionary<string, Sprite> spriteCache,
            PixelLabMapManifest manifest = null
        )
        {
            var output = new Dictionary<int, PixelLabPaintTile>();
            foreach (var asset in layer.tileAssets ?? Array.Empty<PixelLabTileAsset>())
            {
                output[asset.tileIndex] = PaintTileAt(
                    new PixelLabPlacement {
                        tileIndex = asset.tileIndex,
                        assetPath = asset.assetPath,
                    },
                    -1,
                    pixelsPerUnit,
                    tileCache,
                    spriteCache,
                    manifest
                );
            }
            foreach (var placement in layer.placements
                ?? Array.Empty<PixelLabPlacement>())
            {
                if (!output.ContainsKey(placement.tileIndex))
                {
                    output[placement.tileIndex] = PaintTileAt(
                        placement,
                        -1,
                        pixelsPerUnit,
                        tileCache,
                        spriteCache,
                        manifest
                    );
                }
            }
            return output;
        }

        private static Dictionary<int, PixelLabPaintTile> LegacyTiles(
            PixelLabLegacyLayer layer,
            PixelLabSize tileSize,
            float pixelsPerUnit,
            Dictionary<string, PixelLabPaintTile> tileCache,
            Dictionary<string, Sprite> spriteCache,
            PixelLabMapManifest manifest = null
        )
        {
            var output = new Dictionary<int, PixelLabPaintTile>();
            var indices = layer.atlasTiles == null || layer.atlasTiles.Length == 0
                ? Enumerable.Range(0, layer.atlasColumns * layer.atlasRows)
                : layer.atlasTiles.AsEnumerable();
            foreach (var tileIndex in indices)
            {
                var placement = new PixelLabPlacement {
                    tileIndex = tileIndex,
                    assetPath = layer.image,
                    sourceRect = new PixelLabSourceRect {
                        x = tileIndex % layer.atlasColumns * tileSize.width,
                        y = tileIndex / layer.atlasColumns * tileSize.height,
                        width = tileSize.width,
                        height = tileSize.height,
                    },
                };
                output[tileIndex] = PaintTileAt(
                    placement,
                    -1,
                    pixelsPerUnit,
                    tileCache,
                    spriteCache,
                    manifest
                );
            }
            return output;
        }

        private static Dictionary<int, PixelLabPaintTile> TerrainBrushes(
            string layerId,
            PixelLabUnityTileRules rules,
            Dictionary<int, PixelLabPaintTile> directTiles,
            Dictionary<string, PixelLabPaintTile> tileCache
        )
        {
            var output = new Dictionary<int, PixelLabPaintTile>();
            if (!SupportsTerrainPainting(rules, directTiles))
            {
                return output;
            }
            var terrainCount = Mathf.Min(2, rules.terrains == null ? 0 : rules.terrains.Length);
            for (var terrain = 0; terrain < terrainCount; terrain++)
            {
                var tileIndex = CanonicalTerrainTile(rules, directTiles, terrain);
                PixelLabPaintTile direct;
                if (tileIndex < 0 || !directTiles.TryGetValue(tileIndex, out direct))
                {
                    direct = directTiles.Values.FirstOrDefault();
                    if (direct == null)
                    {
                        continue;
                    }
                    tileIndex = direct.tileIndex;
                }

                var key = "terrain-brush|" + layerId + "|" + terrain + "|" + tileIndex;
                var terrainName = rules.terrains != null
                    && terrain < rules.terrains.Length
                        ? rules.terrains[terrain]
                        : "Terrain " + terrain;
                var brushName = "AUTO PAINT - " + terrainName;
                PixelLabPaintTile brush;
                string path = null;
                var createAsset = false;
                if (!tileCache.TryGetValue(key, out brush))
                {
                    path = TerrainBrushAssetPath(key, brushName);
                    brush = AssetDatabase.LoadAssetAtPath<PixelLabPaintTile>(path);
                    createAsset = brush == null;
                    brush = createAsset
                        ? ScriptableObject.CreateInstance<PixelLabPaintTile>()
                        : brush;
                    tileCache[key] = brush;
                }
                brush.name = CleanName(brushName);
                brush.tileIndex = tileIndex;
                brush.paintTerrain = terrain;
                brush.paintableTerrain = true;
                brush.assetPath = direct.assetPath;
                brush.sprite = direct.sprite;
                brush.colliderType = Tile.ColliderType.None;
                brush.flags = TileFlags.LockTransform;
                if (createAsset)
                {
                    AssetDatabase.CreateAsset(brush, path);
                }
                else
                {
                    EditorUtility.SetDirty(brush);
                }
                output[terrain] = brush;
            }
            return output;
        }

        private static bool SupportsTerrainPainting(
            PixelLabUnityTileRules rules,
            Dictionary<int, PixelLabPaintTile> directTiles
        )
        {
            if (rules == null
                || (rules.terrains?.Length ?? 0) < 2
                || !(rules.ruleType == "corner" || rules.ruleType == "edge")
                || rules.arity <= 0)
            {
                return false;
            }

            var expectedMaskCount = 1 << rules.arity;
            var coveredMasks = new HashSet<int>();
            foreach (var entry in rules.entries ?? Array.Empty<PixelLabRuleEntry>())
            {
                var masks = entry.masks != null && entry.masks.Length > 0
                    ? entry.masks
                    : entry.hasMask ? new[] { entry.mask } : Array.Empty<int>();
                foreach (var mask in masks)
                {
                    if (mask >= 0 && mask < expectedMaskCount)
                    {
                        coveredMasks.Add(mask);
                    }
                }
            }
            if (coveredMasks.Count != expectedMaskCount)
            {
                // Hex coastline sets intentionally omit mask 0b111111: that
                // slot is the direct open-water filler, not a transition
                // sprite. The web editor uses the same compact 31-mask table
                // and resolves non-contiguous requests to the nearest arc.
                var compactHexMasks = new HashSet<int> { 0 };
                for (var arcLength = 1; arcLength < 6; arcLength++)
                {
                    for (var start = 0; start < 6; start++)
                    {
                        var arcMask = 0;
                        for (var offset = 0; offset < arcLength; offset++)
                        {
                            arcMask |= 1 << ((start + offset) % 6);
                        }
                        compactHexMasks.Add(arcMask);
                    }
                }
                var ruledIndices = new HashSet<int>(
                    (rules.entries ?? Array.Empty<PixelLabRuleEntry>())
                        .Select(entry => entry.tileIndex)
                );
                var hasDirectFiller = directTiles.Keys.Any(
                    tileIndex => !ruledIndices.Contains(tileIndex)
                );
                var intentionalHexFillerGap = rules.ruleType == "edge"
                    && rules.arity == 6
                    && rules.connectivity == "other"
                    && coveredMasks.SetEquals(compactHexMasks)
                    && hasDirectFiller;
                if (!intentionalHexFillerGap)
                {
                    return false;
                }
            }

            for (var terrain = 0; terrain < 2; terrain++)
            {
                var tileIndex = CanonicalTerrainTile(rules, directTiles, terrain);
                if (tileIndex < 0 || !directTiles.ContainsKey(tileIndex))
                {
                    return false;
                }
            }
            return true;
        }

        private static Dictionary<int, PixelLabPaintTile> SharedLegacyTerrainBrushes(
            PixelLabLegacyTerrain terrainMap,
            List<Dictionary<int, PixelLabPaintTile>> directTilesByLayer,
            Dictionary<string, PixelLabPaintTile> tileCache
        )
        {
            var output = new Dictionary<int, PixelLabPaintTile>();
            var layers = terrainMap.layers ?? Array.Empty<PixelLabLegacyLayer>();
            foreach (var terrain in terrainMap.terrains
                ?? Array.Empty<PixelLabTerrainDefinition>())
            {
                for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
                {
                    var rules = layers[layerIndex].unityTileRules;
                    var localTerrain = Array.IndexOf(
                        rules?.terrainIds ?? Array.Empty<int>(),
                        terrain.id
                    );
                    if (localTerrain < 0)
                    {
                        continue;
                    }
                    var tileIndex = CanonicalTerrainTile(
                        rules,
                        directTilesByLayer[layerIndex],
                        localTerrain
                    );
                    PixelLabPaintTile direct;
                    if (tileIndex < 0 || !directTilesByLayer[layerIndex].TryGetValue(
                        tileIndex,
                        out direct
                    ))
                    {
                        direct = directTilesByLayer[layerIndex].Values.FirstOrDefault();
                    }
                    if (direct == null)
                    {
                        continue;
                    }

                    var key = "shared-terrain-brush|" + terrain.id + "|"
                        + direct.assetPath + "|" + direct.tileIndex;
                    var brushName = (terrain.isTransition
                        ? "AUTO TRANSITION - "
                        : "AUTO PAINT - ") + terrain.name;
                    PixelLabPaintTile brush;
                    string path = null;
                    var createAsset = false;
                    if (!tileCache.TryGetValue(key, out brush))
                    {
                        path = TerrainBrushAssetPath(key, brushName);
                        brush = AssetDatabase.LoadAssetAtPath<PixelLabPaintTile>(path);
                        createAsset = brush == null;
                        brush = createAsset
                            ? ScriptableObject.CreateInstance<PixelLabPaintTile>()
                            : brush;
                        tileCache[key] = brush;
                    }
                    brush.name = CleanName(brushName);
                    brush.tileIndex = direct.tileIndex;
                    brush.paintTerrain = terrain.id;
                    brush.paintableTerrain = !terrain.isTransition;
                    brush.sourceLayerIndex = -1;
                    brush.assetPath = direct.assetPath;
                    brush.sprite = direct.sprite;
                    brush.colliderType = Tile.ColliderType.None;
                    brush.flags = TileFlags.LockTransform;
                    if (createAsset)
                    {
                        AssetDatabase.CreateAsset(brush, path);
                    }
                    else
                    {
                        EditorUtility.SetDirty(brush);
                    }
                    output[terrain.id] = brush;
                    break;
                }
            }
            return output;
        }

        private static int CanonicalTerrainTile(
            PixelLabUnityTileRules rules,
            Dictionary<int, PixelLabPaintTile> directTiles,
            int terrain
        )
        {
            if (rules.ruleType == "pattern_4x4")
            {
                return CanonicalPatternTile(rules, terrain);
            }

            if (rules.ruleType == "corner" && rules.arity > 0)
            {
                // Corner groups publish terrains upper-first, matching the
                // map editor's [mask 15, mask 0] palette order.
                var targetMask = terrain == 0 ? (1 << rules.arity) - 1 : 0;
                var entry = (rules.entries ?? Array.Empty<PixelLabRuleEntry>())
                    .FirstOrDefault(item => item.hasMask && item.mask == targetMask);
                return entry == null ? -1 : entry.tileIndex;
            }

            if (rules.ruleType == "edge" && rules.arity > 0)
            {
                if (terrain == 1)
                {
                    var ruledIndices = new HashSet<int>(
                        (rules.entries ?? Array.Empty<PixelLabRuleEntry>())
                            .Select(entry => entry.tileIndex)
                    );
                    foreach (var tileIndex in directTiles.Keys.OrderBy(index => index))
                    {
                        if (!ruledIndices.Contains(tileIndex))
                        {
                            return tileIndex;
                        }
                    }
                    return -1;
                }
                var targetMask = rules.connectivity == "same"
                    ? (1 << rules.arity) - 1
                    : 0;
                var entry = (rules.entries ?? Array.Empty<PixelLabRuleEntry>())
                    .FirstOrDefault(item => item.hasMask && item.mask == targetMask);
                return entry == null ? -1 : entry.tileIndex;
            }
            return -1;
        }

        private static Dictionary<Vector2Int, int> InitialPatternValues(
            PixelLabLegacyLayer layer
        )
        {
            var values = new Dictionary<Vector2Int, int>();
            if (layer.paintValues != null && layer.paintValues.Length > 0)
            {
                foreach (var item in layer.paintValues)
                {
                    values[new Vector2Int(item.x, item.y)] = item.value;
                }
                return values;
            }

            foreach (var placement in layer.placements
                ?? Array.Empty<PixelLabPlacement>())
            {
                var entry = RuleEntry(layer.unityTileRules, placement.tileIndex);
                if (entry == null || entry.pattern == null || entry.pattern.Length != 16)
                {
                    continue;
                }
                var cell = new Vector2Int(placement.q, placement.r);
                values[cell] = BasePatternValue(layer.unityTileRules, entry.pattern[5]);
                values[cell + new Vector2Int(1, 0)] = BasePatternValue(
                    layer.unityTileRules,
                    entry.pattern[6]
                );
                values[cell + new Vector2Int(0, 1)] = BasePatternValue(
                    layer.unityTileRules,
                    entry.pattern[9]
                );
                values[cell + new Vector2Int(1, 1)] = BasePatternValue(
                    layer.unityTileRules,
                    entry.pattern[10]
                );
            }
            return values;
        }

        private static int CanonicalPatternTile(
            PixelLabUnityTileRules rules,
            int terrain
        )
        {
            foreach (var entry in rules.entries ?? Array.Empty<PixelLabRuleEntry>())
            {
                var pattern = entry.pattern;
                if (pattern != null && pattern.Length == 16
                    && pattern[5] == terrain && pattern[6] == terrain
                    && pattern[9] == terrain && pattern[10] == terrain)
                {
                    return entry.tileIndex;
                }
            }
            return -1;
        }

        private static PixelLabRuleEntry RuleEntry(
            PixelLabUnityTileRules rules,
            int tileIndex
        )
        {
            if (rules == null)
            {
                return null;
            }
            foreach (var entry in rules.entries ?? Array.Empty<PixelLabRuleEntry>())
            {
                if (entry.tileIndex == tileIndex)
                {
                    return entry;
                }
            }
            return null;
        }

        private static int BasePatternValue(PixelLabUnityTileRules rules, int value)
        {
            return rules.terrains != null && rules.terrains.Length > 2 && value == 2
                ? 0
                : value;
        }

        private static PixelLabPaintTile PaintTileAt(
            PixelLabPlacement placement,
            int paintTerrain,
            float pixelsPerUnit,
            Dictionary<string, PixelLabPaintTile> tileCache,
            Dictionary<string, Sprite> spriteCache,
            PixelLabMapManifest manifest = null
        )
        {
            var sourceRect = placement.sourceRect;
            var key = string.Join(
                "|",
                placement.assetPath,
                placement.tileIndex,
                paintTerrain,
                sourceRect == null ? 0f : sourceRect.x,
                sourceRect == null ? 0f : sourceRect.y,
                sourceRect == null ? 0f : sourceRect.width,
                sourceRect == null ? 0f : sourceRect.height,
                pixelsPerUnit
            );
            PixelLabPaintTile cached;
            if (tileCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var path = GeneratedTileDirectory + "/Tile_" + StableHash(key) + ".asset";
            var tile = AssetDatabase.LoadAssetAtPath<PixelLabPaintTile>(path);
            var createAsset = tile == null;
            tile = createAsset
                ? ScriptableObject.CreateInstance<PixelLabPaintTile>()
                : tile;
            tile.name = "PixelLab Tile " + placement.tileIndex;
            tile.tileIndex = placement.tileIndex;
            tile.paintTerrain = paintTerrain;
            tile.paintableTerrain = true;
            tile.sourceLayerIndex = -1;
            tile.assetPath = placement.assetPath;
            tile.sprite = SpriteAt(placement, pixelsPerUnit, spriteCache, manifest);
            tile.colliderType = Tile.ColliderType.None;
            tile.flags = TileFlags.LockTransform;
            if (createAsset)
            {
                AssetDatabase.CreateAsset(tile, path);
            }
            else
            {
                EditorUtility.SetDirty(tile);
            }
            tileCache[key] = tile;
            return tile;
        }

        private static GameObject CreateTilemapObject(Transform parent, string name, string gridKind = null)
        {
            var tilemapObject = new GameObject("PAINT HERE - " + name);
            tilemapObject.transform.SetParent(parent, false);
            var tilemap = tilemapObject.AddComponent<Tilemap>();
            if (gridKind == "hex-flat-top")
            {
                // Unity's YXZ-swizzled Hexagon interpolation changes its
                // half-column stagger when cell.y crosses below zero. The
                // exporter uses the editor's linear q/r lattice instead, so
                // anchor from the stable X axis and let PaintTile's parity
                // correction place the sprite at the cell centre. Keeping
                // anchor.y at zero avoids the one-sided negative-q seam when
                // a terrain brush grows beyond the authored map bounds.
                tilemap.tileAnchor = new Vector3(0.5f, 0f, 0f);
            }
            var renderer = tilemapObject.AddComponent<TilemapRenderer>();
            renderer.mode = TilemapRenderer.Mode.Individual;
            renderer.sortOrder = gridKind == "iso" || gridKind == "hex-flat-top"
                ? TilemapRenderer.SortOrder.TopRight
                : TilemapRenderer.SortOrder.TopLeft;
            renderer.enabled = true;
            return tilemapObject;
        }

        private static PixelLabTileRenderer ConfigureRenderer(
            GameObject tilemapObject,
            GameObject mapRoot,
            TextAsset manifestAsset,
            string gridKind,
            int layerIndex,
            int layerY,
            bool legacy,
            float pixelsPerUnit,
            List<PixelLabPaintTile> availableTiles,
            bool logicalProjectionTerrain = false
        )
        {
            var renderer = tilemapObject.AddComponent<PixelLabTileRenderer>();
            renderer.manifestAsset = manifestAsset;
            renderer.mapRoot = mapRoot.transform;
            renderer.gridKind = gridKind;
            renderer.layerIndex = layerIndex;
            renderer.layerY = layerY;
            renderer.legacy = legacy;
            renderer.logicalProjectionTerrain = logicalProjectionTerrain;
            renderer.pixelsPerUnit = pixelsPerUnit;
            renderer.availableTiles = availableTiles
                .Where(tile => tile != null)
                .Distinct()
                .ToArray();
            renderer.Reload();
            return renderer;
        }

        private static void ConfigureLegacyGrid(
            Grid grid,
            PixelLabSize tileSize,
            float pixelsPerUnit
        )
        {
            var safePpu = Mathf.Max(1f, pixelsPerUnit);
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;
            grid.cellGap = Vector3.zero;
            grid.cellSize = new Vector3(
                tileSize.width / safePpu,
                tileSize.height / safePpu,
                1f
            );
            var originCell = PixelLabGridCoordinates.ToUnityCell(
                "square-topdown",
                0,
                0
            );
            var nativeOrigin = grid.GetCellCenterLocal(originCell);
            // DUAL GRID: terrain values live at cell corners and tile (q, r)
            // renders at (q - 0.5, r - 0.5) * tileSize — the same half-tile
            // shift the Tiled export carries as offsetx/offsety and Godot as
            // the layer position. Anchoring cell (0,0)'s CENTRE at the world
            // origin puts its top-left at (-w/2, -h/2), exactly that shift.
            // The old anchor (centre at (w/2, -h/2)) dropped it, which pushed
            // terrain half a tile down-right relative to objects and inpaint
            // overlays — Nikola's "objects offset up on the Y axis" report.
            grid.transform.localPosition = Vector3.zero - nativeOrigin;
        }

        private static void ConfigureProjectionGrid(
            Grid grid,
            PixelLabProjectionLayer layer,
            int layerY,
            float pixelsPerUnit
        )
        {
            grid.cellGap = Vector3.zero;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;
            switch (layer.gridKind)
            {
                case "iso":
                    grid.cellLayout = GridLayout.CellLayout.Isometric;
                    break;
                case "hex-flat-top":
                    grid.cellLayout = GridLayout.CellLayout.Hexagon;
                    grid.cellSwizzle = GridLayout.CellSwizzle.YXZ;
                    break;
                case "hex-pointy-top":
                    grid.cellLayout = GridLayout.CellLayout.Hexagon;
                    break;
                default:
                    grid.cellLayout = GridLayout.CellLayout.Rectangle;
                    break;
            }

            // Cell size is ANALYTIC, taken from the manifest's own basis
            // vectors — the exact lattice the map editor painted on. The old
            // code probed Unity's unit grid and scaled it per-axis
            // (magnitude-only), which cannot express the hex stagger and
            // silently approximated everything else.
            var safePpu = Mathf.Max(1f, pixelsPerUnit);
            var projection = layer.projection;
            var qBasis = projection.qBasis;
            var rBasis = projection.rBasis;
            switch (layer.gridKind)
            {
                case "iso":
                    // qBasis = (vx/2, vy/2): Unity iso steps sx/2, sy/2 per cell.
                    grid.cellSize = new Vector3(
                        Mathf.Abs(qBasis.x) * 2f / safePpu,
                        Mathf.Abs(qBasis.y) * 2f / safePpu,
                        1f
                    );
                    break;
                case "hex-flat-top":
                    // Column pitch (qBasis.x) is 3/4 of the swizzled cell
                    // height; the row pitch (rBasis.y) is the swizzled width.
                    grid.cellSize = new Vector3(
                        Mathf.Abs(rBasis.y) / safePpu,
                        Mathf.Abs(qBasis.x) / 0.75f / safePpu,
                        1f
                    );
                    break;
                case "hex-pointy-top":
                    grid.cellSize = new Vector3(
                        Mathf.Abs(qBasis.x) / safePpu,
                        Mathf.Abs(rBasis.y) / 0.75f / safePpu,
                        1f
                    );
                    break;
                default:
                    grid.cellSize = new Vector3(
                        Mathf.Abs(qBasis.x) / safePpu,
                        Mathf.Abs(rBasis.y) / safePpu,
                        1f
                    );
                    break;
            }

            var originCell = PixelLabGridCoordinates.ToUnityCell(layer.gridKind, 0, 0);
            var nativeOrigin = grid.GetCellCenterLocal(originCell);
            var editorOrigin = projection.editorOrigin ?? projection.origin;
            var desiredOrigin = PixelPosition(editorOrigin.x, editorOrigin.y, safePpu);
            if (projection.stackBasis != null)
            {
                desiredOrigin += PixelVector(projection.stackBasis, safePpu) * layerY;
            }
            grid.transform.localPosition = desiredOrigin - nativeOrigin;
        }

        private static void CalibrateProjectionGridToPlacements(
            Grid grid,
            Tilemap tilemap,
            PixelLabProjectionLayer layer,
            int layerY,
            float pixelsPerUnit,
            Dictionary<int, PixelLabPaintTile> directTiles
        )
        {
            // The manifest's resolved xPx/yPx is ground truth: it is where the
            // map editor drew each sprite, and what the Tiled and Godot
            // exports consume. Anchoring the grid so a reference placement
            // reproduces its own xPx/yPx removes every per-kind anchor
            // convention (top-face centre vs sprite bottom vs pick point) in
            // one stroke. editorOrigin above remains only the fallback for
            // layers with no placements.
            if (layer.placements == null)
            {
                return;
            }
            // Hex included: PixelLabPaintTile.GetTileData shifts every hex
            // sprite from the interpolated anchor back to GetCellCenterLocal
            // (the stagger restoration), so the centre-based prediction below
            // is exact for the Hexagon layout too. editorOrigin anchoring
            // alone left the hex grid at a FRACTIONAL world position
            // (measured: (+23.5, -11.49) px on the real coastline map), and a
            // half-pixel x made every quad point-sample on texel boundaries —
            // per-quad tie-breaks ate/duplicated silhouette columns.
            var safePpu = Mathf.Max(1f, pixelsPerUnit);
            foreach (var placement in layer.placements)
            {
                if (placement.layerY != layerY)
                {
                    continue;
                }
                if (placement.sourceRect != null
                    && placement.sourceRect.height > 0f
                    && placement.sourceHeight > 0f
                    && placement.sourceRect.height < placement.sourceHeight)
                {
                    // clipped by a stacked neighbour — its xPx describes a
                    // partial sprite, not the full tile Unity draws. The
                    // sourceRect.height > 0 term matters: JsonUtility gives
                    // absent sourceRects a DEFAULT instance (height 0), which
                    // would otherwise skip every placement and silently
                    // disable this calibration.
                    continue;
                }
                PixelLabPaintTile tile;
                if (!directTiles.TryGetValue(placement.tileIndex, out tile)
                    || tile.sprite == null)
                {
                    continue;
                }
                var sprite = tile.sprite;
                var cell = PixelLabGridCoordinates.ToUnityCell(
                    layer.gridKind,
                    placement.q,
                    placement.r
                );
                // Through the TILEMAP, not the Grid: under the Hexagon
                // layout's YXZ swizzle Grid.GetCellCenterLocal and
                // Tilemap.GetCellCenterLocal disagree, and the renderer's
                // stagger restoration (GetTileData) anchors sprites at the
                // TILEMAP's centre. They coincide for unswizzled layouts.
                var pivotWorld = grid.transform.localPosition
                    + tilemap.GetCellCenterLocal(cell);
                var predictedTopLeft = new Vector2(
                    pivotWorld.x - sprite.pivot.x / safePpu,
                    pivotWorld.y + (sprite.rect.height - sprite.pivot.y) / safePpu
                );
                var truth = PixelPosition(placement.xPx, placement.yPx, safePpu);
                var correction = new Vector3(
                    truth.x - predictedTopLeft.x,
                    truth.y - predictedTopLeft.y,
                    0f
                );
                grid.transform.localPosition += correction;
                return;
            }
        }

        private static string CreatePalette(
            string label,
            Grid sourceGrid,
            List<PixelLabPaintTile> tiles,
            bool organizeProjectionTiles = false,
            bool compactSelectionPalette = false
        )
        {
            if (tiles.Count == 0)
            {
                return string.Empty;
            }
            var sprites = tiles.Select(tile => tile.sprite).ToArray();
            var paletteLayout = compactSelectionPalette
                ? GridLayout.CellLayout.Rectangle
                : sourceGrid.cellLayout;
            var paletteCellSize = compactSelectionPalette
                ? SelectionPaletteCellSize(sprites)
                : sourceGrid.cellSize;
            var paletteSwizzle = compactSelectionPalette
                ? GridLayout.CellSwizzle.XYZ
                : sourceGrid.cellSwizzle;
            var palette = CreatePaletteAsset(
                label,
                paletteLayout,
                paletteCellSize,
                paletteSwizzle
            );
            var tilemap = palette.GetComponentInChildren<Tilemap>();
            if (tilemap == null)
            {
                throw new InvalidOperationException(
                    "The generated PixelLab palette has no Tilemap."
                );
            }
            var paletteGrid = palette.GetComponent<Grid>();
            if (paletteGrid == null)
            {
                throw new InvalidOperationException(
                    "The generated PixelLab palette has no Grid."
                );
            }
            var paletteCells = compactSelectionPalette
                ? CompactSelectionPaletteCells(paletteGrid, tilemap, sprites)
                : organizeProjectionTiles
                    ? ProjectionPaletteCells(paletteGrid, tilemap, tiles)
                    : tiles.Select((_, index) =>
                        new Vector3Int(index % 8, -(index / 8), 0)
                    ).ToList();
            for (var index = 0; index < tiles.Count; index++)
            {
                tilemap.SetTile(paletteCells[index], tiles[index]);
            }
            SavePalette(palette, tilemap);
            return AssetDatabase.GetAssetPath(palette);
        }

        private static string CreateBuildingPalette(
            PixelLabBuildingKit kit,
            IReadOnlyList<PixelLabBuildingNativeTile> sourceTiles
        )
        {
            var tiles = sourceTiles
                .Where(tile => tile != null)
                .OrderBy(tile => tile.tileIndex)
                .ToArray();
            if (tiles.Length == 0)
            {
                return string.Empty;
            }
            var sprites = tiles.Select(tile => tile.sprite).ToArray();
            var palette = CreatePaletteAsset(
                BuildingPaletteLabel(kit),
                GridLayout.CellLayout.Rectangle,
                SelectionPaletteCellSize(sprites),
                GridLayout.CellSwizzle.XYZ
            );
            var tilemap = palette.GetComponentInChildren<Tilemap>();
            var paletteGrid = palette.GetComponent<Grid>();
            if (tilemap == null || paletteGrid == null)
            {
                throw new InvalidOperationException(
                    "The generated PixelLab Buildings palette is incomplete."
                );
            }
            var cellSize = paletteGrid.cellSize;
            tilemap.tileAnchor = paletteGrid.LocalToCellInterpolated(new Vector3(
                PaletteCardPadding * 0.5f,
                cellSize.y - PaletteCardPadding * 0.5f,
                0f
            ));
            var columns = Mathf.Min(8, Mathf.CeilToInt(Mathf.Sqrt(tiles.Length)));
            for (var index = 0; index < tiles.Length; index++)
            {
                tilemap.SetTile(
                    new Vector3Int(index % columns, -(index / columns), 0),
                    tiles[index]
                );
            }
            SavePalette(palette, tilemap);
            return AssetDatabase.GetAssetPath(palette);
        }

        private static GameObject CreatePaletteAsset(
            string label,
            GridLayout.CellLayout layout,
            Vector3 cellSize,
            GridLayout.CellSwizzle swizzle
        )
        {
            var name = CleanName(label) + " Palette";
            var utilityType = FindEditorType("UnityEditor.Tilemaps.GridPaletteUtility");
            if (utilityType == null)
            {
                throw new InvalidOperationException(
                    "Unity's 2D Tilemap Editor package is not available. "
                    + "Install it from Window > Package Manager > Unity Registry "
                    + "> 2D Tilemap Editor (com.unity.2d.tilemap), then run "
                    + "Tools > PixelLab > Rebuild Map. Projects made from a 2D "
                    + "template already have it."
                );
            }
            var method = utilityType.GetMethods()
                .FirstOrDefault(candidate =>
                    candidate.Name == "CreateNewPalette"
                    && candidate.GetParameters().Length == 6
                );
            if (method == null)
            {
                throw new InvalidOperationException(
                    "Unity's Tile Palette creation API is not available."
                );
            }
            var cellSizingType = method.GetParameters()[3].ParameterType;
            var manualCellSizing = Enum.Parse(cellSizingType, "Manual");
            var palette = method.Invoke(
                null,
                new object[] {
                    PaletteDirectory,
                    name,
                    layout,
                    manualCellSizing,
                    cellSize,
                    swizzle,
                }
            ) as GameObject;
            if (palette == null)
            {
                throw new InvalidOperationException(
                    "Unity could not create the PixelLab palette " + name
                );
            }
            return palette;
        }

        private static void SavePalette(GameObject palette, Tilemap tilemap)
        {
            EditorUtility.SetDirty(tilemap);
            if (PrefabUtility.IsPartOfPrefabAsset(palette))
            {
                PrefabUtility.SavePrefabAsset(palette);
            }
        }

        private static Vector3 SelectionPaletteCellSize(IEnumerable<Sprite> sprites)
        {
            var available = sprites.Where(sprite => sprite != null).ToArray();
            if (available.Length == 0)
            {
                throw new InvalidOperationException(
                    "A PixelLab selection palette tile has no sprite."
                );
            }
            return new Vector3(
                available.Max(sprite => sprite.bounds.size.x) + PaletteCardPadding,
                available.Max(sprite => sprite.bounds.size.y) + PaletteCardPadding,
                1f
            );
        }

        private static List<Vector3Int> CompactSelectionPaletteCells(
            Grid paletteGrid,
            Tilemap tilemap,
            IReadOnlyList<Sprite> sprites
        )
        {
            if (sprites.Any(sprite => sprite == null))
            {
                throw new InvalidOperationException(
                    "A PixelLab selection palette tile has no sprite."
                );
            }
            var spriteCenter = sprites
                .Select(sprite => sprite.bounds.center)
                .Aggregate(Vector3.zero, (sum, center) => sum + center)
                / sprites.Count;
            tilemap.tileAnchor = paletteGrid.LocalToCellInterpolated(
                paletteGrid.GetCellCenterLocal(Vector3Int.zero) - spriteCenter
            );
            var columns = Mathf.Min(8, Mathf.CeilToInt(Mathf.Sqrt(sprites.Count)));
            return sprites.Select((_, index) =>
                new Vector3Int(index % columns, -(index / columns), 0)
            ).ToList();
        }

        private static string BuildingPaletteLabel(PixelLabBuildingKit kit)
        {
            return "Buildings Individual Tiles " + kit.id + " " + kit.name;
        }

        private static string BuildingPaletteAssetPath(PixelLabBuildingKit kit)
        {
            return PaletteDirectory + "/" + CleanName(BuildingPaletteLabel(kit))
                + " Palette.prefab";
        }

        private static List<Vector3Int> ProjectionPaletteCells(
            Grid paletteGrid,
            Tilemap tilemap,
            List<PixelLabPaintTile> tiles
        )
        {
            var sprites = tiles
                .Where(tile => tile != null && tile.sprite != null)
                .Select(tile => tile.sprite)
                .ToArray();
            if (sprites.Length != tiles.Count)
            {
                throw new InvalidOperationException(
                    "A PixelLab projection palette tile has no sprite."
                );
            }

            var spriteCenter = sprites
                .Select(sprite => sprite.bounds.center)
                .Aggregate(Vector3.zero, (sum, center) => sum + center)
                / sprites.Length;
            var originCell = Vector3Int.zero;
            var originCenter = paletteGrid.GetCellCenterLocal(originCell);
            tilemap.tileAnchor = paletteGrid.LocalToCellInterpolated(
                originCenter - spriteCenter
            );

            var cellBounds = paletteGrid.GetBoundsLocal(originCell);
            var maximumWidth = sprites.Max(sprite => sprite.bounds.size.x);
            var maximumHeight = sprites.Max(sprite => sprite.bounds.size.y);
            var cardWidth = maximumWidth + cellBounds.size.x * 2f;
            var cardHeight = maximumHeight + cellBounds.size.y * 2f;
            var columns = Mathf.Min(
                8,
                Mathf.CeilToInt(Mathf.Sqrt(tiles.Count))
            );
            var usedCells = new HashSet<Vector3Int>();
            var usedBounds = new List<Bounds>();
            var result = new List<Vector3Int>(tiles.Count);

            for (var index = 0; index < tiles.Count; index++)
            {
                var column = index % columns;
                var row = index / columns;
                var targetCenter = originCenter + new Vector3(
                    column * cardWidth,
                    -row * cardHeight,
                    0f
                );
                var cell = ClosestOpenPaletteCell(
                    paletteGrid,
                    tilemap.tileAnchor,
                    tiles[index].sprite,
                    targetCenter,
                    usedCells,
                    usedBounds,
                    Mathf.Min(cellBounds.size.x, cellBounds.size.y) * 0.5f
                );
                usedCells.Add(cell);
                usedBounds.Add(PaletteSpriteBounds(
                    paletteGrid,
                    tilemap.tileAnchor,
                    tiles[index].sprite,
                    cell
                ));
                result.Add(cell);
            }
            return result;
        }

        private static Vector3Int ClosestOpenPaletteCell(
            Grid paletteGrid,
            Vector3 tileAnchor,
            Sprite sprite,
            Vector3 targetCenter,
            HashSet<Vector3Int> usedCells,
            List<Bounds> usedBounds,
            float padding
        )
        {
            var estimated = paletteGrid.LocalToCell(targetCenter);
            var best = Vector3Int.zero;
            var bestDistance = float.PositiveInfinity;
            var found = false;
            const int searchRadius = 12;
            for (var deltaY = -searchRadius; deltaY <= searchRadius; deltaY++)
            {
                for (var deltaX = -searchRadius; deltaX <= searchRadius; deltaX++)
                {
                    var candidate = new Vector3Int(
                        estimated.x + deltaX,
                        estimated.y + deltaY,
                        0
                    );
                    if (usedCells.Contains(candidate))
                    {
                        continue;
                    }
                    var candidateBounds = PaletteSpriteBounds(
                        paletteGrid,
                        tileAnchor,
                        sprite,
                        candidate
                    );
                    if (paletteGrid.LocalToCell(candidateBounds.center) != candidate)
                    {
                        continue;
                    }
                    candidateBounds.Expand(new Vector3(padding, padding, 0f));
                    if (usedBounds.Any(bounds => bounds.Intersects(candidateBounds)))
                    {
                        continue;
                    }
                    var distance = (
                        paletteGrid.GetCellCenterLocal(candidate) - targetCenter
                    ).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        best = candidate;
                        bestDistance = distance;
                        found = true;
                    }
                }
            }
            if (!found)
            {
                throw new InvalidOperationException(
                    "Unity could not organize the PixelLab projection palette."
                );
            }
            return best;
        }

        private static Bounds PaletteSpriteBounds(
            Grid paletteGrid,
            Vector3 tileAnchor,
            Sprite sprite,
            Vector3Int cell
        )
        {
            var pivot = paletteGrid.CellToLocalInterpolated(cell + tileAnchor);
            return new Bounds(
                pivot + sprite.bounds.center,
                sprite.bounds.size
            );
        }

        private static void BuildBaseImage(Transform root, string path)
        {
            var gameObject = new GameObject("Map Base");
            gameObject.transform.SetParent(root, false);
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = SpriteAt(path);
            renderer.sortingOrder = -32000;
        }

        private static void BuildBackground(
            Transform root,
            PixelLabBackground background,
            PixelLabSize mapSize,
            float pixelsPerUnit
        )
        {
            if (background == null || string.IsNullOrEmpty(background.image)
                || background.width <= 0f || background.height <= 0f)
            {
                return;
            }

            var parent = new GameObject("Background");
            parent.transform.SetParent(root, false);
            var spriteCache = new Dictionary<string, Sprite>();
            var imageSprite = SpriteAt(background.image);
            var firstX = background.x;
            var firstY = background.y;
            if (background.repeatX)
            {
                firstX += Mathf.Floor(-firstX / background.width) * background.width;
            }
            if (background.repeatY)
            {
                firstY += Mathf.Floor(-firstY / background.height) * background.height;
            }
            var columns = background.repeatX
                ? Mathf.Max(1, Mathf.CeilToInt((mapSize.width - firstX) / background.width))
                : 1;
            var rows = background.repeatY
                ? Mathf.Max(1, Mathf.CeilToInt((mapSize.height - firstY) / background.height))
                : 1;

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    AddBackgroundSprite(
                        parent.transform,
                        "Image " + column + "," + row,
                        imageSprite,
                        firstX + column * background.width,
                        firstY + row * background.height,
                        background.width,
                        background.height,
                        pixelsPerUnit
                    );
                }
            }

            if (background.repeatY || background.verticalExtend == null
                || !background.verticalExtend.enabled)
            {
                return;
            }

            Sprite topSprite = null;
            Sprite bottomSprite = null;
            if (background.verticalExtend.topHeight > 0f)
            {
                topSprite = SpriteAt(
                    new PixelLabPlacement {
                        assetPath = background.image,
                        sourceRect = new PixelLabSourceRect {
                            x = 0f,
                            y = 0f,
                            width = background.width,
                            height = background.verticalExtend.topHeight,
                        },
                    },
                    pixelsPerUnit,
                    spriteCache
                );
            }
            if (background.verticalExtend.bottomHeight > 0f)
            {
                bottomSprite = SpriteAt(
                    new PixelLabPlacement {
                        assetPath = background.image,
                        sourceRect = new PixelLabSourceRect {
                            x = 0f,
                            y = background.height - background.verticalExtend.bottomHeight,
                            width = background.width,
                            height = background.verticalExtend.bottomHeight,
                        },
                    },
                    pixelsPerUnit,
                    spriteCache
                );
            }

            for (var column = 0; column < columns; column++)
            {
                var x = firstX + column * background.width;
                if (topSprite != null)
                {
                    var y = background.y - background.verticalExtend.topHeight;
                    var row = 0;
                    while (y + background.verticalExtend.topHeight > 0f)
                    {
                        AddBackgroundSprite(
                            parent.transform,
                            "Top " + column + "," + row,
                            topSprite,
                            x,
                            y,
                            background.width,
                            background.verticalExtend.topHeight,
                            pixelsPerUnit
                        );
                        y -= background.verticalExtend.topHeight;
                        row++;
                    }
                }
                if (bottomSprite != null)
                {
                    var y = background.y + background.height;
                    var row = 0;
                    while (y < mapSize.height)
                    {
                        AddBackgroundSprite(
                            parent.transform,
                            "Bottom " + column + "," + row,
                            bottomSprite,
                            x,
                            y,
                            background.width,
                            background.verticalExtend.bottomHeight,
                            pixelsPerUnit
                        );
                        y += background.verticalExtend.bottomHeight;
                        row++;
                    }
                }
            }
        }

        private static void AddBackgroundSprite(
            Transform parent,
            string name,
            Sprite sprite,
            float x,
            float y,
            float width,
            float height,
            float pixelsPerUnit
        )
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            // The map editor rasterizes backgrounds in screen space at an
            // integer anchor (Math.round at 1x). Keeping a saved .5 world
            // anchor here puts point-sampled texels on pixel boundaries and
            // can drop/duplicate the outer row. Match the editor's half-up
            // snap before converting pixels to Unity units.
            gameObject.transform.localPosition = PixelAlignedPosition(x, y, pixelsPerUnit);
            gameObject.transform.localScale = new Vector3(
                width / Mathf.Max(1f, sprite.rect.width),
                height / Mathf.Max(1f, sprite.rect.height),
                1f
            );
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = -32768;
        }

        private static void BuildOverlays(
            Transform root,
            PixelLabOverlay[] overlays,
            float pixelsPerUnit
        )
        {
            if (overlays == null || overlays.Length == 0)
            {
                return;
            }
            var parent = new GameObject("Inpaint Overlays");
            parent.transform.SetParent(root, false);
            for (var index = 0; index < overlays.Length; index++)
            {
                var item = overlays[index];
                var gameObject = new GameObject("Inpaint Overlay " + (index + 1));
                gameObject.transform.SetParent(parent.transform, false);
                gameObject.transform.localPosition = PixelPosition(
                    item.x,
                    item.y,
                    pixelsPerUnit
                );
                var sprite = SpriteAt(item.image);
                gameObject.transform.localScale = new Vector3(
                    item.width / Mathf.Max(1f, sprite.rect.width),
                    item.height / Mathf.Max(1f, sprite.rect.height),
                    1f
                );
                var renderer = gameObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = 20000 + index;
            }
        }

        private static void BuildObjects(
            Transform root,
            PixelLabObject[] objects,
            float pixelsPerUnit
        )
        {
            if (objects == null || objects.Length == 0)
            {
                return;
            }
            var parent = new GameObject("Objects");
            parent.transform.SetParent(root, false);
            foreach (var item in objects ?? Array.Empty<PixelLabObject>())
            {
                var gameObject = new GameObject(
                    string.IsNullOrEmpty(item.name) ? item.id : item.name
                );
                gameObject.transform.SetParent(parent.transform, false);
                gameObject.transform.localPosition = PixelPosition(
                    item.x,
                    item.y,
                    pixelsPerUnit
                );
                var sprite = SpriteAt(item.image);
                gameObject.transform.localScale = new Vector3(
                    item.width / Mathf.Max(1f, sprite.rect.width),
                    item.height / Mathf.Max(1f, sprite.rect.height),
                    1f
                );
                var renderer = gameObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = 30000 + item.drawOrder;
                gameObject.SetActive(item.visible);
                if (item.collider != null)
                {
                    AddObjectCollider(
                        gameObject.transform,
                        item.collider,
                        pixelsPerUnit
                    );
                }
            }
        }

        private static void BuildAnnotations(
            Transform root,
            PixelLabAnnotationLayer[] annotations,
            float pixelsPerUnit
        )
        {
            var exportedLayers = (annotations
                ?? Array.Empty<PixelLabAnnotationLayer>())
                .Where(layer => (layer.rects?.Length ?? 0) > 0
                    || (layer.points?.Length ?? 0) > 0)
                .ToArray();
            if (exportedLayers.Length == 0)
            {
                return;
            }
            var parent = new GameObject("Gameplay");
            parent.transform.SetParent(root, false);
            foreach (var layer in exportedLayers)
            {
                var layerObject = new GameObject(
                    string.IsNullOrEmpty(layer.name) ? layer.id : layer.name
                );
                layerObject.transform.SetParent(parent.transform, false);
                var rectangles = layer.rects ?? Array.Empty<PixelLabBox>();
                if (rectangles.Length > 0)
                {
                    BuildGameplayArea(
                        layerObject.transform,
                        "data",
                        rectangles,
                        pixelsPerUnit
                    );
                }
                foreach (var point in layer.points ?? Array.Empty<PixelLabMarker>())
                {
                    var marker = new GameObject(
                        string.IsNullOrEmpty(point.name) ? point.id : point.name
                    );
                    marker.transform.SetParent(layerObject.transform, false);
                    marker.transform.localPosition = PixelPosition(
                        point.x,
                        point.y,
                        pixelsPerUnit
                    );
                    var annotationPoint = marker.AddComponent<PixelLabAnnotationPoint>();
                    annotationPoint.pointId = point.id;
                    annotationPoint.annotationLayer = layer.name;
                }
            }
        }

        private static void BuildGameplayArea(
            Transform parent,
            string role,
            IEnumerable<PixelLabBox> boxes,
            float pixelsPerUnit
        )
        {
            var areaObject = new GameObject(RoleLabel(role) + " Area");
            areaObject.transform.SetParent(parent, false);
            var body = areaObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            var composite = areaObject.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Synchronous;
            var blocksMovement = false;
            composite.isTrigger = true;
            var gameplayArea = areaObject.AddComponent<PixelLabGameplayArea>();
            gameplayArea.role = role;
            gameplayArea.blocksMovement = blocksMovement;
            gameplayArea.compositeCollider = composite;

            foreach (var box in boxes)
            {
                var collider = areaObject.AddComponent<BoxCollider2D>();
                collider.offset = new Vector2(
                    (box.x + box.width / 2f) / pixelsPerUnit,
                    -(box.y + box.height / 2f) / pixelsPerUnit
                );
                collider.size = new Vector2(
                    box.width / pixelsPerUnit,
                    box.height / pixelsPerUnit
                );
                collider.compositeOperation = Collider2D.CompositeOperation.Merge;
            }
        }

        private static string RoleLabel(string role)
        {
            if (string.IsNullOrEmpty(role))
            {
                return "Data";
            }
            return char.ToUpperInvariant(role[0]) + role.Substring(1);
        }

        private static void AddObjectCollider(
            Transform parent,
            PixelLabBox box,
            float pixelsPerUnit
        )
        {
            if (box.role != "blocker" && box.role != "trigger")
            {
                return;
            }
            var colliderPosition = PixelPosition(
                box.x + box.width / 2f,
                box.y + box.height / 2f,
                pixelsPerUnit
            );
            var parentScale = parent.localScale;
            var offset = colliderPosition - parent.localPosition;
            var collider = parent.gameObject.AddComponent<BoxCollider2D>();
            collider.offset = new Vector2(
                offset.x / parentScale.x,
                offset.y / parentScale.y
            );
            collider.size = new Vector2(
                box.width / pixelsPerUnit / Mathf.Abs(parentScale.x),
                box.height / pixelsPerUnit / Mathf.Abs(parentScale.y)
            );
            collider.isTrigger = box.role == "trigger";
        }

        private static Sprite SpriteAt(
            PixelLabPlacement placement,
            float pixelsPerUnit,
            Dictionary<string, Sprite> spriteCache,
            PixelLabMapManifest manifest = null
        )
        {
            var rect = placement.sourceRect;
            if (rect == null || rect.width <= 0f || rect.height <= 0f)
            {
                return SpriteAt(placement.assetPath);
            }
            var cellHeight = FootprintHeight(manifest, placement.assetPath);
            var pivotY = rect.height > cellHeight ? (rect.height - cellHeight * 0.5f) / rect.height : 0.5f;
            var desiredPivot = new Vector2(0.5f, pivotY);
            var key = string.Join(
                "|",
                placement.assetPath,
                rect.x,
                rect.y,
                rect.width,
                rect.height,
                pixelsPerUnit,
                desiredPivot.x,
                desiredPivot.y
            );
            Sprite cached;
            if (spriteCache.TryGetValue(key, out cached))
            {
                return cached;
            }
            var path = GeneratedSpriteDirectory + "/Sprite_" + StableHash(key) + ".asset";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(placement.assetPath);
                if (texture == null)
                {
                    throw new InvalidOperationException(
                        "PixelLab texture was not imported: " + placement.assetPath
                    );
                }
                var unityRect = new Rect(
                    rect.x,
                    texture.height - rect.y - rect.height,
                    rect.width,
                    rect.height
                );
                sprite = Sprite.Create(
                    texture,
                    unityRect,
                    desiredPivot,
                    pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect
                );
                sprite.name = "PixelLab Sprite " + placement.tileIndex;
                AssetDatabase.CreateAsset(sprite, path);
            }
            spriteCache[key] = sprite;
            return sprite;
        }

        private static Sprite SpriteAt(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                throw new InvalidOperationException(
                    "PixelLab sprite was not imported: " + path
                );
            }
            return sprite;
        }

        private static void AppendBuildScene()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.All(scene => scene.path != SceneAssetPath))
            {
                scenes.Add(new EditorBuildSettingsScene(SceneAssetPath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }

        private static void EnsureAssetFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "Assets")
            {
                return;
            }
            var parts = path.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }
                current = next;
            }
        }

        private static string AbsoluteAssetPath(string assetPath)
        {
            return Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                assetPath.Replace('/', Path.DirectorySeparatorChar)
            );
        }

        private static string StableHash(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var item in Encoding.UTF8.GetBytes(value))
            {
                hash ^= item;
                hash *= prime;
            }
            return hash.ToString("X16");
        }

        private static string TerrainBrushAssetPath(string key, string brushName)
        {
            var folder = GeneratedTileDirectory + "/Auto Paint/" + StableHash(key);
            EnsureAssetFolder(folder);
            return folder + "/" + CleanName(brushName) + ".asset";
        }

        private static string CleanName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(
                (string.IsNullOrWhiteSpace(value) ? "PixelLab" : value)
                    .Select(character => invalid.Contains(character) ? '_' : character)
                    .ToArray()
            ).Trim();
            return cleaned.Length > 80 ? cleaned.Substring(0, 80) : cleaned;
        }

        private static string HeightLabel(int layerY)
        {
            return layerY == 0 ? "Y0 - GROUND" : "Y" + layerY + " - ELEVATED +" + layerY;
        }

        private static Vector3 PixelPosition(float x, float y, float pixelsPerUnit)
        {
            var safePpu = Mathf.Max(1f, pixelsPerUnit);
            return new Vector3(x / safePpu, -y / safePpu, 0f);
        }

        private static Vector3 PixelAlignedPosition(
            float x,
            float y,
            float pixelsPerUnit
        )
        {
            return PixelPosition(
                Mathf.Floor(x + 0.5f),
                Mathf.Floor(y + 0.5f),
                pixelsPerUnit
            );
        }

        private static Vector3 PixelVector(PixelLabVector2 value, float pixelsPerUnit)
        {
            return value == null
                ? Vector3.zero
                : PixelPosition(value.x, value.y, pixelsPerUnit);
        }
    }

    internal sealed class PixelLabMapPainterWindow : EditorWindow
    {
        private enum PainterSurface
        {
            Terrain,
            Buildings,
        }

        private enum BuildingSceneTool
        {
            Navigate,
            Brush,
            Rectangle,
            Fill,
            Eyedropper,
            Erase,
        }

        private sealed class BuildingBrush
        {
            public string label;
            public string role;
            public int tileIndex;
            public int previewTileIndex;
            public bool roof;
            public string pairKind;
            public string orientation;
            public string side;
            public string axis;

            public bool IsPair => !string.IsNullOrEmpty(pairKind);
        }

        private PixelLabTileRenderer[] renderers = Array.Empty<PixelLabTileRenderer>();
        private PixelLabPaintTile[] terrainBrushes = Array.Empty<PixelLabPaintTile>();
        private PixelLabBuildingController[] buildingControllers =
            Array.Empty<PixelLabBuildingController>();
        private BuildingBrush[] buildingBrushes = Array.Empty<BuildingBrush>();
        private int rendererIndex;
        private int terrainIndex;
        private int buildingControllerIndex;
        private int buildingStoreyIndex;
        private int buildingBrushIndex;
        private int newBuildingStoreyY = 1;
        private PainterSurface painterSurface;
        private BuildingSceneTool buildingTool;
        private bool buildingAutoWalls = true;
        private bool paintingEnabled;
        private bool eraseMode;
        private Vector3Int lastPaintCell = new Vector3Int(int.MinValue, int.MinValue, 0);
        private Vector2Int? buildingDragStart;
        private Vector2Int? lastBuildingCell;
        private readonly HashSet<Vector2Int> pendingBuildingCells =
            new HashSet<Vector2Int>();

        internal static void OpenWindow()
        {
            var window = GetWindow<PixelLabMapPainterWindow>("PixelLab Map Painter");
            window.minSize = new Vector2(360f, 250f);
            window.RefreshRenderers();
            window.Show();
        }

        internal static void CloseAll()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<PixelLabMapPainterWindow>())
            {
                window.Close();
            }
        }

        internal static void StopAllPainting()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<PixelLabMapPainterWindow>())
            {
                window.StopPainting();
            }
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += DuringSceneGui;
            EditorApplication.hierarchyChanged += RefreshRenderers;
            EditorApplication.update += OnUnityToolChanged;
            RefreshRenderers();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
            EditorApplication.hierarchyChanged -= RefreshRenderers;
            EditorApplication.update -= OnUnityToolChanged;
            StopPainting();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("PixelLab Map Painter", EditorStyles.boldLabel);
            if (renderers.Length > 0 && buildingControllers.Length > 0)
            {
                var nextSurface = (PainterSurface)GUILayout.Toolbar(
                    (int)painterSurface,
                    new[] { "Terrain", "Buildings" },
                    GUILayout.Height(26f)
                );
                if (nextSurface != painterSurface)
                {
                    StopPainting();
                    painterSurface = nextSurface;
                    SelectActiveSurface();
                }
                EditorGUILayout.Space(4f);
            }
            if (BuildingsMode())
            {
                DrawBuildingsPainterGUI();
                return;
            }

            EditorGUILayout.LabelField("PixelLab Terrain Painter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Choose a layer and terrain, then use Paint or Erase below. Navigate is "
                    + "the safe default and leaves Unity's Scene tools alone. You can also "
                    + "open the Unity Auto-Terrain Palette for Brush, Box, Fill, and Erase.",
                MessageType.Info
            );

            if (renderers.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "Open the generated PixelLab map scene to edit its tiles.",
                    MessageType.Warning
                );
                if (GUILayout.Button("Open Generated PixelLab Map"))
                {
                    OpenGeneratedScene();
                }
                return;
            }

            var layerLabels = renderers.Select(RendererLabel).ToArray();
            var nextRenderer = EditorGUILayout.Popup("Layer", rendererIndex, layerLabels);
            if (nextRenderer != rendererIndex)
            {
                rendererIndex = nextRenderer;
                SelectRenderer();
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Terrain", EditorStyles.boldLabel);
            if (terrainBrushes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "This layer has no generated auto-terrain brushes. Use Advanced "
                        + "Variants for individual source tiles.",
                    MessageType.Warning
                );
            }
            else
            {
                for (var row = 0; row < terrainBrushes.Length; row += 2)
                {
                    EditorGUILayout.BeginHorizontal();
                    DrawTerrainCard(row);
                    if (row + 1 < terrainBrushes.Length)
                    {
                        DrawTerrainCard(row + 1);
                    }
                    else
                    {
                        GUILayout.FlexibleSpace();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Scene Tool", EditorStyles.boldLabel);
            var currentMode = !paintingEnabled ? 0 : eraseMode ? 2 : 1;
            var nextMode = GUILayout.Toolbar(
                currentMode,
                new[] { "Navigate", "Paint", "Erase" },
                GUILayout.Height(28f)
            );
            if (nextMode != currentMode)
            {
                SetPainterMode(nextMode);
            }
            if (paintingEnabled)
            {
                EditorGUILayout.HelpBox(
                    (eraseMode ? "ERASE" : "PAINT")
                        + " is active. Press Escape, choose Navigate, or select any Unity "
                        + "Scene tool to stop PixelLab painting.",
                    eraseMode ? MessageType.Warning : MessageType.Info
                );
                if (GUILayout.Button("Stop Painting and Use Move Tool (Esc)"))
                {
                    StopPainting();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Navigate is active. PixelLab is not capturing Scene clicks.",
                    MessageType.None
                );
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(CurrentRenderer() == null))
            {
                if (GUILayout.Button("Open Unity Auto-Terrain Palette"))
                {
                    StopPainting();
                    PixelLabMapImporter.OpenPalette(CurrentRenderer(), false);
                }
                if (!string.IsNullOrEmpty(CurrentRenderer()?.advancedPaletteAssetPath)
                    && GUILayout.Button("Open Advanced Individual Tiles"))
                {
                    StopPainting();
                    PixelLabMapImporter.OpenPalette(CurrentRenderer(), true);
                }
                if (GUILayout.Button("Select This Paint Layer in Hierarchy"))
                {
                    StopPainting();
                    Selection.activeGameObject = CurrentRenderer().gameObject;
                }
            }
            EditorGUILayout.HelpBox(
                "Auto-Terrain contains one terrain brush per material and chooses edge and "
                    + "corner variants automatically. Paint directly over an existing "
                    + "terrain to replace it across the same logical map.",
                MessageType.None
            );
        }

        private void DrawBuildingsPainterGUI()
        {
            EditorGUILayout.HelpBox(
                "Edit Buildings by meaning, not by visual fragments. Choose a signed "
                    + "storey and role, then paint directly over original cells or beyond "
                    + "the exported bounds. Navigate is always the safe default.",
                MessageType.Info
            );
            var controller = CurrentBuildingController();
            if (controller == null)
            {
                EditorGUILayout.HelpBox(
                    "Open the generated PixelLab Buildings scene to edit it.",
                    MessageType.Warning
                );
                if (GUILayout.Button("Open Generated PixelLab Map"))
                {
                    OpenGeneratedScene();
                }
                return;
            }

            var kitLabels = buildingControllers
                .Select(item => item.Kit.name + " • " + item.Kit.gridKind)
                .ToArray();
            var nextController = EditorGUILayout.Popup(
                "Kit",
                buildingControllerIndex,
                kitLabels
            );
            if (nextController != buildingControllerIndex)
            {
                StopPainting();
                buildingControllerIndex = nextController;
                SelectBuildingController();
                controller = CurrentBuildingController();
            }

            var storeys = controller.EditState.Storeys
                .OrderBy(item => item.layerY)
                .ToArray();
            var storeyLabels = storeys.Select(item => StoreyLabel(item.layerY)).ToArray();
            buildingStoreyIndex = Mathf.Clamp(
                buildingStoreyIndex,
                0,
                Mathf.Max(0, storeys.Length - 1)
            );
            var nextStorey = EditorGUILayout.Popup(
                "Storey",
                buildingStoreyIndex,
                storeyLabels
            );
            if (nextStorey != buildingStoreyIndex)
            {
                StopPainting();
                buildingStoreyIndex = nextStorey;
                SceneView.RepaintAll();
            }

            EditorGUILayout.BeginHorizontal();
            newBuildingStoreyY = EditorGUILayout.IntField(
                "Add signed Y",
                newBuildingStoreyY
            );
            if (GUILayout.Button("Add Level", GUILayout.Width(92f)))
            {
                StopPainting();
                if (!PixelLabBuildingEditorOperations.AddStorey(
                    controller,
                    newBuildingStoreyY
                ))
                {
                    ShowNotification(new GUIContent(
                        "Y" + newBuildingStoreyY + " already exists"
                    ));
                }
                SelectBuildingStorey(newBuildingStoreyY);
                newBuildingStoreyY++;
            }
            EditorGUILayout.EndHorizontal();

            DrawBuildingSemanticGUI(controller);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Scene Tool", EditorStyles.boldLabel);
            var nextTool = (BuildingSceneTool)GUILayout.Toolbar(
                (int)buildingTool,
                new[] { "Navigate", "Brush", "Rect", "Fill", "Pick", "Erase" },
                GUILayout.Height(28f)
            );
            if (nextTool != buildingTool)
            {
                SetBuildingTool(nextTool);
            }
            if (buildingTool == BuildingSceneTool.Navigate)
            {
                EditorGUILayout.HelpBox(
                    "Navigate is active. PixelLab is not capturing Scene clicks.",
                    MessageType.None
                );
            }
            else
            {
                EditorGUILayout.HelpBox(
                    buildingTool + " is active. Press Escape, choose Navigate, or select "
                        + "any Unity Scene tool to stop PixelLab painting.",
                    buildingTool == BuildingSceneTool.Erase
                        ? MessageType.Warning
                        : MessageType.Info
                );
                if (GUILayout.Button("Stop Painting and Use Move Tool (Esc)"))
                {
                    StopPainting();
                }
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Open Unity Individual Tiles Palette"))
            {
                StopPainting();
                PixelLabMapImporter.OpenBuildingPalette(controller);
            }
            EditorGUILayout.HelpBox(
                "Individual Tiles exposes every native Buildings tile for inspection and "
                    + "selection. Use the PixelLab Buildings painter for map edits so "
                    + "structure, walls, corners, and paired pieces keep auto-tiling.",
                MessageType.None
            );
        }

        private void DrawBuildingSemanticGUI(PixelLabBuildingController controller)
        {
            EditorGUILayout.Space(4f);
            buildingAutoWalls = EditorGUILayout.Toggle(
                "Auto walls on floor/roof",
                buildingAutoWalls
            );
            EditorGUILayout.LabelField("Building Role", EditorStyles.boldLabel);
            for (var row = 0; row < buildingBrushes.Length; row += 2)
            {
                EditorGUILayout.BeginHorizontal();
                DrawBuildingBrushCard(controller, row);
                if (row + 1 < buildingBrushes.Length)
                {
                    DrawBuildingBrushCard(controller, row + 1);
                }
                else
                {
                    GUILayout.FlexibleSpace();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawBuildingBrushCard(
            PixelLabBuildingController controller,
            int index
        )
        {
            var brush = buildingBrushes[index];
            var selected = buildingBrushIndex == index;
            var previousColor = GUI.backgroundColor;
            if (selected)
            {
                GUI.backgroundColor = new Color(0.35f, 0.65f, 0.95f);
            }
            var rect = GUILayoutUtility.GetRect(
                0f,
                62f,
                GUILayout.ExpandWidth(true),
                GUILayout.MinWidth(150f)
            );
            var clicked = GUI.Button(rect, GUIContent.none);
            GUI.backgroundColor = previousColor;
            var previewRect = new Rect(rect.x + 7f, rect.y + 7f, 48f, 48f);
            DrawSpritePreview(
                previewRect,
                controller.NativeTile(brush.previewTileIndex).sprite
            );
            GUI.Label(
                new Rect(
                    previewRect.xMax + 9f,
                    rect.y + 6f,
                    Mathf.Max(0f, rect.xMax - previewRect.xMax - 15f),
                    rect.height - 12f
                ),
                brush.label,
                EditorStyles.wordWrappedLabel
            );
            if (clicked)
            {
                buildingBrushIndex = index;
                SetBuildingTool(BuildingSceneTool.Brush);
            }
        }

        private void OpenGeneratedScene()
        {
            PixelLabMapImporter.ActivateEditorContext();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PixelLabMapImporter.SceneAssetPath)
                == null)
            {
                EditorUtility.DisplayDialog(
                    "PixelLab map not found",
                    "Re-import the PixelLab Unity package, then try again.",
                    "OK"
                );
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            EditorSceneManager.OpenScene(
                PixelLabMapImporter.SceneAssetPath,
                OpenSceneMode.Single
            );
            RefreshRenderers();
            SceneView.RepaintAll();
        }

        private void RefreshRenderers()
        {
            var selected = CurrentRenderer();
            var selectedBuilding = CurrentBuildingController();
            renderers = Resources.FindObjectsOfTypeAll<PixelLabTileRenderer>()
                .Where(renderer => renderer != null && renderer.gameObject.scene.IsValid())
                .OrderBy(renderer => renderer.layerY)
                .ThenBy(RendererLabel)
                .ToArray();
            buildingControllers = Resources.FindObjectsOfTypeAll<
                PixelLabBuildingController>()
                .Where(controller =>
                    controller != null && controller.gameObject.scene.IsValid()
                )
                .OrderBy(controller => controller.Kit.name)
                .ToArray();
            rendererIndex = selected == null ? 0 : Array.IndexOf(renderers, selected);
            if (rendererIndex < 0)
            {
                rendererIndex = 0;
            }
            buildingControllerIndex = selectedBuilding == null
                ? 0
                : Array.IndexOf(buildingControllers, selectedBuilding);
            if (buildingControllerIndex < 0)
            {
                buildingControllerIndex = 0;
            }
            if (buildingControllers.Length > 0 && renderers.Length == 0)
            {
                painterSurface = PainterSurface.Buildings;
            }
            else if (renderers.Length > 0 && buildingControllers.Length == 0)
            {
                painterSurface = PainterSurface.Terrain;
            }
            SelectActiveSurface();
            Repaint();
        }

        private void SelectActiveSurface()
        {
            if (BuildingsMode())
            {
                SelectBuildingController();
            }
            else
            {
                SelectRenderer();
            }
        }

        private void SelectRenderer()
        {
            var renderer = CurrentRenderer();
            terrainBrushes = renderer == null
                ? Array.Empty<PixelLabPaintTile>()
                : (renderer.availableTiles ?? Array.Empty<PixelLabPaintTile>())
                    .Where(tile => tile != null && tile.IsTerrainBrush && tile.paintableTerrain)
                    .OrderBy(tile => tile.paintTerrain)
                    .ToArray();
            terrainIndex = Mathf.Clamp(terrainIndex, 0, Mathf.Max(0, terrainBrushes.Length - 1));
            lastPaintCell = new Vector3Int(int.MinValue, int.MinValue, 0);
            if (renderer != null)
            {
                PixelLabMapImporter.SetGridPaintingState("scenePaintTarget", renderer.gameObject);
                Selection.activeGameObject = renderer.gameObject;
            }
            SceneView.RepaintAll();
        }

        private PixelLabTileRenderer CurrentRenderer()
        {
            return rendererIndex >= 0 && rendererIndex < renderers.Length
                ? renderers[rendererIndex]
                : null;
        }

        private PixelLabPaintTile CurrentTerrain()
        {
            return terrainIndex >= 0 && terrainIndex < terrainBrushes.Length
                ? terrainBrushes[terrainIndex]
                : null;
        }

        private bool BuildingsMode()
        {
            return painterSurface == PainterSurface.Buildings &&
                buildingControllers.Length > 0;
        }

        private PixelLabBuildingController CurrentBuildingController()
        {
            return buildingControllerIndex >= 0 &&
                buildingControllerIndex < buildingControllers.Length
                ? buildingControllers[buildingControllerIndex]
                : null;
        }

        private int CurrentBuildingLayerY()
        {
            var controller = CurrentBuildingController();
            if (controller == null)
            {
                return 0;
            }
            var storeys = controller.EditState.Storeys
                .OrderBy(item => item.layerY)
                .ToArray();
            if (storeys.Length == 0)
            {
                return 0;
            }
            buildingStoreyIndex = Mathf.Clamp(
                buildingStoreyIndex,
                0,
                storeys.Length - 1
            );
            return storeys[buildingStoreyIndex].layerY;
        }

        private void SelectBuildingController()
        {
            var controller = CurrentBuildingController();
            buildingDragStart = null;
            lastBuildingCell = null;
            pendingBuildingCells.Clear();
            if (controller == null)
            {
                buildingBrushes = Array.Empty<BuildingBrush>();
                return;
            }
            buildingStoreyIndex = Mathf.Clamp(
                buildingStoreyIndex,
                0,
                Mathf.Max(0, controller.EditState.Storeys.Count - 1)
            );
            buildingBrushes = BuildingBrushes(controller);
            buildingBrushIndex = Mathf.Clamp(
                buildingBrushIndex,
                0,
                Mathf.Max(0, buildingBrushes.Length - 1)
            );
            newBuildingStoreyY = controller.EditState.Storeys.Count == 0
                ? 0
                : controller.EditState.Storeys.Max(item => item.layerY) + 1;
            Selection.activeGameObject = controller.gameObject;
            SceneView.RepaintAll();
        }

        private void SelectBuildingStorey(int layerY)
        {
            var controller = CurrentBuildingController();
            if (controller == null)
            {
                return;
            }
            var storeys = controller.EditState.Storeys
                .OrderBy(item => item.layerY)
                .ToArray();
            for (var index = 0; index < storeys.Length; index++)
            {
                if (storeys[index].layerY == layerY)
                {
                    buildingStoreyIndex = index;
                    break;
                }
            }
            SceneView.RepaintAll();
            Repaint();
        }

        private static BuildingBrush[] BuildingBrushes(
            PixelLabBuildingController controller
        )
        {
            var kit = controller.Kit;
            var output = new List<BuildingBrush>
            {
                new BuildingBrush
                {
                    label = "Floor",
                    role = PixelLabBuildingRoles.Floor,
                    tileIndex = kit.semanticAssets.floorTileIndex,
                    previewTileIndex = kit.semanticAssets.floorTileIndex,
                },
                new BuildingBrush
                {
                    label = "Roof",
                    role = PixelLabBuildingRoles.Floor,
                    tileIndex = kit.semanticAssets.roofTileIndex,
                    previewTileIndex = kit.semanticAssets.roofTileIndex,
                    roof = true,
                },
                new BuildingBrush
                {
                    label = "Partition",
                    role = PixelLabBuildingRoles.Partition,
                    tileIndex = kit.semanticAssets.partitionMarkerTileIndex,
                    previewTileIndex = kit.semanticAssets.partitionMarkerTileIndex,
                },
            };
            var pairedIndices = new HashSet<int>();
            foreach (var pair in kit.pairedPieces ??
                Array.Empty<PixelLabBuildingPair>())
            {
                pairedIndices.Add(pair.firstTileIndex);
                pairedIndices.Add(pair.secondTileIndex);
                output.Add(new BuildingBrush
                {
                    label = BuildingPairLabel(pair),
                    role = PixelLabBuildingRoles.Stamp,
                    tileIndex = pair.firstTileIndex,
                    previewTileIndex = pair.firstTileIndex,
                    pairKind = pair.kind,
                    orientation = pair.orientation,
                    side = pair.side,
                    axis = pair.axis,
                });
            }
            output.Add(new BuildingBrush
            {
                label = "Pillar",
                role = PixelLabBuildingRoles.Stamp,
                tileIndex = kit.semanticAssets.pillarTileIndex,
                previewTileIndex = kit.semanticAssets.pillarTileIndex,
            });
            var standaloneStamps = controller.EditState.Storeys
                .SelectMany(storey => storey.cells)
                .Where(cell => cell.role == PixelLabBuildingRoles.Stamp)
                .Select(cell => cell.tileIndex)
                .Where(index => index != kit.semanticAssets.pillarTileIndex &&
                    !pairedIndices.Contains(index))
                .Distinct()
                .OrderBy(index => index);
            foreach (var tileIndex in standaloneStamps)
            {
                output.Add(new BuildingBrush
                {
                    label = "Stamp " + tileIndex,
                    role = PixelLabBuildingRoles.Stamp,
                    tileIndex = tileIndex,
                    previewTileIndex = tileIndex,
                });
            }
            return output.ToArray();
        }

        private static string BuildingPairLabel(PixelLabBuildingPair pair)
        {
            if (pair.kind == "exterior-door")
            {
                return "Exterior Door " + pair.side;
            }
            if (pair.kind == "partition-door")
            {
                return "Partition Door " + pair.axis;
            }
            if (pair.kind == "stairs")
            {
                return "Stairs " + pair.orientation;
            }
            return pair.kind + " " + pair.orientation;
        }

        private static string StoreyLabel(int layerY)
        {
            if (layerY == 0)
            {
                return "Y0 — Ground";
            }
            return layerY > 0
                ? "Y" + layerY + " — Elevated +" + layerY
                : "Y" + layerY + " — Below " + Mathf.Abs(layerY);
        }

        private void SetPainterMode(int mode)
        {
            paintingEnabled = mode == 1 || mode == 2;
            eraseMode = mode == 2;
            buildingTool = BuildingSceneTool.Navigate;
            lastPaintCell = new Vector3Int(int.MinValue, int.MinValue, 0);
            if (paintingEnabled)
            {
                Tools.current = Tool.None;
            }
            else if (Tools.current == Tool.None)
            {
                Tools.current = Tool.Move;
            }
            Repaint();
            SceneView.RepaintAll();
        }

        private void SetBuildingTool(BuildingSceneTool tool)
        {
            buildingTool = tool;
            paintingEnabled = tool != BuildingSceneTool.Navigate;
            eraseMode = tool == BuildingSceneTool.Erase;
            buildingDragStart = null;
            lastBuildingCell = null;
            pendingBuildingCells.Clear();
            if (paintingEnabled)
            {
                Tools.current = Tool.None;
            }
            else if (Tools.current == Tool.None)
            {
                Tools.current = Tool.Move;
            }
            Repaint();
            SceneView.RepaintAll();
        }

        private void StopPainting()
        {
            SetPainterMode(0);
        }

        private void OnUnityToolChanged()
        {
            if (!paintingEnabled || Tools.current == Tool.None)
            {
                return;
            }
            paintingEnabled = false;
            eraseMode = false;
            buildingTool = BuildingSceneTool.Navigate;
            buildingDragStart = null;
            lastBuildingCell = null;
            pendingBuildingCells.Clear();
            lastPaintCell = new Vector3Int(int.MinValue, int.MinValue, 0);
            Repaint();
            SceneView.RepaintAll();
        }

        private void DuringSceneGui(SceneView sceneView)
        {
            var currentEvent = Event.current;
            if (paintingEnabled
                && currentEvent.type == EventType.KeyDown
                && currentEvent.keyCode == KeyCode.Escape)
            {
                StopPainting();
                currentEvent.Use();
                return;
            }
            if (!paintingEnabled)
            {
                return;
            }
            if (Tools.current != Tool.None)
            {
                OnUnityToolChanged();
                return;
            }

            if (BuildingsMode())
            {
                DuringBuildingsSceneGui(sceneView, currentEvent);
                return;
            }

            var renderer = CurrentRenderer();
            var tilemap = renderer == null ? null : renderer.GetComponent<Tilemap>();
            var grid = renderer == null ? null : renderer.GetComponentInParent<Grid>();
            if (renderer == null || tilemap == null || grid == null)
            {
                return;
            }

            var worldPoint = MouseWorldPoint(currentEvent.mousePosition, renderer.transform.position.z);
            var cell = tilemap.WorldToCell(worldPoint);
            DrawCellOutline(tilemap, cell, eraseMode || currentEvent.shift);

            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12f, 12f, 420f, 54f), EditorStyles.helpBox);
            GUILayout.Label(
                RendererLabel(renderer) + "  |  "
                    + ((eraseMode || currentEvent.shift)
                        ? "ERASE"
                        : TerrainLabel(CurrentTerrain())),
                EditorStyles.boldLabel
            );
            GUILayout.Label("Cell " + cell.x + ", " + cell.y);
            GUILayout.EndArea();
            Handles.EndGUI();

            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (currentEvent.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
            }
            if (currentEvent.type == EventType.MouseMove)
            {
                sceneView.Repaint();
            }
            if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
            {
                lastPaintCell = new Vector3Int(int.MinValue, int.MinValue, 0);
                currentEvent.Use();
                return;
            }
            if ((currentEvent.type != EventType.MouseDown
                    && currentEvent.type != EventType.MouseDrag)
                || currentEvent.button != 0
                || currentEvent.alt
                || currentEvent.control
                || currentEvent.command)
            {
                return;
            }
            if (cell == lastPaintCell)
            {
                currentEvent.Use();
                return;
            }

            var erase = eraseMode || currentEvent.shift;
            var terrain = CurrentTerrain();
            if (!erase && terrain == null)
            {
                return;
            }
            var strokeCells = StrokeCells(lastPaintCell, cell, renderer.gridKind).ToArray();
            var paintCells = erase
                ? strokeCells
                : renderer.legacy
                    ? renderer.TerrainPaintCells(strokeCells)
                    : strokeCells;
            var higherObliqueTilemaps = renderer.HigherObliqueTilemaps();
            if (currentEvent.type == EventType.MouseDown)
            {
                Undo.RegisterCompleteObjectUndo(
                    tilemap,
                    erase ? "Erase PixelLab terrain" : "Paint PixelLab terrain"
                );
                Undo.RegisterCompleteObjectUndo(
                    renderer,
                    erase ? "Erase PixelLab terrain state" : "Paint PixelLab terrain state"
                );
                foreach (var higherTilemap in higherObliqueTilemaps)
                {
                    Undo.RegisterCompleteObjectUndo(
                        higherTilemap,
                        erase ? "Erase PixelLab terrain" : "Paint PixelLab terrain"
                    );
                }
            }
            foreach (var paintCell in paintCells)
            {
                tilemap.SetTile(paintCell, erase ? null : terrain);
            }
            renderer.ClearHigherObliqueCells(strokeCells);
            tilemap.RefreshAllTiles();
            renderer.RefreshNow();
            EditorUtility.SetDirty(tilemap);
            foreach (var higherTilemap in higherObliqueTilemaps)
            {
                EditorUtility.SetDirty(higherTilemap);
            }
            EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            lastPaintCell = cell;
            currentEvent.Use();
            sceneView.Repaint();
        }

        private void DuringBuildingsSceneGui(
            SceneView sceneView,
            Event currentEvent
        )
        {
            var controller = CurrentBuildingController();
            if (controller == null || controller.Kit == null ||
                controller.EditState == null)
            {
                return;
            }
            var layerY = CurrentBuildingLayerY();
            var worldPoint = MouseWorldPoint(
                currentEvent.mousePosition,
                controller.transform.position.z
            );
            var cell = controller.WorldToLogical(worldPoint, layerY);
            var brush = CurrentBuildingBrush();
            DrawBuildingCellOutline(controller, cell, layerY, eraseMode);
            if (pendingBuildingCells.Count > 0)
            {
                foreach (var pending in pendingBuildingCells)
                {
                    DrawBuildingCellOutline(
                        controller,
                        pending,
                        layerY,
                        eraseMode,
                        1.5f
                    );
                }
            }

            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12f, 12f, 470f, 58f), EditorStyles.helpBox);
            GUILayout.Label(
                controller.Kit.name + " • " + StoreyLabel(layerY) + " • " +
                    BuildingSceneLabel(),
                EditorStyles.boldLabel
            );
            GUILayout.Label(
                "Cell " + cell.x + ", " + cell.y
            );
            GUILayout.EndArea();
            Handles.EndGUI();

            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (currentEvent.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
            }
            if (currentEvent.type == EventType.MouseMove)
            {
                sceneView.Repaint();
            }
            if (currentEvent.button != 0 || currentEvent.alt || currentEvent.control ||
                currentEvent.command)
            {
                return;
            }

            if (buildingTool == BuildingSceneTool.Eyedropper &&
                currentEvent.type == EventType.MouseDown)
            {
                PickBuildingBrush(controller, layerY, cell);
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }
            if (buildingTool == BuildingSceneTool.Fill &&
                currentEvent.type == EventType.MouseDown)
            {
                if (brush != null && !brush.IsPair)
                {
                    ApplyBuildingCells(
                        controller,
                        BuildingFillCells(controller, brush.role, layerY, cell),
                        false
                    );
                }
                else
                {
                    ShowNotification(new GUIContent(
                        "Fill is unavailable for paired pieces"
                    ));
                }
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }
            if (currentEvent.type == EventType.MouseDown)
            {
                buildingDragStart = cell;
                lastBuildingCell = cell;
                pendingBuildingCells.Clear();
                pendingBuildingCells.Add(cell);
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }
            if (currentEvent.type == EventType.MouseDrag && buildingDragStart.HasValue)
            {
                if (buildingTool == BuildingSceneTool.Rectangle)
                {
                    pendingBuildingCells.Clear();
                    foreach (var item in BuildingRectangleCells(
                        buildingDragStart.Value,
                        cell
                    ))
                    {
                        pendingBuildingCells.Add(item);
                    }
                }
                else
                {
                    foreach (var item in BuildingStrokeCells(
                        lastBuildingCell ?? cell,
                        cell
                    ))
                    {
                        pendingBuildingCells.Add(item);
                    }
                    lastBuildingCell = cell;
                }
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }
            if (currentEvent.type != EventType.MouseUp || !buildingDragStart.HasValue)
            {
                return;
            }
            if (buildingTool == BuildingSceneTool.Rectangle)
            {
                pendingBuildingCells.Clear();
                foreach (var item in BuildingRectangleCells(
                    buildingDragStart.Value,
                    cell
                ))
                {
                    pendingBuildingCells.Add(item);
                }
            }
            ApplyBuildingCells(
                controller,
                pendingBuildingCells,
                buildingTool == BuildingSceneTool.Erase
            );
            buildingDragStart = null;
            lastBuildingCell = null;
            pendingBuildingCells.Clear();
            currentEvent.Use();
            sceneView.Repaint();
        }

        private void ApplyBuildingCells(
            PixelLabBuildingController controller,
            IEnumerable<Vector2Int> source,
            bool erase
        )
        {
            var cells = source.Distinct().ToArray();
            if (cells.Length == 0)
            {
                return;
            }
            var brush = CurrentBuildingBrush();
            if (brush == null)
            {
                return;
            }
            PixelLabBuildingEditorOperations.ApplyCells(
                controller,
                erase ? "Erase PixelLab Buildings" : "Paint PixelLab Buildings",
                cells,
                cell => erase
                    ? controller.EraseCell(
                        brush.role,
                        CurrentBuildingLayerY(),
                        cell.x,
                        cell.y
                    )
                    : PaintBuildingBrush(
                        controller,
                        brush,
                        CurrentBuildingLayerY(),
                        cell
                    )
            );
            Repaint();
        }

        private bool PaintBuildingBrush(
            PixelLabBuildingController controller,
            BuildingBrush brush,
            int layerY,
            Vector2Int cell
        )
        {
            if (brush.role == PixelLabBuildingRoles.Floor)
            {
                return controller.PaintFloor(
                    layerY,
                    cell.x,
                    cell.y,
                    brush.roof,
                    buildingAutoWalls
                );
            }
            if (brush.role == PixelLabBuildingRoles.Partition)
            {
                return controller.PaintPartition(layerY, cell.x, cell.y);
            }
            if (brush.IsPair)
            {
                return controller.PaintPairedPiece(
                    layerY,
                    cell.x,
                    cell.y,
                    brush.pairKind,
                    brush.orientation,
                    brush.side,
                    brush.axis
                );
            }
            if (brush.tileIndex == controller.Kit.semanticAssets.pillarTileIndex)
            {
                return controller.PaintPillar(layerY, cell.x, cell.y);
            }
            return controller.PaintStamp(
                layerY,
                cell.x,
                cell.y,
                brush.tileIndex
            );
        }

        private void PickBuildingBrush(
            PixelLabBuildingController controller,
            int layerY,
            Vector2Int cell
        )
        {
            for (var index = 0; index < buildingBrushes.Length; index++)
            {
                var brush = buildingBrushes[index];
                var logical = controller.SemanticCell(
                    brush.role,
                    layerY,
                    cell.x,
                    cell.y
                );
                if (logical == null || !BuildingBrushMatches(
                    controller.Kit,
                    brush,
                    logical.tileIndex
                ))
                {
                    continue;
                }
                buildingBrushIndex = index;
                if (brush.role == PixelLabBuildingRoles.Floor)
                {
                    buildingAutoWalls = logical.tileIndex <
                        controller.Kit.semanticAssets.noWallFloorOffset;
                }
                SetBuildingTool(BuildingSceneTool.Brush);
                ShowNotification(new GUIContent("Picked " + brush.label));
                return;
            }
            ShowNotification(new GUIContent("No semantic Building cell here"));
        }

        private static bool BuildingBrushMatches(
            PixelLabBuildingKit kit,
            BuildingBrush brush,
            int tileIndex
        )
        {
            if (brush.role == PixelLabBuildingRoles.Floor)
            {
                var normalized = tileIndex >= kit.semanticAssets.noWallFloorOffset
                    ? tileIndex - kit.semanticAssets.noWallFloorOffset
                    : tileIndex;
                return normalized == brush.tileIndex;
            }
            if (!brush.IsPair)
            {
                return tileIndex == brush.tileIndex;
            }
            return (kit.pairedPieces ?? Array.Empty<PixelLabBuildingPair>())
                .Any(pair => pair.kind == brush.pairKind &&
                    pair.orientation == brush.orientation &&
                    pair.side == brush.side && pair.axis == brush.axis &&
                    (pair.firstTileIndex == tileIndex ||
                     pair.secondTileIndex == tileIndex));
        }

        private IEnumerable<Vector2Int> BuildingFillCells(
            PixelLabBuildingController controller,
            string role,
            int layerY,
            Vector2Int seed
        )
        {
            var target = controller.SemanticCell(role, layerY, seed.x, seed.y);
            if (target == null)
            {
                return new[] { seed };
            }
            var storey = controller.EditState.FindStorey(layerY);
            var eligible = new HashSet<Vector2Int>(storey.cells
                .Where(cell => cell.role == role &&
                    (role != PixelLabBuildingRoles.Floor ||
                     cell.tileIndex == target.tileIndex))
                .Select(cell => new Vector2Int(cell.q, cell.r)));
            var output = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!eligible.Contains(current) || !output.Add(current))
                {
                    continue;
                }
                queue.Enqueue(current + Vector2Int.up);
                queue.Enqueue(current + Vector2Int.right);
                queue.Enqueue(current + Vector2Int.down);
                queue.Enqueue(current + Vector2Int.left);
            }
            return output;
        }

        private BuildingBrush CurrentBuildingBrush()
        {
            return buildingBrushIndex >= 0 &&
                buildingBrushIndex < buildingBrushes.Length
                ? buildingBrushes[buildingBrushIndex]
                : null;
        }

        private string BuildingSceneLabel()
        {
            return CurrentBuildingBrush()?.label ?? "Choose a role";
        }

        private static IEnumerable<Vector2Int> BuildingStrokeCells(
            Vector2Int from,
            Vector2Int to
        )
        {
            var x = from.x;
            var y = from.y;
            var deltaX = Mathf.Abs(to.x - from.x);
            var deltaY = Mathf.Abs(to.y - from.y);
            var stepX = from.x < to.x ? 1 : -1;
            var stepY = from.y < to.y ? 1 : -1;
            var error = deltaX - deltaY;
            yield return from;
            while (x != to.x || y != to.y)
            {
                var doubled = error * 2;
                if (doubled > -deltaY)
                {
                    error -= deltaY;
                    x += stepX;
                }
                if (doubled < deltaX)
                {
                    error += deltaX;
                    y += stepY;
                }
                yield return new Vector2Int(x, y);
            }
        }

        private static IEnumerable<Vector2Int> BuildingRectangleCells(
            Vector2Int from,
            Vector2Int to
        )
        {
            for (var r = Mathf.Min(from.y, to.y); r <= Mathf.Max(from.y, to.y); r++)
            {
                for (var q = Mathf.Min(from.x, to.x); q <= Mathf.Max(from.x, to.x); q++)
                {
                    yield return new Vector2Int(q, r);
                }
            }
        }

        private static void DrawBuildingCellOutline(
            PixelLabBuildingController controller,
            Vector2Int cell,
            int layerY,
            bool erase,
            float width = 4f
        )
        {
            var center = controller.LogicalToWorld(cell.x, cell.y, layerY);
            var q = controller.LogicalToWorld(cell.x + 1, cell.y, layerY) - center;
            var r = controller.LogicalToWorld(cell.x, cell.y + 1, layerY) - center;
            var corners = new[] {
                center - q / 2f - r / 2f,
                center + q / 2f - r / 2f,
                center + q / 2f + r / 2f,
                center - q / 2f + r / 2f,
                center - q / 2f - r / 2f,
            };
            Handles.color = erase
                ? new Color(1f, 0.25f, 0.25f, 1f)
                : new Color(0.2f, 0.85f, 1f, 1f);
            Handles.DrawAAPolyLine(width, corners);
        }

        private void DrawTerrainCard(int index)
        {
            var tile = terrainBrushes[index];
            var selected = terrainIndex == index;
            var previousColor = GUI.backgroundColor;
            if (selected)
            {
                GUI.backgroundColor = new Color(0.35f, 0.65f, 0.95f);
            }
            var rect = GUILayoutUtility.GetRect(
                0f,
                62f,
                GUILayout.ExpandWidth(true),
                GUILayout.MinWidth(150f)
            );
            var clicked = GUI.Button(rect, GUIContent.none);
            GUI.backgroundColor = previousColor;

            var previewRect = new Rect(rect.x + 7f, rect.y + 7f, 48f, 48f);
            DrawSpritePreview(previewRect, tile == null ? null : tile.sprite);
            var labelRect = new Rect(
                previewRect.xMax + 9f,
                rect.y + 6f,
                Mathf.Max(0f, rect.xMax - previewRect.xMax - 15f),
                rect.height - 12f
            );
            GUI.Label(labelRect, TerrainLabel(tile), EditorStyles.wordWrappedLabel);

            if (clicked)
            {
                terrainIndex = index;
                SetPainterMode(1);
            }
        }

        private static void DrawSpritePreview(Rect rect, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f, 0.7f));
                return;
            }
            var textureRect = sprite.textureRect;
            var texture = sprite.texture;
            var coordinates = new Rect(
                textureRect.x / texture.width,
                textureRect.y / texture.height,
                textureRect.width / texture.width,
                textureRect.height / texture.height
            );
            GUI.DrawTextureWithTexCoords(rect, texture, coordinates, true);
        }

        private static IEnumerable<Vector3Int> StrokeCells(
            Vector3Int from,
            Vector3Int to,
            string gridKind
        )
        {
            if (from.x == int.MinValue || from.y == int.MinValue)
            {
                yield return to;
                yield break;
            }
            if (gridKind == "hex-pointy-top" || gridKind == "hex-flat-top")
            {
                var fromAxial = PixelLabGridCoordinates.ToAxial(gridKind, from);
                var toAxial = PixelLabGridCoordinates.ToAxial(gridKind, to);
                var distance = HexDistance(fromAxial, toAxial);
                for (var step = 1; step <= distance; step++)
                {
                    var axial = HexRound(
                        Vector2.Lerp(fromAxial, toAxial, step / (float)distance)
                    );
                    yield return PixelLabGridCoordinates.ToUnityCell(
                        gridKind,
                        axial.x,
                        axial.y
                    );
                }
                yield break;
            }

            var x = from.x;
            var y = from.y;
            var deltaX = Mathf.Abs(to.x - from.x);
            var deltaY = Mathf.Abs(to.y - from.y);
            var stepX = from.x < to.x ? 1 : -1;
            var stepY = from.y < to.y ? 1 : -1;
            var error = deltaX - deltaY;
            while (x != to.x || y != to.y)
            {
                var doubledError = error * 2;
                if (doubledError > -deltaY)
                {
                    error -= deltaY;
                    x += stepX;
                }
                if (doubledError < deltaX)
                {
                    error += deltaX;
                    y += stepY;
                }
                yield return new Vector3Int(x, y, to.z);
            }
        }

        private static int HexDistance(Vector2Int from, Vector2Int to)
        {
            var deltaQ = to.x - from.x;
            var deltaR = to.y - from.y;
            return (Mathf.Abs(deltaQ) + Mathf.Abs(deltaR)
                + Mathf.Abs(deltaQ + deltaR)) / 2;
        }

        private static Vector2Int HexRound(Vector2 axial)
        {
            var x = axial.x;
            var z = axial.y;
            var y = -x - z;
            var roundedX = Mathf.RoundToInt(x);
            var roundedY = Mathf.RoundToInt(y);
            var roundedZ = Mathf.RoundToInt(z);
            var xDelta = Mathf.Abs(roundedX - x);
            var yDelta = Mathf.Abs(roundedY - y);
            var zDelta = Mathf.Abs(roundedZ - z);
            if (xDelta > yDelta && xDelta > zDelta)
            {
                roundedX = -roundedY - roundedZ;
            }
            else if (yDelta > zDelta)
            {
                roundedY = -roundedX - roundedZ;
            }
            else
            {
                roundedZ = -roundedX - roundedY;
            }
            return new Vector2Int(roundedX, roundedZ);
        }

        private static Vector3 MouseWorldPoint(Vector2 mousePosition, float z)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, z));
            float distance;
            return plane.Raycast(ray, out distance) ? ray.GetPoint(distance) : Vector3.zero;
        }

        private static void DrawCellOutline(Tilemap grid, Vector3Int cell, bool erase)
        {
            var center = grid.GetCellCenterWorld(cell);
            var q = grid.GetCellCenterWorld(cell + Vector3Int.right) - center;
            var r = grid.GetCellCenterWorld(cell + Vector3Int.up) - center;
            var corners = new[] {
                center - q / 2f - r / 2f,
                center + q / 2f - r / 2f,
                center + q / 2f + r / 2f,
                center - q / 2f + r / 2f,
                center - q / 2f - r / 2f,
            };
            Handles.color = erase
                ? new Color(1f, 0.25f, 0.25f, 1f)
                : new Color(0.2f, 0.85f, 1f, 1f);
            Handles.DrawAAPolyLine(4f, corners);
        }

        private static string RendererLabel(PixelLabTileRenderer renderer)
        {
            if (renderer == null)
            {
                return "No layer";
            }
            var terrainNames = (renderer.availableTiles ?? Array.Empty<PixelLabPaintTile>())
                .Where(tile => tile != null && tile.IsTerrainBrush && tile.paintableTerrain)
                .OrderBy(tile => tile.paintTerrain)
                .Select(TerrainLabel)
                .Distinct()
                .ToArray();
            if (terrainNames.Length > 0)
            {
                var height = renderer.layerY == 0
                    ? "Y0 - GROUND"
                    : "Y" + renderer.layerY + " - ELEVATED +" + renderer.layerY;
                return height + " • " + string.Join(" ↔ ", terrainNames);
            }
            return renderer.transform.parent == null
                ? renderer.name
                : renderer.transform.parent.name;
        }

        private static string TerrainLabel(PixelLabPaintTile tile)
        {
            if (tile == null)
            {
                return "Choose a terrain";
            }
            return tile.name.Replace("AUTO PAINT - ", string.Empty);
        }
    }

    internal sealed class PixelLabPackagePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            if (importedAssets.Contains(PixelLabMapImporter.PackageManifestAssetPath))
            {
                PixelLabMapImporter.ScheduleImport();
            }
        }
    }
}
#endif
