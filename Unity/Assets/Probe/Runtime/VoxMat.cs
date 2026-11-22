using UnityEngine;

namespace VoxProbe
{
    /// Материалы вокселя: 1 байт на воксель — индекс в палитре, 0 — пусто.
    /// Слои головы как в веб-версии (кожа, жир, мышцы, кость, мозг, зубы, глаз), плюс мясо и кровь на срезе.
    public static class VoxMat
    {
        public const byte Empty = 0, Skin = 1, Fat = 2, Muscle = 3, Bone = 4, Brain = 5, Tooth = 6, Eye = 7, Iris = 8,
            Pupil = 9, Lip = 10, Meat = 11, Blood = 12, Cart = 13, EyeIn = 14, Count = 15;

        /// Палитра (sRGB). Шейдер берёт цвет отсюда по индексу из вершины.
        public static readonly Color32[] Palette =
        {
            new Color32(0, 0, 0, 0),        // 0 пусто
            new Color32(214, 160, 128, 255),// 1 кожа
            new Color32(236, 206, 140, 255),// 2 жир
            new Color32(150, 40, 38, 255),  // 3 мышцы
            new Color32(226, 216, 190, 255),// 4 кость
            new Color32(214, 150, 150, 255),// 5 мозг
            new Color32(244, 240, 226, 255),// 6 зуб
            new Color32(240, 240, 236, 255),// 7 белок глаза
            new Color32(70, 110, 140, 255), // 8 радужка
            new Color32(16, 16, 20, 255),   // 9 зрачок
            new Color32(186, 104, 100, 255),// 10 губа
            new Color32(120, 20, 22, 255),  // 11 мясо на срезе
            new Color32(96, 6, 10, 255),    // 12 кровь
            new Color32(220, 170, 150, 255),// 13 хрящ
            new Color32(200, 190, 180, 255),// 14 глаз внутри
        };

        public static bool Soft(byte m) => m == Skin || m == Fat || m == Muscle || m == Lip || m == Cart;
    }
}
