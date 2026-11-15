using System.IO;
using UnityEditor;
using UnityEngine;

namespace Glowrunner.EditorTools
{
    /// <summary>
    /// Paints the app icon (1024×1024, macOS rounded-square style) from signed-distance shapes
    /// in the game's neon look, saves it as a PNG and sets it as the project's default icon.
    /// </summary>
    public static class GlowrunnerIcon
    {
        public const string IconPath = GlowrunnerSetup.Root + "/Icon/GlowrunnerIcon.png";
        const int Size = 1024;

        static readonly Vector3 Cyan = new Vector3(0.25f, 0.9f, 1f);
        static readonly Vector3 OrbCol = new Vector3(0.2f, 0.95f, 1f);
        static readonly Vector3 Red = new Vector3(1f, 0.15f, 0.25f);
        static readonly Vector3 Pink = new Vector3(1f, 0.3f, 0.8f);
        static readonly Vector3 Spark = new Vector3(1f, 0.85f, 0.5f);

        // Wall blocks drawn as neon outlines: (x0, y0, x1, y1), y pointing down.
        static readonly Vector4[] Walls =
        {
            new Vector4(180, 180, 440, 330), new Vector4(584, 180, 844, 330),
            new Vector4(180, 420, 300, 660), new Vector4(724, 420, 844, 660),
            new Vector4(180, 750, 440, 844), new Vector4(584, 750, 844, 844),
        };

        static readonly Vector2[] Sparks =
        {
            new Vector2(240, 375), new Vector2(320, 375), new Vector2(400, 375),
            new Vector2(624, 375), new Vector2(704, 375), new Vector2(784, 375), new Vector2(512, 705), new Vector2(592, 705), new Vector2(672, 705),
            new Vector2(752, 705), new Vector2(832, 705), new Vector2(380, 540), new Vector2(644, 540),
        };

        static readonly Vector2 OrbCenter = new Vector2(512, 540);
        static readonly Vector2 CoreCenter = new Vector2(250, 705);
        static readonly Vector2 SentinelCenter = new Vector2(512, 262);

        [MenuItem("Glowrunner/Generate App Icon")]
        public static void Generate()
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color32[Size * Size];

            for (int py = 0; py < Size; py++)
            {
                for (int px = 0; px < Size; px++)
                {
                    // 2×2 supersampling for smooth edges.
                    Vector4 acc = Vector4.zero;
                    for (int s = 0; s < 4; s++)
                    {
                        var p = new Vector2(px + 0.25f + 0.5f * (s & 1), py + 0.25f + 0.5f * (s >> 1));
                        acc += Shade(p);
                    }
                    acc /= 4f;
                    // Texture rows go bottom-up.
                    pixels[(Size - 1 - py) * Size + px] = new Color(acc.x, acc.y, acc.z, acc.w);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
            File.WriteAllBytes(IconPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Size;
            importer.isReadable = true;
            importer.SaveAndReimport();

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log("Glowrunner: app icon generated at " + IconPath);
        }

        /// <summary>Returns premultiplied-free RGBA for one sample point.</summary>
        static Vector4 Shade(Vector2 p)
        {
            // macOS icon body: rounded square inset 100px with a soft drop shadow.
            var c = new Vector2(512, 512);
            float body = RoundBox(p, c, new Vector2(412, 412), 185f);
            float bodyAlpha = Mathf.Clamp01(0.5f - body);

            if (bodyAlpha <= 0f)
            {
                float sh = RoundBox(p - new Vector2(0, 12), c, new Vector2(412, 412), 185f);
                float a = 0.35f * Mathf.Exp(-Mathf.Max(sh, 0f) / 18f);
                return new Vector4(0f, 0f, 0f, a);
            }

            // Background: deep navy gradient with a cyan bloom behind the orb.
            float t = p.y / Size;
            Vector3 col = Vector3.Lerp(new Vector3(0.04f, 0.05f, 0.14f), new Vector3(0.01f, 0.01f, 0.04f), t);
            float dOrbCentre = (p - OrbCenter).magnitude;
            col += OrbCol * 0.22f * Mathf.Exp(-dOrbCentre / 230f);

            // Neon wall outlines.
            float wallDist = float.MaxValue;
            foreach (var w in Walls)
            {
                var wc = new Vector2((w.x + w.z) * 0.5f, (w.y + w.w) * 0.5f);
                var wh = new Vector2((w.z - w.x) * 0.5f, (w.w - w.y) * 0.5f);
                float d = RoundBox(p, wc, wh, 22f);
                if (d < 0f) col = Vector3.Lerp(col, new Vector3(0.05f, 0.07f, 0.2f), 0.85f); // wall top
                wallDist = Mathf.Min(wallDist, Mathf.Abs(d) - 6f);
            }
            col += Neon(wallDist, Cyan, 1.1f, 16f, 0.55f);

            // Sparks: small glowing diamonds.
            foreach (var s in Sparks)
            {
                var q = p - s;
                float d = (Mathf.Abs(q.x) + Mathf.Abs(q.y)) * 0.7071f - 11f;
                col += Neon(d, Spark, 1f, 10f, 0.5f);
            }

            // Power core.
            col += Neon((p - CoreCenter).magnitude - 26f, Pink, 1f, 28f, 0.9f);

            // Sentinel drone: saucer disc + dome + antenna with beacon, eyes looking down at the orb.
            {
                var sc = SentinelCenter;
                float dome = Mathf.Max((p - sc).magnitude - 44f, p.y - (sc.y + 4f));
                float disc = Ellipse(p, sc + new Vector2(0, 6), 58f, 14f);
                float mast = RoundBox(p, sc + new Vector2(0, -56), new Vector2(4.5f, 16f), 2f);
                float beacon = (p - (sc + new Vector2(0, -76))).magnitude - 10f;
                float d = Mathf.Min(Mathf.Min(dome, disc), Mathf.Min(mast, beacon));
                col += Neon(d, Red, 1f, 24f, 0.7f, shaded: true, p: p, centre: sc + new Vector2(-14, -22), radius: 70f);

                var look = new Vector2(0, 6);
                for (int i = -1; i <= 1; i += 2)
                {
                    var e = sc + new Vector2(i * 17f, -12f);
                    float eye = (p - e).magnitude - 11f;
                    col = Vector3.Lerp(col, new Vector3(0.95f, 0.95f, 1f), Mathf.Clamp01(0.5f - eye));
                    float pupil = (p - e - look * 0.7f).magnitude - 5.5f;
                    col = Vector3.Lerp(col, new Vector3(0.02f, 0.02f, 0.06f), Mathf.Clamp01(0.5f - pupil));
                }
            }

            // Orbit ring behind the orb (top half), then the orb, then the ring's front half and satellites.
            float ringD = Ellipse(p, OrbCenter, 175f, 58f);
            bool ringFront = p.y > OrbCenter.y;
            if (!ringFront) col += Neon(Mathf.Abs(ringD) - 2.5f, new Vector3(0.7f, 1f, 1f), 0.55f, 8f, 0.25f);

            float orb = dOrbCentre - 118f;
            col += OrbCol * 0.5f * Mathf.Exp(-Mathf.Max(orb, 0f) / 45f);
            if (orb < 1f)
            {
                // Sphere shading with a soft highlight up and to the left.
                var n = (p - OrbCenter) / 118f;
                float z = Mathf.Sqrt(Mathf.Max(0f, 1f - n.sqrMagnitude));
                var light = new Vector3(-0.45f, -0.55f, 0.7f).normalized;
                float diff = Mathf.Max(0f, n.x * light.x + n.y * light.y + z * light.z);
                Vector3 sphere = Vector3.Lerp(OrbCol * 0.55f, OrbCol, diff);
                float spec = Mathf.Pow(diff, 28f);
                sphere += Vector3.one * spec * 0.9f;
                // Bright core like the emissive orb.
                sphere = Vector3.Lerp(sphere, new Vector3(0.85f, 1f, 1f), 0.25f * z);
                col = Vector3.Lerp(col, sphere, Mathf.Clamp01(0.5f - orb));
            }

            if (ringFront) col += Neon(Mathf.Abs(ringD) - 2.5f, new Vector3(0.7f, 1f, 1f), 0.75f, 8f, 0.3f);
            foreach (float ang in new[] { 200f, 330f, 75f })
            {
                float a = ang * Mathf.Deg2Rad;
                var sp = OrbCenter + new Vector2(Mathf.Cos(a) * 175f, Mathf.Sin(a) * 58f);
                bool front = sp.y > OrbCenter.y;
                if (!front && dOrbCentre < 118f) continue; // hidden behind the orb
                col += Neon((p - sp).magnitude - 13f, new Vector3(0.85f, 1f, 1f), 1f, 14f, 0.6f);
            }

            // Subtle inner rim light on the icon edge.
            col += new Vector3(0.3f, 0.5f, 1f) * 0.12f * Mathf.Exp(body / 6f);

            col = new Vector3(Soft(col.x), Soft(col.y), Soft(col.z));
            return new Vector4(col.x, col.y, col.z, bodyAlpha);
        }

        /// <summary>A solid neon shape (distance d ≤ 0 is inside) with an exponential glow around it.</summary>
        static Vector3 Neon(float d, Vector3 color, float core, float falloff, float glow,
            bool shaded = false, Vector2 p = default, Vector2 centre = default, float radius = 1f)
        {
            float inside = Mathf.Clamp01(0.5f - d);
            Vector3 fill = color * core;
            if (shaded)
            {
                float h = Mathf.Clamp01(1f - (p - centre).magnitude / radius);
                fill = Vector3.Lerp(color * 0.75f, Vector3.Lerp(color, Vector3.one, 0.45f), h);
            }
            return fill * inside + color * glow * Mathf.Exp(-Mathf.Max(d, 0f) / falloff) * (1f - inside);
        }

        static float RoundBox(Vector2 p, Vector2 c, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        /// <summary>Approximate signed distance to an axis-aligned ellipse.</summary>
        static float Ellipse(Vector2 p, Vector2 c, float rx, float ry)
        {
            var q = p - c;
            float k = new Vector2(q.x / rx, q.y / ry).magnitude;
            float grad = new Vector2(q.x / (rx * rx), q.y / (ry * ry)).magnitude;
            return k * (k - 1f) / Mathf.Max(grad, 1e-4f);
        }

        static float Soft(float x) => x <= 0.8f ? x : 0.8f + 0.2f * (1f - Mathf.Exp(-(x - 0.8f) / 0.2f));
    }
}
