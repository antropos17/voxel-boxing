using System.Collections.Generic;
using UnityEngine;
using VoxProbe;

namespace VoxGame
{
    public enum GloveShape { Normal, Heavy, Narrow, Spiked }

    public struct GloveLook
    {
        public Color      colorA, colorB;
        public GloveShape shape;

        // Смаппировать запись каталога (shapeTag 0-3 → GloveShape 0-3)
        public static GloveLook FromEntry(GloveEntry e) => new GloveLook
        {
            colorA = e.colorA,
            colorB = e.colorB,
            shape  = (GloveShape)Mathf.Clamp(e.shapeTag, 0, (int)GloveShape.Spiked),
        };
    }

    // Две перчатки от первого лица в пространстве камеры.
    // НЕ MonoBehaviour — создаётся и управляется из FightDirector.
    public class PlayerGloves
    {
        // ---- Константы --------------------------------------------------
        const float VS_G  = 0.0052f;  // 5.2 мм на воксель
        const int   GX    = 30;
        const int   GY    = 24;
        const int   GZ    = 52;

        // Позиции покоя в локальных координатах камеры
        static readonly Vector3[] RestPos =
        {
            new Vector3(-0.3f, -0.2f, 0.5f),   // 0=Left
            new Vector3( 0.3f, -0.2f, 0.5f),   // 1=Right
        };

        // Индексы палитры перчатки
        const byte G_MAIN = 1;
        const byte G_SEC  = 2;

        // ---- Данные хода ------------------------------------------------
        readonly Transform   _cam;
        Material             _mat;
        readonly Transform[] _gloveRoot = new Transform[2];
        readonly VoxBody[]   _bodies    = new VoxBody[2];

        // Пружинное состояние
        readonly Vector3[] _pos = new Vector3[2];
        readonly Vector3[] _vel = new Vector3[2];
        readonly Vector3[] _tgt = new Vector3[2];

        // Таймеры
        readonly float[] _punchTimer  = new float[2]; // > 0 → перчатка ещё в броске
        bool   _blocking;
        int    _dodgeSide;    // -1/0/+1
        float  _dodgeTimer;

        // ---- Конструктор ------------------------------------------------

        public PlayerGloves(Transform cameraParent, GloveLook look, Material voxMat)
        {
            _cam = cameraParent;
            _mat = new Material(voxMat);
            SetPalette(look);

            var builds = new List<(VoxBody, VoxModel)>(2);
            for (int s = 0; s < 2; s++)
            {
                var root = new GameObject($"glove{s}").transform;
                root.SetParent(_cam, false);
                _gloveRoot[s] = root;

                var mesh = new GameObject($"gloveMesh{s}").transform;
                mesh.SetParent(root, false);
                // Центрировать по X/Y, Z начинается от root (к сопернику)
                mesh.localPosition = new Vector3(-GX * 0.5f * VS_G, -GY * 0.5f * VS_G, -GZ * 0.5f * VS_G);

                _bodies[s] = new VoxBody(mesh, _mat, VS_G);
                _pos[s] = RestPos[s];
                _tgt[s] = RestPos[s];

                var m = new VoxModel(GX, GY, GZ);
                PaintGlove(m, look.shape, s);
                builds.Add((_bodies[s], m));
            }
            VoxModel.BuildAll(builds);
        }

        // ---- Команды анимации -------------------------------------------

        // Запустить бросок перчаткой. side: -1=лево, +1=право. duration — время до возврата (с).
        public void Punch(int side, PunchType kind, float duration)
        {
            int s = side < 0 ? 0 : 1;
            _tgt[s] = PunchTarget(s, kind);
            _punchTimer[s] = Mathf.Max(duration, 0.08f);
        }

        // Включить/выключить стойку блока.
        public void SetBlock(bool blocking)
        {
            _blocking = blocking;
        }

        // Уклон: side < 0 = влево, side > 0 = вправо, 0 = сброс.
        public void SetDodge(int side)
        {
            _dodgeSide  = side;
            _dodgeTimer = side != 0 ? 0.25f : 0f;
        }

        // ---- Tick -------------------------------------------------------

        public void Tick(float dt)
        {
            if (_dodgeTimer > 0)
            {
                _dodgeTimer -= dt;
                if (_dodgeTimer <= 0) _dodgeSide = 0;
            }

            const float K = 60f, C = 12f;
            for (int s = 0; s < 2; s++)
            {
                // Уменьшаем таймер броска
                if (_punchTimer[s] > 0)
                {
                    _punchTimer[s] -= dt;
                    if (_punchTimer[s] <= 0)
                        _tgt[s] = BaseTarget(s);
                }
                else
                {
                    _tgt[s] = BaseTarget(s);
                }

                // Пружинить к цели
                _vel[s] += (_tgt[s] - _pos[s]) * (K * dt);
                _vel[s] *= 1f - C * dt;
                _pos[s] += _vel[s] * dt;

                if (_gloveRoot[s] != null)
                    _gloveRoot[s].localPosition = _pos[s];
            }
        }

        // ---- Целевые позиции --------------------------------------------

        Vector3 BaseTarget(int s)
        {
            if (_blocking)   return BlockPos(s);
            if (_dodgeSide != 0) return DodgePos(s);
            return RestPos[s];
        }

        static Vector3 BlockPos(int s) =>
            new Vector3(s == 0 ? -0.15f : 0.15f, -0.08f, 0.42f);

        Vector3 DodgePos(int s)
        {
            float dx = _dodgeSide * 0.08f;
            return RestPos[s] + new Vector3(dx, 0f, 0f);
        }

        static Vector3 PunchTarget(int s, PunchType kind)
        {
            float sign = s == 0 ? 1f : -1f;
            switch (kind)
            {
                case PunchType.Hook:
                    return new Vector3(sign * 0.10f, -0.17f, 0.65f);
                case PunchType.Upper:
                    return new Vector3(RestPos[s].x, -0.08f, 0.63f);
                case PunchType.Mega:
                    return new Vector3(sign * 0.05f, -0.12f, 0.78f);
                default: // Jab
                    return new Vector3(RestPos[s].x, -0.19f, 0.70f);
            }
        }

        // ---- Построение вокселей ----------------------------------------

        static void PaintGlove(VoxModel m, GloveShape shape, int side)
        {
            float cx = GX * 0.5f, cy = GY * 0.5f, cz = GZ * 0.5f;

            switch (shape)
            {
                case GloveShape.Heavy:
                    m.FillRoundBox(cx, cy, cz + 4f, 13f, 11f, 22f, 5f, G_MAIN);
                    m.FillRoundBox(cx, cy, 8f,      11f,  9f,  7f, 3f, G_SEC);
                    break;
                case GloveShape.Narrow:
                    m.FillRoundBox(cx, cy, cz + 4f,  9f,  7f, 20f, 3f, G_MAIN);
                    m.FillRoundBox(cx, cy, 8f,        7f,  6f,  7f, 2f, G_SEC);
                    break;
                case GloveShape.Spiked:
                    m.FillRoundBox(cx, cy, cz + 4f, 11f,  9f, 20f, 4f, G_MAIN);
                    m.FillRoundBox(cx, cy, 8f,        9f, 7.5f,7f, 3f, G_SEC);
                    // 4 костяшки поверх кулака
                    for (int k = 0; k < 4; k++)
                        m.FillEllipsoid(6f + k * 5f, cy + 8f, GZ - 4f, 2.5f, 2.5f, 3f, G_SEC);
                    break;
                default: // Normal
                    m.FillRoundBox(cx, cy, cz + 4f, 11f,  9f, 20f, 4f, G_MAIN);
                    m.FillRoundBox(cx, cy, 8f,        9f, 7.5f,7f, 3f, G_SEC);
                    break;
            }

            // Небольшое зеркальное смещение большого пальца для левой/правой руки
            float tx = side == 0 ? cx - 11f : cx + 11f;
            m.FillEllipsoid(tx, cy - 3f, cz - 8f, 4.5f, 3.5f, 5f, G_MAIN);
        }

        void SetPalette(GloveLook look)
        {
            var p = new Color[16];
            p[0]      = Color.clear;
            p[G_MAIN] = look.colorA;
            p[G_SEC]  = look.colorB;
            VoxModel.SetPalette(_mat, p);
        }

        // ---- Dispose ----------------------------------------------------

        public void Dispose()
        {
            foreach (var b in _bodies) b?.Dispose();
            DestroyObj(_mat);
            for (int i = 0; i < 2; i++)
                if (_gloveRoot[i] != null) DestroyObj(_gloveRoot[i].gameObject);
        }

        static void DestroyObj(UnityEngine.Object o)
        {
            if (o == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEngine.Object.DestroyImmediate(o); return; }
#endif
            UnityEngine.Object.Destroy(o);
        }
    }
}
