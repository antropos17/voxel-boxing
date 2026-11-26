// LoadingScreen.cs — экран загрузки боя: «Соперник выходит на ринг…», имя и прозвище соперника, режим.
using UnityEngine;
using UnityEngine.UI;

namespace VoxGame {

    public class LoadingScreen : MonoBehaviour, ILayoutHandler {

        public const ScreenId Id = ScreenId.Loading;
        const int PipCount = 3;
        const float PipRate = 3f;   // шагов бегущей отметки в секунду

        Text _caption, _name, _nick, _card, _mode;
        RectTransform _band, _pipsRect;
        Image[] _pips;
        float _t;

        public string NameText => _name.text;
        public string NickText => _nick.text;

        public void Init(UIRoot root) {
            BuildUI();
            root.Register(Id, gameObject);
        }

        void BuildUI() {
            MenuKit.Root(gameObject);
            MenuKit.Backdrop(transform, new Color(UIKit.BgDark.r, UIKit.BgDark.g, UIKit.BgDark.b, 0.92f));

            _mode = UIKit.Label(transform, "Mode", UIKit.FontSm, UIKit.TextSub, semiBold: false);
            _caption = UIKit.Label(transform, "Caption", UIKit.FontMd, UIKit.TextMain, semiBold: true);
            _caption.text = "Соперник выходит на ринг…";

            // Полоса под именем — акцент как у заголовков лестницы
            _band = UIKit.Panel(transform, "Band", UIKit.BgPanel);
            _band.GetComponent<UIBorder>().Color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, 0.7f);
            _name = UIKit.Label(_band, "Name", 60, UIKit.Accent, semiBold: true);
            var sh = _name.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(3f, -3f);
            _nick = UIKit.Label(_band, "Nick", UIKit.FontLg, UIKit.TextMain, semiBold: true);
            MenuKit.Place(_name.rectTransform, 0.03f, 0.45f, 0.97f, 0.95f);
            MenuKit.Place(_nick.rectTransform, 0.03f, 0.08f, 0.97f, 0.45f);

            _card = UIKit.Label(transform, "Card", UIKit.FontSm, UIKit.TextSub, semiBold: false);

            _pips = MenuKit.Pips(transform, "Pips", PipCount);
            _pipsRect = MenuKit.PipsRect(_pips);

            Layout(false);
        }

        // modeLine — «Лестница · ступень 3 из 12», «Быстрый бой», «Тренировка», «Испытание · бой 1 из 3».
        public void Show(in FighterDef foe, string modeLine) {
            _name.text = foe.name;
            _nick.text = "«" + foe.nickname + "»";
            _card.text = "Уровень " + foe.level;
            _mode.text = modeLine ?? "";
            _t = 0f;
            MenuKit.SetPips(_pips, 1);
        }

        public void OnLayout(bool landscape) => Layout(landscape);

        public void Layout(bool landscape) {
            if (landscape) {
                MenuKit.Place(_mode.rectTransform,    0.10f, 0.80f, 0.90f, 0.90f);
                MenuKit.Place(_caption.rectTransform, 0.10f, 0.68f, 0.90f, 0.80f);
                MenuKit.Place(_band,                  0.18f, 0.30f, 0.82f, 0.66f);
                MenuKit.Place(_card.rectTransform,    0.10f, 0.19f, 0.90f, 0.28f);
                MenuKit.Place(_pipsRect,              0.44f, 0.08f, 0.56f, 0.14f);
            } else {
                MenuKit.Place(_mode.rectTransform,    0.05f, 0.70f, 0.95f, 0.75f);
                MenuKit.Place(_caption.rectTransform, 0.05f, 0.62f, 0.95f, 0.69f);
                MenuKit.Place(_band,                  0.04f, 0.42f, 0.96f, 0.60f);
                MenuKit.Place(_card.rectTransform,    0.05f, 0.36f, 0.95f, 0.41f);
                MenuKit.Place(_pipsRect,              0.36f, 0.30f, 0.64f, 0.33f);
            }
        }

        // Бегущая отметка: экран живой, даже если сборка боя займёт кадр-другой.
        public void Tick(float dt, float udt) {
            if (!gameObject.activeInHierarchy) return;
            _t += udt;
            if (_t > 1000f) _t -= 1000f;
            int k = (int)(_t * PipRate) % PipCount;
            for (int i = 0; i < PipCount; i++) _pips[i].color = i == k ? UIKit.Accent : UIKit.BgPanelLt;
        }
    }
}
