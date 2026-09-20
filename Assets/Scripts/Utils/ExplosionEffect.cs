using System.Collections.Generic;
using UnityEngine;

public class ExplosionEffect : MonoBehaviour
{
    private static Sprite squareSprite;

    private class Fragment
    {
        public Transform tr;
        public SpriteRenderer sr;
        public Vector2 velocity;
        public float spin;
        public float life;
        public float maxLife;
    }

    private readonly List<Fragment> fragments = new List<Fragment>();

    // Point d'entrée : ExplosionEffect.Spawn(position, couleur)
    public static void Spawn(Vector3 position, Color color, int fragmentCount = 14, float speed = 5f)
    {
        GameObject root = new GameObject("Explosion");
        root.transform.position = position;
        var fx = root.AddComponent<ExplosionEffect>();
        fx.Run(position, color, fragmentCount, speed);
    }

    private void Run(Vector3 position, Color color, int count, float speed)
    {
        for (int i = 0; i < count; i++)
        {
            var frag = new GameObject("frag");
            frag.transform.SetParent(transform, false);
            frag.transform.localPosition = Vector3.zero;

            var sr = frag.AddComponent<SpriteRenderer>();
            sr.sprite = GetSquareSprite();
            sr.color = color;
            frag.transform.localScale = Vector3.one * Random.Range(0.15f, 0.35f);

            // Direction aléatoire en cercle (explosion radiale)
            float angle = Random.Range(0f, Mathf.PI * 2f);
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            fragments.Add(new Fragment
            {
                tr = frag.transform,
                sr = sr,
                velocity = dir * Random.Range(speed * 0.5f, speed),
                spin = Random.Range(-540f, 540f),
                life = 0f,
                maxLife = Random.Range(0.4f, 0.7f)
            });
        }
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
        bool anyAlive = false;

        foreach (var frag in fragments)
        {
            if (frag == null || frag.tr == null) continue;
            anyAlive = true;

            frag.life += Time.deltaTime;
            float t = frag.life / frag.maxLife;

            // Déplacement + décélération
            frag.tr.position += (Vector3)(frag.velocity * Time.deltaTime);
            frag.velocity *= 1f - 3f * Time.deltaTime;
            frag.tr.Rotate(0f, 0f, frag.spin * Time.deltaTime);

            // Rétrécissement + fondu
            frag.tr.localScale = Vector3.one * Mathf.Lerp(0.3f, 0f, t);
            Color c = frag.sr.color;
            c.a = 1f - t;
            frag.sr.color = c;

            if (frag.life >= frag.maxLife) Destroy(frag.tr.gameObject);
        }

        if (!anyAlive) Destroy(gameObject);
    }
}