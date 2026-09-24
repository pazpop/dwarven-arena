// Génère les Animator Controllers (Blend Tree directionnel 8 directions) à
// partir des dossiers Assets/Sprites/{Player,Goblin}/{Idle,Move,...}/{direction}/
// remplis par PixelLab. À relancer si les sprites changent — supprime et
// reconstruit tout le dossier Assets/Animations/<Personnage> à chaque fois.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class GenerateDwarvenArenaAnimations
{
    private static readonly (string dir, Vector2 pos)[] Directions =
    {
        ("south", new Vector2(0, -1)),
        ("south-east", new Vector2(1, -1)),
        ("east", new Vector2(1, 0)),
        ("north-east", new Vector2(1, 1)),
        ("north", new Vector2(0, 1)),
        ("north-west", new Vector2(-1, 1)),
        ("west", new Vector2(-1, 0)),
        ("south-west", new Vector2(-1, -1)),
    };

    [MenuItem("Tools/Dwarven Arena/Générer les animations du Player")]
    public static void GeneratePlayer()
    {
        const string spriteRoot = "Assets/Sprites/Player";
        const string animRoot = "Assets/Animations/Player";
        ResetFolder(animRoot);
        FixSpriteImportSettings(spriteRoot);

        var idle = BuildBlendTree("Player_Idle", spriteRoot, animRoot, "Idle", fps: 8, loop: true);
        var move = BuildBlendTree("Player_Move", spriteRoot, animRoot, "Move", fps: 10, loop: true);
        var attack = BuildBlendTree("Player_Attack", spriteRoot, animRoot, "Attack", fps: 12, loop: false);
        var protect = BuildBlendTree("Player_Protect", spriteRoot, animRoot, "Protect", fps: 10, loop: false);

        var controller = CreateController($"{animRoot}/Player.controller");
        AddParam(controller, "MoveX", AnimatorControllerParameterType.Float);
        AddParam(controller, "MoveY", AnimatorControllerParameterType.Float);
        AddParam(controller, "IsMoving", AnimatorControllerParameterType.Bool);
        AddParam(controller, "IsProtecting", AnimatorControllerParameterType.Bool);
        AddParam(controller, "Attack", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        var idleState = sm.AddState("Idle"); idleState.motion = idle;
        var moveState = sm.AddState("Move"); moveState.motion = move;
        var attackState = sm.AddState("Attack"); attackState.motion = attack;
        var protectState = sm.AddState("Protect"); protectState.motion = protect;
        sm.defaultState = idleState;

        AddTransition(idleState, moveState, ("IsMoving", true));
        AddTransition(moveState, idleState, ("IsMoving", false));
        AddTriggerTransition(idleState, attackState, "Attack");
        AddTriggerTransition(moveState, attackState, "Attack");
        AddExitTransition(attackState, idleState);
        AddTransition(idleState, protectState, ("IsProtecting", true));
        AddTransition(moveState, protectState, ("IsProtecting", true));
        AddTransition(protectState, idleState, ("IsProtecting", false), ("IsMoving", false));
        AddTransition(protectState, moveState, ("IsProtecting", false), ("IsMoving", true));

        Save(controller);
        Debug.Log("Player.controller généré (Assets/Animations/Player/).");
    }

    [MenuItem("Tools/Dwarven Arena/Générer les animations du Goblin")]
    public static void GenerateGoblin()
    {
        const string spriteRoot = "Assets/Sprites/Goblin";
        const string animRoot = "Assets/Animations/Goblin";
        ResetFolder(animRoot);
        FixSpriteImportSettings(spriteRoot);

        // Idle du gobelin : une seule pose statique par direction (pas de sous-dossiers),
        // contrairement au nain qui a une vraie anim "breathing" à plusieurs frames.
        var idle = BuildBlendTreeFromSingleSprites("Goblin_Idle", $"{spriteRoot}/Idle", animRoot);
        var move = BuildBlendTree("Goblin_Move", spriteRoot, animRoot, "Move", fps: 10, loop: true);

        var controller = CreateController($"{animRoot}/Goblin.controller");
        AddParam(controller, "MoveX", AnimatorControllerParameterType.Float);
        AddParam(controller, "MoveY", AnimatorControllerParameterType.Float);
        AddParam(controller, "IsMoving", AnimatorControllerParameterType.Bool);

        var sm = controller.layers[0].stateMachine;
        var idleState = sm.AddState("Idle"); idleState.motion = idle;
        var moveState = sm.AddState("Move"); moveState.motion = move;
        sm.defaultState = idleState;

        AddTransition(idleState, moveState, ("IsMoving", true));
        AddTransition(moveState, idleState, ("IsMoving", false));

        Save(controller);
        Debug.Log("Goblin.controller généré (Assets/Animations/Goblin/).");
    }

    // ==================== Construction des Blend Trees ====================

    private static BlendTree BuildBlendTree(string name, string spriteRoot, string animRoot, string animType, int fps, bool loop)
    {
        var tree = NewTree(name, animRoot);
        foreach (var (dir, pos) in Directions)
        {
            string folder = $"{spriteRoot}/{animType}/{dir}";
            if (!Directory.Exists(folder))
            {
                Debug.LogWarning($"Dossier manquant, direction ignorée : {folder}");
                continue;
            }
            var clip = BuildClipFromFrames($"{name}_{dir}", folder, animRoot, fps, loop);
            tree.AddChild(clip, pos);
        }
        return tree;
    }

    // Pour un dossier de sprites statiques (un fichier par direction, ex: Goblin/Idle/south.png)
    private static BlendTree BuildBlendTreeFromSingleSprites(string name, string folder, string animRoot)
    {
        var tree = NewTree(name, animRoot);
        foreach (var (dir, pos) in Directions)
        {
            string path = $"{folder}/{dir}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"Sprite manquant, direction ignorée : {path}");
                continue;
            }
            var clip = new AnimationClip { name = $"{name}_{dir}", frameRate = 1 };
            SetSpriteCurve(clip, new[] { (0f, sprite) });
            AssetDatabase.CreateAsset(clip, $"{animRoot}/{name}_{dir}.anim");
            tree.AddChild(clip, pos);
        }
        return tree;
    }

    private static AnimationClip BuildClipFromFrames(string clipName, string folder, string animRoot, int fps, bool loop)
    {
        var sprites = Directory.GetFiles(folder, "*.png")
            .OrderBy(f => FrameNumber(f))
            .Select(f => AssetDatabase.LoadAssetAtPath<Sprite>(ToAssetPath(f)))
            .Where(s => s != null)
            .ToArray();

        var clip = new AnimationClip { name = clipName, frameRate = fps };
        SetSpriteCurve(clip, sprites.Select((s, i) => (i / (float)fps, s)).ToArray());

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.CreateAsset(clip, $"{animRoot}/{clipName}.anim");
        return clip;
    }

    private static void SetSpriteCurve(AnimationClip clip, (float time, Sprite sprite)[] frames)
    {
        var binding = new EditorCurveBinding { path = "", type = typeof(SpriteRenderer), propertyName = "m_Sprite" };
        var keyframes = frames.Select(f => new ObjectReferenceKeyframe { time = f.time, value = f.sprite }).ToArray();
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
    }

    private static BlendTree NewTree(string name, string animRoot)
    {
        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = "MoveX",
            blendParameterY = "MoveY",
        };
        AssetDatabase.CreateAsset(tree, $"{animRoot}/{name}_BlendTree.asset");
        return tree;
    }

    // ==================== Import des sprites ====================

    // Personnages en 64x64px natif -> PPU=64 = exactement 1 unité Unity à
    // Scale=1, pile la taille du CircleCollider2D (rayon 0.5, donc 1 unité de
    // diamètre) déjà réglé dans la scène. Corrige "sprite trop petit" à la
    // source, sans jamais avoir à toucher au Scale (qui redécale le hitbox).
    private const int CharacterPixelsPerUnit = 64;

    [MenuItem("Tools/Dwarven Arena/Corriger le PPU du Spike (Environment)")]
    public static void FixSpikePixelsPerUnit()
    {
        FixSpriteImportSettings("Assets/Sprites/Environment", pixelsPerUnit: 48);
        AssetDatabase.SaveAssets();
        Debug.Log("PPU du Spike réglé à 48 (taille native, 1 unité à Scale=1).");
    }

    private static void FixSpriteImportSettings(string root, int pixelsPerUnit = CharacterPixelsPerUnit)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

            bool changed = false;
            if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
            if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; changed = true; }
            if (importer.spritePixelsPerUnit != pixelsPerUnit) { importer.spritePixelsPerUnit = pixelsPerUnit; changed = true; }
            if (changed) importer.SaveAndReimport();
        }
    }

    // ==================== Animator Controller ====================

    private static AnimatorController CreateController(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
            AssetDatabase.DeleteAsset(path);
        return AnimatorController.CreateAnimatorControllerAtPath(path);
    }

    private static void AddParam(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        if (controller.parameters.Any(p => p.name == name)) return;
        controller.AddParameter(name, type);
    }

    private static void AddTransition(AnimatorState from, AnimatorState to, params (string param, bool value)[] conditions)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = 0;
        foreach (var (param, value) in conditions)
            t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, param);
    }

    private static void AddTriggerTransition(AnimatorState from, AnimatorState to, string trigger)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = 0;
        t.AddCondition(AnimatorConditionMode.If, 0, trigger);
    }

    private static void AddExitTransition(AnimatorState from, AnimatorState to)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = 1f;
        t.duration = 0;
    }

    // ==================== Utilitaires ====================

    private static void ResetFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            AssetDatabase.DeleteAsset(path);
        Directory.CreateDirectory(path);
        AssetDatabase.Refresh();
    }

    private static void Save(AnimatorController controller)
    {
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    private static int FrameNumber(string filePath)
    {
        int.TryParse(Path.GetFileNameWithoutExtension(filePath), out int n);
        return n;
    }

    private static string ToAssetPath(string path) => path.Replace("\\", "/");
}
