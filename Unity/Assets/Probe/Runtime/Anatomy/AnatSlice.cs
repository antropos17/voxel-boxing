using Unity.Mathematics;
using UnityEngine;

namespace VoxProbe
{
    /// Срез анатомической головы плоскостью: точка o, направления u (вправо) и v (вверх), размер w×h см, шаг px см на пиксель.
    /// Цвет — «на срезе» из таблицы материалов, разброс по ячейке, рисунок: волокна мышц по направлению, решётка губчатой кости,
    /// дольки жира; тонкая тёмная линия на границе материалов.
    public static class AnatSlice
    {
        public struct Result { public Color32[] px; public int w, h; public int[] matCount; }

        public static Result Render(AnatHead a, float3 o, float3 u, float3 v, float w, float h, float px, AnatState st = null)
        {
            u = math.normalize(u); v = math.normalize(v);
            float3 n = math.normalize(math.cross(u, v));
            int W = (int)math.ceil(w / px), H = (int)math.ceil(h / px);
            var mats = new byte[W * H]; var parts = new byte[W * H]; var cells = new int[W * H]; var hcell = new int[W * H];
            var res = new Result { px = new Color32[W * H], w = W, h = H, matCount = new int[AnatMat.Count] };
            for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                float3 p = o + u * ((i + 0.5f) * px - w * 0.5f) + v * ((j + 0.5f) * px - h * 0.5f);
                byte m = a.Sample(p, out byte part, out AnatGrid g);
                int k = i + W * j;
                mats[k] = m; parts[k] = part; hcell[k] = g == a.head ? g.Lookup(p) : -1;
                cells[k] = g != null ? g.Lookup(p) * 7 + (g == a.head ? 0 : g.name.GetHashCode() & 0xFFFF) : 0;
                if (m < AnatMat.Count) res.matCount[m]++;
            }
            var bg = new Color32(24, 26, 30, 255);
            for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                int k = i + W * j;
                byte m = mats[k];
                if (m == 0) { res.px[k] = bg; continue; }
                float3 p = o + u * ((i + 0.5f) * px - w * 0.5f) + v * ((j + 0.5f) * px - h * 0.5f);
                Color c;
                if (m == AnatMat.Air) c = new Color(0.06f, 0.07f, 0.09f);
                else
                {
                    var d = AnatMat.Table[m];
                    c = d.cut;
                    float hsh = Hash(cells[k]) * 2 - 1;
                    c *= 1 + d.vary * 0.6f * hsh;
                    int pt = parts[k] & 0x7F;
                    if (m == AnatMat.Muscle && AnatPart.IsMuscle(pt))
                    {
                        float3 f = AnatGeo.Fiber(pt, p);
                        float3 fp = f - n * math.dot(f, n);
                        float s;
                        if (math.length(fp) > 0.35f) { float3 perp = math.normalize(math.cross(n, fp)); s = math.sin(math.dot(p, perp) * 2 * math.PI / 0.07f); }
                        else { float3 c2 = math.floor(p / 0.06f); s = Hash3(c2) > 0.25f ? 0.6f : -1f; }  // поперёк волокон — пучки
                        c *= 1 + 0.12f * s;
                    }
                    else if (m == AnatMat.Spongy) { if (Hash3(math.floor(p / 0.045f)) > 0.62f) c *= 0.62f; }
                    else if (m == AnatMat.Fat || m == AnatMat.OrbFat)
                    {
                        float3 cc = p / 0.28f; float3 f3 = cc - math.floor(cc);
                        float e = math.cmin(math.min(f3, 1 - f3) * 3.5f + new float3(Hash3(math.floor(cc)) * 0.4f));
                        if (e < 0.12f) c = Color.Lerp(c, new Color(0.93f, 0.9f, 0.8f), 0.55f);                     // перегородки долек
                    }
                    if ((parts[k] & AnatPart.Suture) != 0) c *= 0.55f;
                }
                // этап B: кровь и синяк на срезе (st — состояния головы)
                if (st != null && hcell[k] >= 0)
                {
                    int hi = hcell[k], hx = hi % a.head.dims.x, hy = (hi / a.head.dims.x) % a.head.dims.y, hz = hi / (a.head.dims.x * a.head.dims.y);
                    var vs = st.Get(hx, hy, hz);
                    if (vs.bruise > 0) c = Color.Lerp(c, new Color(0.36f, 0.12f, 0.30f), vs.bruise / 255f * 0.6f);
                    if (vs.blood > 0) c = Color.Lerp(c, new Color(0.42f, 0.03f, 0.04f), math.saturate(vs.blood / 255f * 1.4f) * 0.85f);
                }
                // граница материалов
                bool edge = false;
                if (i > 0 && mats[k - 1] != m) edge = true;
                if (j > 0 && mats[k - W] != m) edge = true;
                if (edge) c *= 0.72f;
                c.a = 1;
                res.px[k] = c;
            }
            return res;
        }

        static float Hash(int x) { uint h = (uint)x * 0x9E3779B1u; h ^= h >> 15; h *= 0x85EBCA77u; h ^= h >> 13; return (h & 0xFFFF) / 65535f; }
        static float Hash3(float3 c) { int3 i = (int3)c; return Hash(i.x * 73856093 ^ i.y * 19349663 ^ i.z * 83492791); }

        public static byte[] Png(Result r)
        {
            var t = new Texture2D(r.w, r.h, TextureFormat.RGBA32, false);
            t.SetPixels32(r.px); t.Apply();
            var b = t.EncodeToPNG();
            Object.DestroyImmediate(t);
            return b;
        }
    }
}
