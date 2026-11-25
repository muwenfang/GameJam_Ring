// Z3 竞技场：无地形机关。每 15 秒给场内双方各加 15% 攻击力，可叠加。
// 纯地图演示不含角色，用场地上方的"攻击加成"图标弹出提示层数（加成数值仍按 config 计算）。
using System.Collections;
using UnityEngine;

namespace MapKitTest
{
    public class ArenaMechanic : ZoneMechanicBase
    {
        [Header("加成提示图标（可选，场景中赋值）")]
        public Sprite buffIcon;

        int stacks;
        string lastEvent = "-";

        void Start()
        {
            base.Awake();
            if (cfg == null) return;
            StartCoroutine(BuffLoop());
        }

        IEnumerator BuffLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(cfg.arena.intervalSeconds);
                stacks++;
                float atk = 1f + stacks * cfg.arena.atkBonusPerStack;
                lastEvent = "第" + stacks + "层 攻击x" + atk.ToString("0.00");
                Debug.Log("[机制验证] 竞技场加成 " + lastEvent);
                StartCoroutine(ShowBuff());
            }
        }

        IEnumerator ShowBuff()
        {
            var go = new GameObject("FX_攻击加成提示");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(12.5f, 9f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = buffIcon != null ? buffIcon : MakeSolidSprite("加成", new Color(1f, 0.85f, 0.2f, 1f), 4f, new Vector2(0.5f, 0.5f));
            sr.sortingOrder = 20;
            go.transform.localScale = Vector3.one * 2f;
            float t = 0f;
            while (t < 1.2f)
            {
                t += Time.deltaTime;
                sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1.2f - t));
                go.transform.localPosition += Vector3.up * 0.5f * Time.deltaTime;
                yield return null;
            }
            Destroy(go);
        }

        public override string StatusText()
        {
            return "每" + cfg.arena.intervalSeconds + "秒 +" + (cfg.arena.atkBonusPerStack * 100f).ToString("0") + "% 攻击，当前 " + lastEvent;
        }
    }
}
