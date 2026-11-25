// Z6 洞窟：每 8 秒掉落 3~10 颗 1×1 岩石；掉落前 1 秒地面黑色阴影从 35% 长到 100% 预警，
// 之后岩石从屏幕上方加速下落（约 0.45 秒），落地后碎成几块小石头（同一张图缩小）。
using System.Collections;
using UnityEngine;

namespace MapKitTest
{
    public class CaveMechanic : ZoneMechanicBase
    {
        [Header("素材引用")]
        public Sprite rockSprite;
        public Sprite shadowSprite;

        int waveCount;
        string lastEvent = "-";

        void Start()
        {
            base.Awake();
            if (cfg == null) return;
            StartCoroutine(RockLoop());
        }

        IEnumerator RockLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(cfg.cave.intervalSeconds);
                int n = cfg.cave.rockCount.Random();
                Debug.Log("[机制验证] 洞窟第" + (++waveCount) + "波 落石" + n + "颗");
                lastEvent = "第" + waveCount + "波 " + n + "颗";
                for (int i = 0; i < n; i++)
                {
                    float x = Random.Range(0.5f, 24.5f);
                    StartCoroutine(WarnAndDrop(x));
                }
            }
        }

        IEnumerator WarnAndDrop(float x)
        {
            // 阴影预警：1 秒内从 35% 长到 100%
            var sh = new GameObject("FX_落石阴影");
            sh.transform.SetParent(transform, false);
            sh.transform.localPosition = new Vector3(x, 0.5f, 0f);
            var ssr = sh.AddComponent<SpriteRenderer>();
            ssr.sprite = shadowSprite != null ? shadowSprite : MakeSolidSprite("阴影", new Color(0f, 0f, 0f, 0.6f), 1f, new Vector2(0.5f, 0.5f));
            ssr.color = new Color(0f, 0f, 0f, 0.1f);
            ssr.sortingOrder = 3;
            float t = 0f;
            while (t < cfg.cave.warningSeconds)
            {
                t += Time.deltaTime;
                float k = t / cfg.cave.warningSeconds;
                float s = Mathf.Lerp(0.35f, 1f, k);
                sh.transform.localScale = Vector3.one * s;
                ssr.color = new Color(0f, 0f, 0f, Mathf.Lerp(0.1f, 0.75f, k));
                yield return null;
            }
            Destroy(sh);

            // 岩石从上方加速下落 0.45 秒
            var rock = new GameObject("FX_落石");
            rock.transform.SetParent(transform, false);
            float startY = 13f;
            rock.transform.localPosition = new Vector3(x, startY, 0f);
            var rr = rock.AddComponent<SpriteRenderer>();
            rr.sprite = rockSprite != null ? rockSprite : MakeSolidSprite("石", Color.gray, 1f, new Vector2(0.5f, 0.5f));
            rr.sortingOrder = 5;
            float v = 0f, drop = 0f, dropDur = 0.45f;
            float targetY = 0.5f;
            while (drop < dropDur)
            {
                drop += Time.deltaTime;
                v += 60f * Time.deltaTime;     // 加速下落
                float ny = rock.transform.localPosition.y - v * Time.deltaTime;
                if (ny <= targetY) { ny = targetY; break; }
                rock.transform.localPosition = new Vector3(x, ny, 0f);
                yield return null;
            }
            rock.transform.localPosition = new Vector3(x, targetY, 0f);

            // 落地碎裂：几块小石头
            for (int k = 0; k < 4; k++)
            {
                var frag = new GameObject("FX_碎石");
                frag.transform.SetParent(transform, false);
                frag.transform.localPosition = new Vector3(x, targetY, 0f);
                var fr = frag.AddComponent<SpriteRenderer>();
                fr.sprite = rr.sprite;
                fr.sortingOrder = 5;
                frag.transform.localScale = Vector3.one * Random.Range(0.18f, 0.32f);
                StartCoroutine(Scatter(frag, Random.Range(0.4f, 0.7f)));
            }
            Destroy(rock);
        }

        IEnumerator Scatter(GameObject frag, float life)
        {
            float vx = Random.Range(-2.5f, 2.5f), vy = Random.Range(2f, 4f);
            float t = 0f;
            var sr = frag.GetComponent<SpriteRenderer>();
            while (t < life)
            {
                t += Time.deltaTime;
                vy -= 9f * Time.deltaTime;
                frag.transform.localPosition += new Vector3(vx, vy, 0f) * Time.deltaTime;
                sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - t / life));
                yield return null;
            }
            Destroy(frag);
        }

        public override string StatusText() { return "落石 " + waveCount + " 波，最近：" + lastEvent; }
    }
}
