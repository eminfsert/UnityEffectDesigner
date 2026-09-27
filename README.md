# Unity Effect Designer

Claude Code için Unity VFX ajan stüdyosu: betimlenen ya da referans görselle
desteklenen stilize efektleri Unity 6 URP'de çalışan prefab'lara dönüştüren uzman
ajanlar ekibi (Shuriken + VFX Graph, shader, doku/vektör, mesh).

Tasarım: [`docs/DESIGN.md`](docs/DESIGN.md)

## Repo düzeni

| Yol | İçerik |
|---|---|
| `.claude-plugin/marketplace.json` | Plugin marketplace tanımı |
| `plugins/unity-vfx-designer/` | Claude Code plugin'i (skill'ler, ajanlar, komutlar) |
| `unity-package/com.effectdesigner.vfxtoolkit/` | Unity editor paketi: MCP for Unity'ye VFX custom tool'ları ekler |
| `tools/` | Geliştirme yardımcıları (ör. Unity `.meta` üreteci) |

## Gereksinimler

- Unity 6 + URP
- [MCP for Unity](https://github.com/CoplayDev/unity-mcp) (Unity projesinde kurulu ve Claude Code'a bağlı)

## Kurulum

1. Unity Package Manager → *Add package from git URL*:
   `https://github.com/eminfsert/UnityEffectDesigner.git?path=unity-package/com.effectdesigner.vfxtoolkit`
2. Claude Code:
   ```
   /plugin marketplace add eminfsert/UnityEffectDesigner
   /plugin install unity-vfx-designer@unity-effect-designer
   ```
3. MCP istemcisini yeniden bağla (yeni custom tool'ların görünmesi için).

## Durum (MVP-1)

- [x] `vfx_capture_timeline`: efekti zaman çizelgesi boyunca render edip kontakt sayfa üretir
- [x] `unity-adapter` skill'i
- [x] `vfx_apply_particle_recipe`: tek JSON tarifiyle tüm Shuriken modüllerini kurar/günceller
- [x] `particle-recipes` skill'i (tarif formatı referansı)
- [ ] `vfx_compile_report`, `vfx_project_check`
- [ ] Ajanlar ve `/vfx:create` orkestrasyonu
- [ ] VFX Graph şablon kütüphanesi, `VFXCore.hlsl`

Unity `.meta` dosyaları `tools/gen_unity_meta.py` ile üretilir; pakete yeni dosya
eklerken çalıştırın.

Unity kurmadan çalışan testler: `tests/run.sh` (.NET 8 SDK gerekir). Paketi tüm derleme
varyantlarında Unity referans DLL'lerine karşı derler ve tariflerin gerçek Unity modül
özelliklerine eşlendiğini doğrular.
