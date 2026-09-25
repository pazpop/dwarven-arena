// Effet visuel d'explosion (mort d'un ennemi ou du Nain) : fragments colorés qui
// giclent, tournent et rétrécissent. Un fragment de particule (pas un root +
// enfants) : chaque instance est un GameObject réutilisable piochée dans un
// ObjectPool plutôt que Instantiate/Destroy à chaque mort — évite le churn GC
// d'une trentaine d'objets par kill.
using UnityEngine;
using UnityEngine.Pool;

public class ExplosionEffect : MonoBehaviour
{
    private static Sprite squareSprite;
    private static ObjectPool<ExplosionEffect> pool;

    // Ce projet a Domain Reload + Scene Reload désactivés (voir EditorSettings) : un
    // fragment encore actif (explosion en cours) au moment d'un Stop précédent reste
    // dans la scène, orphelin et visible, puisqu'elle n'est jamais rechargée. On
    // nettoie tout fragment existant et on repart sur un pool neuf à chaque Play.
    // AfterSceneLoad (pas SubsystemRegistration, trop tôt pour un FindObjectsByType
    // fiable — la scène n'est pas garantie prête à ce stade selon la doc Unity)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ResetOnPlay()
    {
        foreach (var leftover in FindObjectsByType<ExplosionEffect>(FindObjectsInactive.Include))
        {
            Destroy(leftover.gameObject);
        }
        pool = null;
    }

    private SpriteRenderer sr;
    private Vector2 velocity;
    private float spin;
    private float life;
    private float maxLife;
    private float maxScale;

    // Point d'entrée : ExplosionEffect.Spawn(position, couleur) — minScale/maxScale
    // permettent une seconde salve de fines particules en plus des gros morceaux
    // (voir EnemyAI.Die()), sans toucher au réglage par défaut
    public static void Spawn(Vector3 position, Color color, int fragmentCount = 22, float speed = 5f,
        float minScale = 0.05f, float maxScale = 0.22f)
    {
        if (pool == null)
        {
            pool = new ObjectPool<ExplosionEffect>(
                createFunc: CreateFragment,
                actionOnGet: f => f.gameObject.SetActive(true),
                actionOnRelease: f => f.gameObject.SetActive(false),
                // Le PoolManager interne de Unity vide tous les pools enregistrés à la
                // sortie du Play — à ce moment-là, Unity a déjà détruit les fragments
                // (objets créés en Play) avant que ce callback s'exécute, donc "f" peut
                // être un "fake null" (wrapper C# encore là, objet natif déjà détruit)
                actionOnDestroy: f => { if (f != null) Destroy(f.gameObject); },
                defaultCapacity: 32, maxSize: 256);
        }

        for (int i = 0; i < fragmentCount; i++)
        {
            pool.Get().Init(position, color, speed, minScale, maxScale);
        }
    }

    private static ExplosionEffect CreateFragment()
    {
        var go = new GameObject("ExplosionFragment");
        var frag = go.AddComponent<ExplosionEffect>();
        frag.sr = go.AddComponent<SpriteRenderer>();
        frag.sr.sprite = GetSquareSprite();
        frag.sr.sortingOrder = 30010; // Au-dessus des décors (jusqu'à 30003) : jamais caché par le décor
        return frag;
    }

    private void Init(Vector3 position, Color color, float speed, float minScale, float maxScaleParam)
    {
        transform.position = position;
        transform.rotation = Quaternion.identity;
        maxScale = Random.Range(minScale, maxScaleParam);
        // Taille correcte tout de suite (pas Vector3.one) : pour un fragment réutilisé
        // du pool, Unity ne garantit pas que Update() tourne dès la même frame que sa
        // réactivation (contrairement à un fragment fraîchement créé) — sans ça, il
        // pouvait rester visible à taille pleine un moment avant de se corriger
        transform.localScale = Vector3.one * maxScale;
        sr.color = color;

        // Direction aléatoire en cercle (explosion radiale)
        float angle = Random.Range(0f, Mathf.PI * 2f);
        var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        velocity = dir * Random.Range(speed * 0.5f, speed);
        spin = Random.Range(-400f, 400f);
        life = 0f;
        maxLife = Random.Range(0.35f, 0.6f);
    }

    // Génère un petit carré blanc par code — aucun asset nécessaire
    private static Sprite GetSquareSprite()
    {
        if (squareSprite == null)
        {
            Texture2D tex = new Texture2D(8, 8);
            Color[] pixels = new Color[64];
            for (int i = 0; i < 64; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            squareSprite = Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 8f);
        }
        return squareSprite;
    }

    private void Update()
    {
        life += Time.deltaTime;
        float t = life / maxLife;

        // Déplacement + décélération
        transform.position += (Vector3)(velocity * Time.deltaTime);
        velocity *= 1f - 5f * Time.deltaTime; // Freinage marqué : rayon d'explosion contenu, plus "lourd"
        transform.Rotate(0f, 0f, spin * Time.deltaTime);

        // Rétrécissement + fondu
        transform.localScale = Vector3.one * Mathf.Lerp(maxScale, 0f, t);
        Color c = sr.color;
        c.a = 1f - t;
        sr.color = c;

        if (life >= maxLife) pool.Release(this);
    }
}
