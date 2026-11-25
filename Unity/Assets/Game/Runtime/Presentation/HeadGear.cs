using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxProbe;

namespace VoxGame
{
    // Воксельные накладки анатомической головы: причёска, борода и усы, головной убор, пластырь на брови, серьга.
    // Формы — оболочки над поверхностью самой головы (AnatGeo.Body, см; начало — слуховой проход на средней линии,
    // +Y вверх, +Z к лицу, +X — левая сторона человека, на экране слева), поэтому сидят по её размерам.
    // Глаза, нос и рот не закрываются ничем.
    // Иерархия: mount (headMount тела) → holder (снимает масштаб тела: голова не масштабируется) → skull (поза черепа
    // по изгибу шеи AnatSkin) → fit (настроечный сдвиг и масштаб) → части.
    // НЕ MonoBehaviour: Build / Tick / Dispose вызывает владелец (FoeBody).
    public class HeadGear
    {
        // ---- Посадка (подгоняется по снимкам) ------------------------------------------------------
        static readonly Vector3 FitOffset = Vector3.zero;  // см, общий сдвиг накладок в осях головы
        const float FitScale = 1.00f;                      // общий масштаб накладок вокруг начала головы

        // Причёски: толщина над кожей, см (анатомические волосы — слой 0.2–0.3 см, его надо перекрыть)
        const float BaldOut = 0.55f, BuzzOut = 0.60f, SlickOut = 0.70f;
        const float FullOut = 1.70f, FullFringe = 1.00f;   // шапка волос: на макушке и у лба
        const float MohawkH = 3.60f, MohawkHalfW = 1.60f;  // ирокез: высота гребня и полуширина
        static readonly Vector3 TailA = new Vector3(0f, 3.2f, -11.4f), TailB = new Vector3(0f, -7.0f, -13.0f);  // хвост

        // Борода
        const float BeardIn = 0.15f, BeardOut = 0.90f, BeardChin = 2.00f, MustacheOut = 0.75f;

        // Повязка: средняя линия y = BandY + BandTilt·z, полуширина BandHalf
        const float BandY = 5.9f, BandTilt = 0.14f, BandHalf = 1.35f, BandThick = 0.75f;

        // Шлем: толщина, край над бровями, полуширина выреза лица, низ сбоку и сзади
        const float HelmetOut = 1.80f, HelmetEar = 1.00f, HelmetBrowY = 6.4f, HelmetOpenX = 5.0f;
        const float HelmetLowSide = -5.0f, HelmetLowBack = -4.0f;
        static readonly Vector3 StrapA = new Vector3(5.6f, -4.4f, 0.8f), StrapB = new Vector3(3.9f, -9.6f, 4.0f),
                                StrapC = new Vector3(0f, -11.4f, 5.2f);

        // Шапочка: край y = BeanieY + BeanieTilt·z, отворот BeanieCuff см
        const float BeanieY = 4.7f, BeanieTilt = 0.28f, BeanieOut = 1.0f, BeanieCuff = 2.2f;

        // Пластырь на брови: центр (x — со стороны side), наклон, серьга-кольцо
        const float PlasterX = 4.7f, PlasterY = 5.2f, PlasterAngle = 22f;
        const float RingY = -3.0f, RingZ = -1.05f, RingR = 0.62f, RingWire = 0.16f;

        // Шаг решёток, см
        const float HairCell = 0.35f, BeardCell = 0.30f, WearCell = 0.40f, SmallCell = 0.15f, RingCell = 0.10f;

        // Номера цветов
        const byte PMain = 1, PDark = 2, PLight = 3, PSkin = 4, PTrim = 5, PMetal = 6;

        // ---- Объекты -------------------------------------------------------------------------------
        Transform holder, skull, fit, hair, beard, wear, extras;
        Transform dropParent;
        Material mat;
        readonly List<Mesh> meshes = new List<Mesh>(8);
        HeadLook look;
        float hairOut;        // толщина причёски над кожей (на ней сидит головной убор)
        bool visible = true;

        // Сброшенный головной убор
        bool dropped, resting;
        Vector3 dropVel, dropSpin;
        float floorY;
        Vector3 wearLocalPos; Quaternion wearLocalRot;

        public bool HasWear => wear != null;
        public bool WearDropped => dropped;

        // ---- Сборка --------------------------------------------------------------------------------

        public void Build(Transform mount, FighterDef def, Material material)
        {
            mat = material;
            look = def.head;
            dropParent = mount.root;

            holder = new GameObject("headGear").transform;
            holder.SetParent(mount, false);
            float ms = mount.lossyScale.x;
            holder.localScale = Vector3.one / (ms > 1e-4f ? ms : 1f);

            skull = new GameObject("skull").transform;
            skull.SetParent(holder, false);
            fit = new GameObject("fit").transform;
            fit.SetParent(skull, false);
            fit.localPosition = FitOffset * 0.01f;
            fit.localScale = Vector3.one * FitScale;

            hairOut = HairThickness(look.hair);
            hair = BuildHair();
            beard = BuildBeard();
            wear = BuildWear();
            BuildExtras();
            if (wear != null) { wearLocalPos = wear.localPosition; wearLocalRot = wear.localRotation; }
            SyncToSkull();
        }

        static float HairThickness(HairStyle h)
        {
            switch (h)
            {
                case HairStyle.Buzz:     return BuzzOut;
                case HairStyle.FullHair: return FullOut;
                case HairStyle.Ponytail: return SlickOut;
                default:                 return BaldOut;
            }
        }

        Transform Part(string name, Vector3 mn, Vector3 mx, float cell, VoxModel.Sdf sdf, VoxModel.Paint paint,
                       Color32[] pal, float[] ao = null)
        {
            var m = VoxModel.FromSdf(mn, mx, cell, sdf, paint);
            var mesh = m.BuildMesh(name, pal, mn * 0.01f, cell * 0.01f, 0.3f, ao);
            if (mesh.vertexCount == 0) { GameUtil.Destroy(mesh); return null; }
            meshes.Add(mesh);
            return VoxModel.Spawn(name, fit, mesh, mat);
        }

        // Оболочка над кожей головы между inner и outer (см). Далеко от неё — настоящее расстояние (Fill пропускает
        // пустоту), рядом вне зоны — небольшое положительное (пусто, без пропуска).
        static float Shell(float b, float inner, float outer, bool zone)
        {
            float s = Mathf.Max(b - outer, inner - b);
            if (s > 1f) return s;
            return zone ? s : Mathf.Max(s, 0.3f);
        }

        static float Smooth(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float Hash3(float x, float y, float z, float cell)
        {
            uint h = (uint)(Mathf.FloorToInt(x / cell) * 73856093) ^ (uint)(Mathf.FloorToInt(y / cell) * 19349663)
                   ^ (uint)(Mathf.FloorToInt(z / cell) * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }

        static float Body(float x, float y, float z) => AnatGeo.Body(x, y, z);

        // ---- Причёска -------------------------------------------------------------------------------

        Transform BuildHair()
        {
            Color32 hc = look.hairColor;
            var pal = new Color32[]
            {
                default, hc, Color.Lerp(hc, Color.black, 0.35f), Color.Lerp(hc, Color.white, 0.18f),
                BoxerParts.HeadSkin, Color.Lerp(hc, Color.black, 0.2f),
            };
            var ao = new[] { 1f, 0.5f, 0.5f, 0.5f, 0.3f, 0.5f };
            var mn = new Vector3(-10.5f, -7f, -14f);
            var mx = new Vector3(10.5f, 17.5f, 12f);

            switch (look.hair)
            {
                case HairStyle.Buzz:
                    return Part("hairBuzz", mn, mx, HairCell,
                        (x, y, z) => Shell(Body(x, y, z), 0f, BuzzOut, AnatGeo.HairZone(x, y + 0.1f, z)),
                        (x, y, z) => Hash3(x, y, z, HairCell) < 0.35f ? PDark : PMain, pal, ao);

                case HairStyle.FullHair:
                    return Part("hairFull", mn, mx, HairCell,
                        (x, y, z) => Shell(Body(x, y, z), 0f, FullThick(x, y, z), AnatGeo.HairZone(x, y + 0.4f, z)),
                        (x, y, z) =>
                        {
                            float n = Hash3(x, y, z, 1.1f);
                            return n < 0.25f ? PDark : n > 0.8f ? PLight : PMain;
                        }, pal, ao);

                case HairStyle.Mohawk:
                    return Part("hairMohawk", mn, mx, HairCell,
                        (x, y, z) =>
                        {
                            bool crest = Mathf.Abs(x) < MohawkHalfW;
                            return Shell(Body(x, y, z), 0f, crest ? MohawkTop(z) : BaldOut, AnatGeo.HairZone(x, y + 0.3f, z));
                        },
                        (x, y, z) =>
                        {
                            if (Mathf.Abs(x) >= MohawkHalfW) return PSkin;
                            return Body(x, y, z) < 0.9f ? PDark : Hash3(x, y, z, 0.7f) < 0.3f ? PLight : PMain;
                        }, pal, ao);

                case HairStyle.Ponytail:
                    return Part("hairTail", new Vector3(-10.5f, -9.5f, -15.5f), mx, HairCell,
                        (x, y, z) =>
                        {
                            float d = Shell(Body(x, y, z), 0f, SlickOut, AnatGeo.HairZone(x, y + 0.3f, z));
                            return Mathf.Min(d, TailSdf(x, y, z));
                        },
                        (x, y, z) =>
                        {
                            if (Vector3.Distance(new Vector3(x, y, z), TailA) < 1.6f && y < TailA.y + 0.2f) return PTrim;  // резинка
                            float s = Mathf.Repeat(x * 1.3f + z * 0.2f, 1f);   // зачёс назад — полосы
                            return s < 0.3f ? PDark : PMain;
                        }, WithTrim(pal, look.wearColor), ao);

                default:   // лысый: кожа поверх анатомических волос
                    return Part("hairBald", mn, mx, HairCell,
                        (x, y, z) => Shell(Body(x, y, z), 0f, BaldOut, AnatGeo.HairZone(x, y + 0.3f, z)),
                        (x, y, z) => PSkin, pal, ao);
            }
        }

        static Color32[] WithTrim(Color32[] pal, Color trim)
        {
            var p = (Color32[])pal.Clone();
            p[PTrim] = trim;
            return p;
        }

        // Шапка волос: у лба тоньше (чёлка не опускается на лицо), кустами
        static float FullThick(float x, float y, float z) =>
            Mathf.Lerp(FullOut, FullFringe, Smooth(3f, 7f, z)) * (0.85f + 0.3f * Hash3(x, y, z, 1.2f));

        // Гребень ирокеза: ниже у лба и на затылке, зубцами
        static float MohawkTop(float z)
        {
            float h = MohawkH * Mathf.Lerp(0.55f, 1f, Smooth(8f, 3f, z)) * Mathf.Lerp(0.6f, 1f, Smooth(-11f, -5f, z));
            return h * (0.78f + 0.22f * Mathf.Abs(Mathf.Sin(z * 0.9f)));
        }

        static float TailSdf(float x, float y, float z) =>
            Mathf.Min(VoxModel.Cap(x, y, z, TailA.x, TailA.y, TailA.z, TailB.x, TailB.y, TailB.z, 1.6f, 0.6f),
                      VoxModel.Ell(x - TailA.x, y - TailA.y - 0.6f, z - TailA.z - 0.6f, 1.9f, 1.6f, 1.7f));

        // ---- Борода и усы ---------------------------------------------------------------------------

        static bool MustacheZone(float ax, float y, float z)
        {
            if (z < 5.2f || ax > 3.4f) return false;
            float top = ax < 2.2f ? -3.05f : -2.6f;                         // под ноздрями
            float low = -3.85f - (ax > 2.3f ? (ax - 2.3f) * 1.3f : 0f);     // кончики опущены к углам рта
            return y < top && y > low;
        }

        static bool MouthHole(float ax, float y, float z) => ax < 2.75f && y > -6.6f && y < -3.85f && z > 4.5f;

        static bool BeardZone(BeardStyle s, float x, float y, float z)
        {
            float ax = Mathf.Abs(x);
            if (MustacheZone(ax, y, z) && s != BeardStyle.Chinstrap) return true;
            if (MouthHole(ax, y, z)) return false;
            switch (s)
            {
                case BeardStyle.Horseshoe:
                    return z > 3.5f && ax > 2.55f && ax < 3.6f && y > -8.8f && y < -3.0f;
                case BeardStyle.Goatee:
                    return z > 4.0f && ax < 2.4f && y < -6.85f && y > -11.2f;
                case BeardStyle.Chinstrap:
                {
                    float jl = -10.4f + 0.13f * ax * ax;   // нижний край лица
                    bool strip = z > -2.2f && ax < 6.2f && y < jl + 1.6f && y > jl - 2.8f;
                    bool burn = ax > 5.0f && z > 0.9f && z < 3.4f && y >= jl && y < 1.0f;
                    return strip || burn;
                }
                case BeardStyle.Full:
                {
                    if (z < -2.4f || y < -13.2f) return false;
                    if (ax > 5.4f && y > -3.5f && z < 1.0f) return false;       // ухо
                    if (ax < 1.9f && y > -3.0f) return false;                   // нос
                    float top = ax > 5.4f ? 1.0f : Mathf.Lerp(-3.4f, -1.4f, Mathf.InverseLerp(2.6f, 5.4f, ax));
                    return y < top;
                }
                default:
                    return false;   // только усы
            }
        }

        Transform BuildBeard()
        {
            var s = look.beard;
            if (s == BeardStyle.None) return null;
            Color32 hc = look.hairColor;
            var pal = new Color32[] { default, hc, Color.Lerp(hc, Color.black, 0.35f), Color.Lerp(hc, Color.white, 0.15f) };
            var ao = new[] { 1f, 0.5f, 0.5f, 0.5f };
            return Part("beard", new Vector3(-8.5f, -14.5f, -3f), new Vector3(8.5f, 2f, 12.5f), BeardCell,
                (x, y, z) =>
                {
                    float ax = Mathf.Abs(x), outer;
                    if (s == BeardStyle.Mustache || (s != BeardStyle.Chinstrap && MustacheZone(ax, y, z)))
                        outer = MustacheOut;
                    else if (s == BeardStyle.Full || s == BeardStyle.Goatee)
                        outer = Mathf.Lerp(BeardOut, BeardChin, Smooth(-7.5f, -10f, y) * (1f - Smooth(2.5f, 5f, ax)));
                    else
                        outer = BeardOut;
                    return Shell(Body(x, y, z), BeardIn, outer, BeardZone(s, x, y, z));
                },
                (x, y, z) =>
                {
                    float n = Hash3(x, y, z, 0.6f);
                    return n < 0.3f ? PDark : n > 0.85f ? PLight : PMain;
                }, pal, ao);
        }

        // ---- Головной убор ---------------------------------------------------------------------------

        Transform BuildWear()
        {
            Color32 wc = look.wearColor, tc = look.wearTrim;
            var pal = new Color32[]
            {
                default, wc, Color.Lerp(wc, Color.black, 0.3f), Color.Lerp(wc, Color.white, 0.6f),
                BoxerParts.HeadSkin, tc, new Color32(198, 200, 206, 255),
            };
            var ao = new[] { 1f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
            float inner = hairOut - 0.1f;

            switch (look.wear)
            {
                case HeadWear.Headband:
                {
                    float bi = inner - 0.05f, bo = hairOut + BandThick;
                    var knot = new Vector3(0f, BandY + BandTilt * -11f, -11.0f - hairOut);
                    return Part("headband", new Vector3(-10.5f, -3f, -17f), new Vector3(10.5f, 13f, 12f), WearCell,
                        (x, y, z) =>
                        {
                            float c = BandY + BandTilt * z;
                            float d = Shell(Body(x, y, z), bi, bo, Mathf.Abs(y - c) < BandHalf);
                            // узел и два хвоста сзади
                            float ax = Mathf.Abs(x);
                            d = Mathf.Min(d, VoxModel.Ell(x - knot.x, y - knot.y, z - knot.z, 1.1f, 0.9f, 0.8f));
                            d = Mathf.Min(d, VoxModel.Cap(ax, y, z, 0.5f, knot.y, knot.z, 1.9f, knot.y - 4.2f, knot.z - 1.6f, 0.55f, 0.4f));
                            return d;
                        },
                        (x, y, z) =>
                        {
                            float c = BandY + BandTilt * z;
                            return Mathf.Abs(y - c) > BandHalf - 0.45f && z > knot.z + 1.2f ? PTrim : PMain;
                        }, pal, ao);
                }

                case HeadWear.Helmet:
                    return Part("helmet", new Vector3(-12.5f, -12.5f, -14.5f), new Vector3(12.5f, 16.5f, 12f), WearCell,
                        (x, y, z) =>
                        {
                            float d = Shell(Body(x, y, z), inner, HelmetOut + HelmetEar * EarBump(x, y, z),
                                            HelmetCover(x, y, z, 0f));
                            return Mathf.Min(d, StrapSdf(x, y, z));
                        },
                        (x, y, z) =>
                        {
                            if (StrapSdf(x, y, z) < 0.25f) return PDark;
                            if (!HelmetCover(x, y, z, 0.9f)) return PTrim;          // кант по краю
                            if (Mathf.Abs(x) < 1.0f && y > 9f) return PTrim;         // полоса по макушке
                            return PMain;
                        }, pal, ao);

                case HeadWear.Beanie:
                {
                    float top = 12.8f + hairOut + BeanieOut + 0.8f;
                    var pom = new Vector3(0f, top + 1.6f, -1.4f);
                    bool pompom = look.pompom;
                    return Part("beanie", new Vector3(-11f, -1f, -14f), new Vector3(11f, pompom ? top + 4.5f : top + 1f, 12f), WearCell,
                        (x, y, z) =>
                        {
                            float edge = BeanieY + BeanieTilt * z;
                            float outer = hairOut + BeanieOut + (y < edge + BeanieCuff ? 0.45f : 0f) + 0.8f * Smooth(9f, 13f, y);
                            float d = Shell(Body(x, y, z), inner, outer, y > edge);
                            if (pompom)
                                d = Mathf.Min(d, Vector3.Distance(new Vector3(x, y, z), pom) - 2.3f
                                                 - 0.35f * Hash3(x, y, z, 0.8f));
                            return d;
                        },
                        (x, y, z) =>
                        {
                            if (pompom && y > top) return PLight;
                            float edge = BeanieY + BeanieTilt * z;
                            if (y < edge + BeanieCuff)   // отворот в рубчик
                                return Mathf.Repeat(Mathf.Atan2(z, x) * 14f / Mathf.PI, 2f) < 1f ? PTrim : PDark;
                            return Mathf.Repeat(y * 1.6f, 1f) < 0.25f ? PDark : PMain;
                        }, pal, ao);
                }

                default:
                    return null;
            }
        }

        // Шлем закрывает череп, уши и скулы; вырез лица — от бровей вниз. m — запас (для канта по краю).
        static bool HelmetCover(float x, float y, float z, float m)
        {
            float ax = Mathf.Abs(x);
            float low = z < -3f ? HelmetLowBack : HelmetLowSide;
            if (y < low + m) return false;
            if (z > 2.0f - m && y < HelmetBrowY - m && ax < HelmetOpenX + m) return false;
            if (z > 5.2f - m && y < HelmetBrowY - m) return false;
            return true;
        }

        // Утолщение над ухом (ушная раковина отстоит от головы на ~2 см)
        static float EarBump(float x, float y, float z)
        {
            float u = (y - 0.55f) / 3.4f, v = (z + 1.05f) / 2.6f;
            return Mathf.Exp(-(u * u + v * v)) * Smooth(4f, 6f, Mathf.Abs(x));
        }

        // Подбородочный ремень шлема: от уха под подбородок
        static float StrapSdf(float x, float y, float z)
        {
            float ax = Mathf.Abs(x);
            float d = VoxModel.Cap(ax, y, z, StrapA.x, StrapA.y, StrapA.z, StrapB.x, StrapB.y, StrapB.z, 0.5f);
            return Mathf.Min(d, VoxModel.Cap(ax, y, z, StrapB.x, StrapB.y, StrapB.z, StrapC.x, StrapC.y, StrapC.z, 0.5f));
        }

        // ---- Мелочи: пластырь, серьга ---------------------------------------------------------------

        void BuildExtras()
        {
            float side = look.side < 0 ? -1f : 1f;
            if (!look.plaster && !look.earring) return;
            extras = new GameObject("extras").transform;
            extras.SetParent(fit, false);

            if (look.plaster)
            {
                var pal = new Color32[] { default, new Color32(228, 198, 160, 255), new Color32(196, 160, 126, 255) };
                float cx = side * PlasterX, cy = PlasterY;
                float a1 = side * PlasterAngle * Mathf.Deg2Rad, a2 = a1 - side * 50f * Mathf.Deg2Rad;
                var t = Part("plaster", new Vector3(cx - 2.5f, cy - 2.2f, 3.5f), new Vector3(cx + 2.5f, cy + 2.2f, 10.5f), SmallCell,
                    (x, y, z) =>
                    {
                        bool zone = z > 4.5f && (Strip(x - cx, y - cy, a1, 1.5f, 0.55f) || Strip(x - cx, y - cy, a2, 1.2f, 0.45f));
                        return Shell(Body(x, y, z), 0.12f, 0.45f, zone);
                    },
                    (x, y, z) => Strip(x - cx, y - cy, a1, 0.6f, 0.3f) ? (byte)2 : (byte)1, pal);
                if (t != null) t.SetParent(extras, false);
            }

            if (look.earring)
            {
                // мочка: ось уха (y 0.55, z −1.05), пластина отстоит от кожи головы на ~0.65 см (как в AnatGeo.EarSdf)
                float xs = 7.3f - Body(7.3f, -2.5f, RingZ);
                var c = new Vector3(side * (xs + 0.65f), RingY, RingZ);
                var pal = new Color32[] { default, new Color32(232, 190, 70, 255), new Color32(255, 232, 140, 255) };
                float e = RingR + RingWire + 0.3f;
                var t = Part("earring", c - Vector3.one * e, c + Vector3.one * e, RingCell,
                    (x, y, z) =>
                    {
                        float dy = y - c.y, dz = z - c.z, q = Mathf.Sqrt(dy * dy + dz * dz) - RingR;
                        return Mathf.Sqrt(q * q + (x - c.x) * (x - c.x)) - RingWire;
                    },
                    (x, y, z) => y > c.y ? (byte)2 : (byte)1, pal);
                if (t != null) t.SetParent(extras, false);
            }
        }

        static bool Strip(float dx, float dy, float a, float halfLen, float halfW)
        {
            float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
            float u = dx * cs + dy * sn, v = -dx * sn + dy * cs;
            return Mathf.Abs(u) < halfLen && Mathf.Abs(v) < halfW;
        }

        // ---- Кадр ------------------------------------------------------------------------------------

        public void Tick(float dt)
        {
            if (skull == null) return;
            SyncToSkull();
            if (dropped && !resting) StepDropped(dt);
        }

        // Поза черепа по изгибу шеи (AnatSkin.Total): всё выше границы жёсткой головы движется одним телом,
        // поэтому достаточно перенести одну точку черепа и взять суммарный поворот.
        // Директор может вызвать после AnatSkin.SetGlobals, чтобы накладки шли с головой без отставания на кадр.
        public void SyncToSkull()
        {
            if (skull == null) return;
            if (!AnatSkin.on)
            {
                skull.localPosition = Vector3.zero;
                skull.localRotation = Quaternion.identity;
                return;
            }
            var p0 = new float3(0f, 6f, 0f);
            float3 q0 = AnatSkin.Skin(p0, out float3x3 M);
            float3 t = q0 - math.mul(M, p0);
            skull.localPosition = (Vector3)(t * 0.01f);
            skull.localRotation = new quaternion(M);
        }

        // Полёт сброшенного убора: тяжесть, отскок от настила с потерей скорости, затем покой
        void StepDropped(float dt)
        {
            const float Rest = 0.07f;
            for (float left = Mathf.Min(dt, 0.1f); left > 1e-5f; left -= 1f / 60f)
            {
                float h = Mathf.Min(left, 1f / 60f);
                dropVel += Vector3.down * 9.8f * h;
                Vector3 p = wear.position + dropVel * h;
                float w = dropSpin.magnitude;
                if (w > 1e-4f) wear.rotation = Quaternion.AngleAxis(w * h * Mathf.Rad2Deg, dropSpin / w) * wear.rotation;
                if (p.y < floorY + Rest)
                {
                    p.y = floorY + Rest;
                    if (dropVel.y < 0f) dropVel.y = -dropVel.y * 0.3f;
                    dropVel.x *= 0.6f; dropVel.z *= 0.6f;
                    dropSpin *= 0.6f;
                    if (dropVel.sqrMagnitude < 0.04f) { resting = true; wear.position = p; return; }
                }
                wear.position = p;
            }
        }

        // ---- Управление ------------------------------------------------------------------------------

        public void SetVisible(bool on)
        {
            visible = on;
            if (holder != null) holder.gameObject.SetActive(on);
            if (dropped && wear != null) wear.gameObject.SetActive(on);
        }

        // Борода прячется, когда сорвана нижняя челюсть
        public void SetBeardVisible(bool on)
        {
            if (beard != null) beard.gameObject.SetActive(on);
        }

        // Сбросить головной убор на настил. worldImpulse — скорость отлёта (мир), floorY — высота настила.
        public bool Drop(Vector3 worldImpulse, float floor)
        {
            if (wear == null || dropped) return false;
            dropped = true; resting = false;
            floorY = floor;
            wear.SetParent(dropParent, true);
            wear.gameObject.SetActive(visible);
            dropVel = Vector3.ClampMagnitude(worldImpulse, 3f) + Vector3.up * 1.2f;
            Vector3 dir = worldImpulse.sqrMagnitude > 1e-6f ? worldImpulse.normalized : Vector3.forward;
            dropSpin = Vector3.Cross(Vector3.up, dir) * 7f + Vector3.up * 2f;
            return true;
        }

        // Вернуть всё на голову (перезапуск боя)
        public void Restore()
        {
            if (dropped && wear != null)
            {
                wear.SetParent(fit, false);
                wear.localPosition = wearLocalPos;
                wear.localRotation = wearLocalRot;
            }
            dropped = false; resting = false;
            SetVisible(true);
            SetBeardVisible(true);
        }

        public void Dispose()
        {
            if (dropped && wear != null) GameUtil.Destroy(wear.gameObject);
            foreach (var m in meshes) GameUtil.Destroy(m);
            meshes.Clear();
            if (holder != null) GameUtil.Destroy(holder.gameObject);
            holder = skull = fit = hair = beard = wear = extras = null;
        }
    }
}
