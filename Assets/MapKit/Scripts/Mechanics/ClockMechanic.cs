// Z5 钟塔：钟面/时针/分针（6×6 单位、轴心中心）叠在背景中心，指针持续转动。
// 每 10 秒钟声三选一：回溯（指针快速反转 1.2s）/ 加速 ×1.15 / 减速 ×0.85，用钟声图标闪烁提示。
// 齿轮已在背景图里画好，不额外摆。
using System.Collections;
using UnityEngine;

namespace MapKitTest
{
    public class ClockMechanic : MonoBehaviour
    {
        [Header("指针与图标（场景中引用）")]
        public Transform hourHand;
        public Transform minuteHand;
        public Sprite iconRewind;
        public Sprite iconHaste;
        public Sprite iconSlow;

        float hourAngle, minuteAngle;
        float speedFactor = 1f;    // 加速/减速期间改变指针转速
        float speedUntil;
        int bellCount;
        string lastEvent = "-";

        void Start()
        {
            // 从场景已有对象找指针（PROP_时针 / PROP_分针）
            if (hourHand == null) hourHand = transform.Find("PROP_时针");
            if (minuteHand == null) minuteHand = transform.Find("PROP_分针");
            var cfg = MapKitConfig.Load();
            if (cfg == null) return;
            StartCoroutine(BellLoop(cfg));
        }

        void Update()
        {
            // 分针正常转速：12 秒一圈（肉眼可见）；时针 1/12 速
            float dt = Time.deltaTime * speedFactor;
            minuteAngle -= 30f * dt;      // 30°/秒
            hourAngle -= 2.5f * dt;
            if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0f, 0f, minuteAngle);
            if (hourHand != null) hourHand.localRotation = Quaternion.Euler(0f, 0f, hourAngle);
        }

        IEnumerator BellLoop(MapKitConfig cfg)
        {
            while (true)
            {
                yield return new WaitForSeconds(cfg.clock.intervalSeconds);
                int pick = Random.Range(0, 3);
                bellCount++;
                if (pick == 0)
                {
                    // 回溯：指针快速反转约 1.2 秒
                    lastEvent = "回溯3秒";
                    float t = 0f;
                    while (t < 1.2f)
                    {
                        t += Time.deltaTime;
                        minuteAngle += 180f * Time.deltaTime;
                        hourAngle += 15f * Time.deltaTime;
                        yield return null;
                    }
                }
                else if (pick == 1)
                {
                    lastEvent = "加速×1.15";
                    speedFactor = cfg.clock.hasteMultiplier;
                    yield return ShowIcon(iconHaste, cfg.clock.speedDuration);
                    speedFactor = 1f;
                }
                else
                {
                    lastEvent = "减速×0.85";
                    speedFactor = cfg.clock.slowMultiplier;
                    yield return ShowIcon(iconSlow, cfg.clock.speedDuration);
                    speedFactor = 1f;
                }
                if (pick == 0) yield return ShowIcon(iconRewind, 1.2f);
                Debug.Log("[机制验证] 钟声#" + bellCount + " " + lastEvent);
            }
        }

        IEnumerator ShowIcon(Sprite icon, float dur)
        {
            if (icon == null) { yield return new WaitForSeconds(dur); yield break; }
            var go = new GameObject("FX_钟声图标");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(12.5f, 8.2f, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = icon;
            sr.sortingOrder = 20;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                sr.color = new Color(1f, 1f, 1f, Mathf.PingPong(t * 4f, 1f));
                yield return null;
            }
            Destroy(go);
        }

        public string StatusText() { return "钟声 " + bellCount + " 次，最近：" + lastEvent; }
    }
}
