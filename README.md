# Unity Effect Designer

Claude Code için Unity VFX ajan stüdyosu: betimlenen ya da referans görselle
desteklenen stilize efektleri Unity 6 URP'de çalışan prefab'lara dönüştüren uzman
ajanlar ekibi (Shuriken + VFX Graph, shader, doku/vektör, mesh).

Tasarım: [`docs/DESIGN.md`](docs/DESIGN.md)

## Repo düzeni

| Yol | İçerik |
|---|---|
| `.claude-plugin/marketplace.json` | Plugin marketplace tanımı |
| `plugins/unity-vfx-designer/` | Claude Code plugin'i `vfx` (skill'ler, ajanlar) |
| `unity-package/com.effectdesigner.vfxtoolkit/` | Unity editor paketi: MCP for Unity'ye VFX custom tool'ları ekler |
| `tools/` | Geliştirme yardımcıları (ör. Unity `.meta` üreteci) |

## Gereksinimler

- Unity 6 + URP
- [MCP for Unity](https://github.com/CoplayDev/unity-mcp) (Unity projesinde kurulu ve Claude Code'a bağlı)

## Kurulum

1. Unity Package Manager → *Add package from git URL*:
   `https://github.com/eminfsert/UnityEffectDesigner.git?path=unity-package/com.effectdesigner.vfxtoolkit#claude/trusting-ritchie-uze78h`
2. Claude Code:
   ```
   /plugin marketplace add eminfsert/UnityEffectDesigner@claude/trusting-ritchie-uze78h
   /plugin install vfx@unity-effect-designer
   ```
   Terminalden (yalnız bu proje için):
   ```
   claude plugin marketplace add eminfsert/UnityEffectDesigner@claude/trusting-ritchie-uze78h --scope local
   claude plugin install vfx@unity-effect-designer --scope local
   ```
   Açık bir oturumda yeniden başlatmadan yüklemek için `/reload-plugins`. Güncelleme:
   `claude plugin marketplace update unity-effect-designer` ardından `claude plugin update vfx@unity-effect-designer`.
3. MCP istemcisini yeniden bağla (yeni custom tool'ların görünmesi için).
4. Oyunun VolumeProfile'ını seç → **Assets → Effect Designer → Use As Capture Volume Profile**
   (capture'lar oyunun renkleriyle alınsın diye).
5. Doku üretimi için Python + `numpy` + `Pillow` (yoksa ajanlar Unity içinde C# ile üretir).

## Kullanım

| Komut | Ne yapar |
|---|---|
| `/vfx:create <betimleme> [referans görseller]` | Yeni efekt: brief → spec + konsept panosu (onayın) → üretim → capture'la eleştiri → en fazla 3 iyileştirme turu |
| `/vfx:iterate <efekt> <geri bildirim>` | "Daha agresif", "daha mor", "kumda okunmuyor" gibi geri bildirimi somut değişikliklere çevirip uygular |
| `/vfx:review <prefab>` | Herhangi bir efekti capture alıp puanlar, düzeltme listesi verir |

Ekip (subagent'lar): `vfx-architect` (manifest ve sözleşmeler), `texture-artist`
(maskeler, flipbook'lar, noise), `shader-artist` (özel shader'lar), `particle-artist`
(Shuriken sistemleri, materyaller, prefab), `vfx-critic` (capture + puanlama). Director ana
oturumdur: seninle konuşur, ekibi yönetir. Her efektin tasarım dosyaları
`Assets/VFX/<Id>/Design/` altında: spec, manifest, tarifler, eleştiriler, değişiklik günlüğü.

## Durum (MVP-1)

- [x] `vfx_capture_timeline`: efekti zaman çizelgesi boyunca render edip kontakt sayfa üretir
- [x] `unity-adapter` skill'i
- [x] `vfx_apply_particle_recipe`: tek JSON tarifiyle tüm Shuriken modüllerini kurar/günceller
- [x] `particle-recipes` skill'i (tarif formatı referansı)
- [x] `vfx_compile_report`: shader hatalarını satır numarasıyla + property sözleşmesi kontrolü
- [x] `VFXCore.hlsl` + `Stylized Unlit` partikül shader'ı, tarif içinde satır içi materyal
- [x] `vfx-shaders` skill'i
- [x] Capture: renk ölçümü (washedOut, doygunluk, ton), oyunun volume profile'ı ile render (proje ayarı)
- [x] Capture: sistem başına ve arka plan başına renk ölçümü, düşük kontrast uyarısı, `view_framing` ile aynı kadraj
- [x] Referans analizi `vfxref.py` (görsel/GIF/video → referans sayfası, faz renkleri, siyah/parlak oranları; critic için yan yana karşılaştırma)
- [x] `vfx_make_mesh` (kubbe, küre, halka, silindir/koni, yay) ve `Stylized Shell` shader'ı (renk rampası, toon kenar, şeritle çözülme)
- [x] Yeni doku şekilleri: mürekkep kıvrımı, kırık parça, alev dili, çizgili toon puf, şerit maskesi; capture'da yer düzlemi
- [ ] `vfx_project_check`
- [x] Ajanlar (architect, texture, shader, particle, critic) ve `/vfx:create`, `/vfx:iterate`, `/vfx:review`
- [x] Doku üretici `vfxtex.py` (glow, halka, yıldız, streak, slash, noise, smoke flipbook, SVG)
- [x] Uçtan uca `/vfx:create` testi gerçek projede (requests/003: CoinPickup 71 → 88, geçti)
- [ ] VFX Graph şablon kütüphanesi

Unity `.meta` dosyaları `tools/gen_unity_meta.py unity-package/com.effectdesigner.vfxtoolkit`
ile üretilir; pakete yeni dosya eklerken çalıştırın.

## Sürümleme

- Plugin (`plugins/unity-vfx-designer/.claude-plugin/plugin.json`) ve Unity paketi
  (`unity-package/.../package.json`) ayrı sürümlenir.
- Plugin dosyalarından (skill, ajan, script) biri değişen **her** commit plugin sürümünü
  artırır: kurulu plugin sürüm adlı bir önbellekte durur, sürüm aynı kalırsa güncelleme
  yeni içeriği almayabilir.
- Paket sürümü `Editor/ToolkitInfo.cs` ile aynı tutulur (testler kontrol eder); her araç
  sonucu `toolkitVersion` döndürür. Plugin'in istediği en düşük paket sürümü
  `unity-adapter` skill'inde yazılıdır (şu an: plugin 0.5.1 → paket ≥ 0.5.1).

Unity kurmadan çalışan testler: `tests/run.sh` (.NET 8 SDK gerekir). Paketi tüm derleme
varyantlarında Unity referans DLL'lerine karşı derler ve tariflerin gerçek Unity modül
özelliklerine eşlendiğini doğrular.
