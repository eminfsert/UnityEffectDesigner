# Unity Effect Designer — Claude Code için VFX Ajan Stüdyosu

> Durum: **Tasarım taslağı (v0.5)** — değerlendirme ve karar için.

### Alınan kararlar

| Konu | Karar |
|---|---|
| Motor / pipeline | **Unity 6 + URP** (HDRP ve Built-in kapsam dışı) |
| Partikül sistemi | **Shuriken + VFX Graph birlikte** — katman bazında seçim (bkz. §6.1) |
| Stil | **Stilize** (el boyaması / anime / MOBA-ARPG çizgisi) — gerçekçi efekt kapsam dışı (bkz. §6.3) |
| Platform | **PC / konsol** — mobil hedeflenmiyor; VFX Graph (compute) serbestçe kullanılır |
| Unity bağlantısı | **CoplayDev/unity-mcp (MCP for Unity)** — kullanıcıda kurulu. Kendi MCP sunucumuzu yazmayız; eksikleri bu MCP'nin **custom tool** mekanizmasıyla ekleyen küçük bir C# paketi yazarız (bkz. §4) |

Kullanıcının betimlediği ya da referans görselle desteklediği bir efekti
("mor-altın renkli, önce içe çöken sonra patlayan bir büyü halkası") Unity
projesinde **çalışan, oynatılabilir, ayarlanabilir bir prefab** hâline getiren,
uzmanlaşmış ajanlardan oluşan bir "VFX stüdyosu".

---

## 1. Temel karar: Skill mi, Plugin mi?

**Cevap: Plugin.** Skill'ler ve ajanlar plugin'in *içinde* yaşar.

| Bileşen | Neden gerekli | Tek başına skill yeter mi? |
|---|---|---|
| **Subagent'lar** (`agents/*.md`) | Her uzmanın ayrı sistem promptu, ayrı araç yetkisi, ayrı bağlamı olmalı | Hayır |
| **Skill'ler** (`skills/*/SKILL.md`) | Shuriken modülleri, HLSL tarifleri, VFX teorisi gibi büyük bilgi paketleri *ihtiyaç anında* yüklenir; ajan bağlamları şişmez | Evet ama tek başına orkestrasyon yapamaz |
| **Komutlar** (`/vfx:create`, `/vfx:iterate` …) | Kullanıcının giriş noktası, orkestrasyon akışını tanımlar | Hayır |
| **Unity bağlantısı** (mevcut Unity MCP + VFX Toolkit C# paketi) | Ajanların Unity'yi *görmesi ve kontrol etmesi*: derleme, prefab kurma, **render alma** | Hayır |
| **Hook'lar** | Shader yazılınca otomatik derle, SVG yazılınca otomatik rasterize et | Hayır |

Plugin tüm bunları tek paket olarak dağıtır; `/plugin install` ile kurulur.

> **Önemli mimari kısıt:** Claude Code'da subagent'lar başka subagent
> çağıramaz. Bu yüzden **orkestratör (Director) bir subagent değil**; ana
> oturumu yöneten `/vfx:create` komutu + `vfx-director` skill'idir. Uzmanları o
> çağırır, paralel çalıştırır, sonuçları birleştirir.

---

## 2. Ekip (Ajan Kadrosu)

### 2.1 Çekirdek ekip (MVP)

| # | Ajan | Rol | Ürettiği çıktı |
|---|---|---|---|
| 0 | **VFX Director** *(ana oturum)* | Brief'i yorumlar, referans görseli katmanlara ayırır, efekt spesifikasyonunu yazar, ekibi yönetir, son sözü söyler | `effect.spec.yaml` |
| 1 | **Systems Architect** | Paketin sistem tasarımı: klasör yapısı, isimlendirme, render pipeline tespiti, prefab hiyerarşisi, ajanlar arası **sözleşmeler** (shader property adları, vertex stream düzeni, texture kanal paketleme), runtime controller | `manifest.yaml`, `EffectController.cs`, asmdef |
| 2 | **Texture & Vector Artist** | SVG ile vektör çizim (halka, kıvılcım, slash, rune, yıldız), prosedürel doku (Perlin/Voronoi/FBM noise), gradient ramp/LUT, flipbook atlası, SDF | `.svg` → `.png`, noise, flipbook |
| 3 | **Shader Artist** | URP için HLSL/ShaderLab + VFX Graph'a Shader Graph sarmalayıcıları: dissolve, erosion, fresnel, UV scroll, distortion, soft particle, flipbook blend, vertex offset, additive/alpha-blend/premultiplied varyantları | `.shader`, `.hlsl`, material recipe |
| 4 | **Particle Artist** | Shuriken **ve** VFX Graph uzmanı; her katman için doğru backend'i seçer. Emisyon, şekil, ömür eğrileri, noise, trail/strip, sub-emitter/GPU event, custom vertex stream | `particle recipe (JSON)` / VFX Graph recipe |
| 5 | **VFX Critic (Görsel QA)** | Unity'den alınan render karelerini spec ve referansla karşılaştırır, puanlar, somut düzeltme listesi çıkarır | `review.md` + değişiklik listesi |

### 2.2 Genişletilmiş ekip (önerilen ek yetenekler)

| Ajan | Neden değerli |
|---|---|
| **Mesh Artist** | Stilize VFX'in (MOBA/ARPG tarzı) büyük kısmı mesh'tir: swirl, slash arc, koni, halka, şok dalgası, trail mesh. Python/C# ile prosedürel mesh (+UV düzeni) üretir. Sadece partikülle yapılan efektler "ucuz" görünür. |
| **Motion & Timing Designer** | Efektin *hissi* zamanlamadan gelir: anticipation → impact → dissipation. AnimationCurve'ler, easing, beat haritası; ayrıca **efekt dışı etkiler**: kamera sarsıntısı, ışık flaşı, bloom/chromatic pulse, hit-stop, ekran distorsiyonu. |
| **Performance Engineer** | Overdraw, partikül sayısı, shader instruction, batch sayısı, PC/konsol bütçesi; LOD varyantları üretir. "Güzel ama oyunu kasan" efekti engeller. |
| **Lighting & Post Artist** | Point light flicker, emissive/HDR renk kalibrasyonu, bloom eşiği ile uyum, Volume override'ları. |
| **Style Librarian (Hafıza)** | Projenin "stil İncili": palet, çizgi dili, daha önce onaylanmış efektler, kullanıcının tercihleri ("daha az duman sever"). Mevcut texture/shader'ları yeniden kullanır, tutarlılık sağlar. |
| **Audio Cue Designer** *(opsiyonel)* | Ses üretmez; efektin beat haritasına göre SFX tetik noktaları ve ses brief'i yazar. |

> Maliyet notu: Her ajan bir bağlam = token. Öneri, *çekirdek 5 + Mesh +
> Motion* ile başlamak; Performance ve Librarian'ı MVP-2'de eklemek.

---

## 3. Uçtan Uca Akış

```mermaid
flowchart TD
    U[Kullanıcı: metin + referans görsel] --> D0[Director: Intake<br/>netleştirici sorular]
    D0 --> D1[Director: Katman ayrıştırma<br/>effect.spec.yaml]
    D1 --> CB[Konsept Panosu<br/>palet, katmanlar, zaman çizelgesi]
    CB -->|kullanıcı onayı| A[Architect: manifest + sözleşmeler]
    A --> P1

    subgraph P1[Paralel Üretim]
      T[Texture & Vector Artist]
      S[Shader Artist]
      M[Mesh Artist]
      MO[Motion Designer]
    end

    P1 --> PA[Particle Artist<br/>materyal/mesh/texture referanslarıyla]
    PA --> AS[Architect: Montaj<br/>VFX Toolkit ile prefab kurulumu]
    AS --> R[Unity: render kareleri + kontakt sayfa + GIF]
    R --> C[Critic: puanlama + düzeltme listesi]
    C -->|puan < eşik ve tur < N| ROUTE[Director: düzeltmeleri<br/>ilgili uzmana yönlendir]
    ROUTE --> P1
    C -->|puan ≥ eşik| UR[Kullanıcıya sunum]
    UR -->|"daha mavi, daha agresif"| ROUTE
```

### Fazlar

1. **Intake** — Director eksik bilgiyi sorar (en fazla 3–4 soru): efekt türü
   (tek seferlik / loop / projectile), 2D mi 3D mi, hedef platform, stil,
   oyun içi ölçek ve süre. Proje bilgisi (Unity sürümü, URP/HDRP, yüklü
   paketler) VFX Toolkit'ten otomatik okunur — kullanıcıya sorulmaz.
2. **Ayrıştırma** — Referans görsel/brief katmanlara bölünür. Örn. *Ateş topu
   çarpması*: çekirdek flaş · şok dalgası halkası · kıvılcımlar · duman ·
   kor parçaları · zemin izi (decal) · ışık · kamera sarsıntısı.
3. **Konsept Panosu** — Üretime başlamadan önce kullanıcıya tek bir görsel
   sayfa: renk paleti, katman listesi, zaman çizelgesi (hangi katman ne zaman
   doğar/söner), şekil dili. *Pahalı üretimden önce yönü onaylatmak*,
   deneyimin "profesyonel stüdyo" hissini veren kısımdır.
4. **Sözleşmeler** — Architect, uzmanlar paralel çalışabilsin diye arayüzleri
   önceden sabitler (bkz. §5).
5. **Paralel üretim** — Texture, Shader, Mesh, Motion aynı anda; Particle
   Artist onların sözleşmedeki isimlerine referans vererek çalışır (gerekirse
   yer tutucu ile başlar).
6. **Montaj** — VFX Toolkit üzerinden: asset import, shader derleme, materyal
   oluşturma, recipe → ParticleSystem kurulumu, prefab kaydı.
7. **Render & Eleştiri** — Sabit kamera açılarından belirli zamanlarda kareler
   (ör. t = 0.0, 0.1, 0.25, 0.5, 1.0, 2.0 s), koyu ve açık arka plan,
   kontakt sayfa + GIF. Critic rubrikle puanlar (bkz. §7).
8. **İterasyon** — Critic'in listesi Director tarafından ilgili uzmana
   yönlendirilir; varsayılan en fazla 3 otomatik tur, sonra kullanıcıya sunum.
9. **Doğal dil ayarı** — "daha agresif", "sonu daha yumuşak sönsün", "biraz
   daha mor" gibi geri bildirim → parametre delta'larına çevrilir.

---

## 4. Unity Bağlantısı — Ekibin Gözleri ve Elleri

Ajanların Unity'yi göremediği bir sistem kör üretim yapar. Kaliteyi
belirleyen tek en önemli bileşen **render alıp geri besleme döngüsü**dür.

### Karar: CoplayDev/unity-mcp + VFX Toolkit custom tool'ları

Kullanıcıda kurulu olan [MCP for Unity](https://github.com/CoplayDev/unity-mcp)
(CoplayDev) kullanılır. İncelemede (Eylül 2026, `main`) işimize yarayan hazır
araçlar:

| İhtiyaç | MCP for Unity'de hazır | Yeterli mi? |
|---|---|---|
| Sahne, GameObject, prefab, asset, paket | `manage_scene`, `manage_gameobject`, `manage_prefabs`, `manage_asset`, `manage_packages` | ✅ |
| Shuriken | `manage_vfx` → `particle_*`: main, emission, shape, color/size/velocity over lifetime, noise, renderer, burst; collision/trails/lights modüllerini sadece **açıp kapatma** | ⚠️ Kısmi |
| VFX Graph | `manage_vfx` → `vfx_*`: şablondan asset oluşturma, exposed property atama (float…gradient, texture, mesh, curve), event, seed, oynatma | ✅ Tam da §6.1'deki "şablon + exposed property" stratejisi |
| Line / Trail Renderer | `line_*` (daire, yay, bezier), `trail_*` | ✅ |
| Shader / materyal | `manage_shader` (create/read/update/delete), `manage_material` | ⚠️ Derleme hatası raporu yok (konsoldan okunabilir) |
| Doku | `manage_texture`: pattern, gradient, noise, import ayarları | ⚠️ Temel; stilize dokular bizim araçlarımızla |
| URP ayarları | `manage_graphics`: pipeline, renderer feature, Volume, rendering stats | ✅ |
| Görme | `manage_camera` → `screenshot`: inline PNG, `view_target`, 6 açılı kontakt sayfa, orbit | ⚠️ Açı var, **zaman yok** |
| Profil | `manage_profiler`, rendering stats | ✅ Temel |
| Kaçış kapısı | `execute_code` (editor'de C# çalıştır), `batch_execute` | ✅ |
| **Genişletme** | **`[McpForUnityTool]` attribute'lu editor sınıfları otomatik olarak MCP aracı olur** | ✅ Kilit özellik |

Not: `manage_vfx` gibi araçlar `vfx` grubundadır ve varsayılan olarak gizli
olabilir; `unity-adapter` skill'i oturum başında `manage_tools` ile açar.

**Eksik kalan ve bizim yazacağımız custom tool'lar** (hepsi
`com.effectdesigner.vfxtoolkit` paketinde, `[McpForUnityTool]` ile otomatik
kaydolur — ayrı sunucu, ayrı port, ayrı kurulum yok):

| Custom tool | Neden gerekli |
|---|---|
| `vfx_capture_timeline` | **En kritik araç.** Efekti edit mode'da `ParticleSystem.Simulate(t)` / `VisualEffect.Simulate` ile belirli zamanlara sarar, her zaman × açı için kare alır; kontakt sayfa + GIF döner. Mevcut `screenshot` sadece o anki kareyi çeker — hareketi değerlendiremeyiz. |
| `vfx_apply_particle_recipe` | Tek çağrıda tüm hiyerarşi ve **tüm modüller**: texture sheet animation (flipbook), sub-emitter, rotation, limit velocity, force, custom data, **custom vertex stream**, trail ayarları. Mevcut araçta bunlar yok ve katman başına ~15 ayrı çağrı gerekir. |
| `vfx_compile_report` | `ShaderUtil.GetShaderMessages` ile shader hatalarını satır numarasıyla döner; Shader Graph/VFX Graph derleme durumunu kontrol eder. |
| `vfx_project_check` | URP'de Opaque/Depth Texture, HDR, bloom eşiği, Decal feature, VFX Graph/Shader Graph paket sürümleri — tek raporda. |
| `vfx_profile_effect` | Efekt oynarken maks. partikül sayısı, overdraw görünümü karesi, draw call. |

Geri kalan her şey MCP for Unity'nin hazır araçlarıyla yapılır. İlk
yaklaşımda eksik bir şey çıkarsa önce `execute_code` ile prototiplenir,
sık kullanılıyorsa custom tool'a terfi ettirilir.

### Kritik tasarım kararı: **Deklaratif recipe → güvenilir builder**

Ajanlar `.prefab`/`.mat`/`.vfx` YAML'ını **elle yazmaz** (kırılgan, GUID
cehennemi). Bunun yerine:

- Particle Artist **JSON recipe** yazar (şema ile doğrulanır),
- VFX Toolkit'teki tek ve iyi test edilmiş C# builder onu ParticleSystem'e çevirir.

Faydaları: şema doğrulaması, küçük diff'ler, iterasyonda sadece parametre
yaması, versiyon kontrolü dostu, LLM'in en iyi olduğu şey olan *yapılandırılmış
veri* üretimi.

Shader'lar ise **kod** olarak yazılır (HLSL, LLM için doğal). VFX Graph
çıktıları için gereken Shader Graph'lar ise ince şablonlardır: tüm mantık aynı
HLSL dosyasında durur, Shader Graph sadece `Custom Function` düğümüyle onu
çağırır (bkz. §6.2).

### Çalışma ortamı notu

Unity MCP'si, Unity Editor'ün **açık olduğu makinede** çalışan Claude Code gerektirir
(yerel CLI / masaüstü uygulaması). Unity `-batchmode -nographics` render
alamaz; headless/CI modu için GPU'lu batchmode + `-executeMethod` yedek yol
olarak planlanır.

---

## 5. Veri Sözleşmeleri

### 5.1 `effect.spec.yaml` (Director → herkes)

```yaml
id: arcane_nova_impact
archetype: impact            # projectile | impact | aura | beam | portal | buff | pickup | ...
style: stylized_hand_painted
platform: pc                 # pc | console
pipeline: urp                # Unity 6 URP (VFX Toolkit ile doğrulanır)
duration: 1.6                # saniye
loop: false
scale_meters: 3.0
palette:
  core:   "#FFF4D6"
  primary: "#9B5CFF"
  accent:  "#FFC247"
  shadow:  "#2A0F4F"
beats:                       # hissin iskeleti
  - { t: 0.00, name: anticipation, note: "içe çeken parçacıklar, ışık kısılır" }
  - { t: 0.35, name: impact,       note: "beyaz flaş + halka + kamera sarsıntısı" }
  - { t: 0.60, name: dissipation,  note: "kıvılcımlar yavaşlar, duman kıvrılır" }
layers:
  - id: core_flash
    role: impact
    window: [0.35, 0.50]
    technique: particle_billboard
    backend: shuriken
    needs: { texture: soft_glow_star, shader: vfx_additive_flipbook }
  - id: shockwave
    role: impact
    window: [0.35, 0.80]
    technique: mesh_ring
    needs: { mesh: ring_flat, texture: ring_noise_mask, shader: vfx_erosion_distort }
  - id: sparks
    role: secondary
    window: [0.36, 1.20]
    technique: particle_stretched
    backend: shuriken
    count_hint: 40
  - id: arcane_dust
    role: ambient
    window: [0.00, 1.60]
    technique: gpu_particles_sdf_attract   # önce küreye çekilir, impact'te savrulur
    backend: vfxgraph
    template: gpu_dust_attractor
    count_hint: 20000
  # ...
extras:
  camera_shake: { t: 0.35, amplitude: 0.25, duration: 0.3 }
  light: { color: "#B889FF", peak_intensity: 8, curve: impact_spike }
references:
  - path: refs/user_ref_01.png
    notes: "halkanın kırık-parçalı kenarı buradan"
```

### 5.2 `manifest.yaml` (Architect → uzmanlar)

Paralel üretimin anahtarı. Örnekler:

- **İsimlendirme:** `VFX_<EffectId>_<Layer>`, `T_<Id>_<Name>`, `M_…`, `SH_…`
- **Klasör:** `Assets/VFX/<EffectId>/{Textures,Shaders,Materials,Meshes,Prefabs}`
- **Texture kanal paketleme:** `R = şekil maskesi, G = noise, B = erosion ramp, A = alfa`
- **Shader property sözleşmesi:** `_MainTex, _TintColor (HDR), _Erosion, _ScrollSpeed, _DistortStrength …`
- **Custom vertex stream sözleşmesi** (Particle ↔ Shader):
  `TEXCOORD0.xy = UV, TEXCOORD0.zw = Custom1.xy (erosion, emissive boost), TEXCOORD1.x = AgeFraction`
- **Runtime API:** `EffectController.Play(intensity, tint)`, `Stop(fade)`, pool uyumluluğu, çarpma normali/ölçek parametreleri

### 5.3 Particle recipe (kısaltılmış örnek)

```json
{
  "name": "VFX_ArcaneNova_Sparks",
  "main": { "duration": 1.0, "loop": false, "startLifetime": [0.4, 0.8],
            "startSpeed": [6, 12], "startSize": [0.04, 0.09],
            "startColor": { "gradient": "palette.accent->palette.core" },
            "simulationSpace": "World", "gravityModifier": 0.6 },
  "emission": { "bursts": [ { "time": 0.36, "count": [30, 45] } ] },
  "shape": { "type": "Sphere", "radius": 0.2 },
  "velocityOverLifetime": { "speedModifier": { "curve": "ease_out_expo" } },
  "sizeOverLifetime": { "curve": [[0,1],[0.7,0.8],[1,0]] },
  "renderer": { "mode": "Stretch", "velocityScale": 0.08,
                "material": "M_ArcaneNova_Spark",
                "customVertexStreams": ["Position","Color","UV","Custom1.xy"] }
}
```

---

## 6. Uzman Detayları

### Texture & Vector Artist — efektin içindeki 2D çizimler

Partikül efektlerinin büyük kısmı, partiküllerin taşıdığı **2D sprite/doku**
kadar iyidir. Bu ajan efektin ihtiyaç duyduğu her 2D çizimi *o efekt için*,
spec'teki palet ve şekil diline göre üretir. Hazır doku kütüphanesine mecbur
kalınmaz.

**Üretim yolları (ajan ihtiyaca göre seçer / birleştirir):**

| Yol | Nasıl | Güçlü olduğu yer |
|---|---|---|
| **1. Vektör (SVG)** — varsayılan | Ajan SVG kodu yazar → `resvg` ile PNG | Keskin, stilize, geometrik şekiller: yıldız/sparkle, halka, slash yayı, hilal, streak, damla, yaprak, tüy, kalkan altıgenleri, **büyü çemberi ve rünler**, heal "+", kalp, coin parıltısı |
| **2. Organik vektör** | SVG + filtreler: `feTurbulence` + `feDisplacementMap` (tırtıklı/fırça kenar), `feGaussianBlur` (glow), `feComponentTransfer` (2–3 tonlu posterize) | Stilize duman topu, alev dili, patlama bulutu, enerji çatlağı — "elle boyanmış" his |
| **3. Prosedürel (Python/numpy)** | Kod ile piksel üretimi | Tileable noise (Perlin/Voronoi/FBM), erosion maskeleri, gradient ramp/LUT, **prosedürel şimşek** (dallanan L-system), SDF |
| **4. AI görsel üretimi** — opsiyonel | MCP for Unity'nin `generate_image` aracı (fal.ai / OpenRouter, kullanıcının kendi API anahtarı) | Çok ressamsı, karmaşık, detaylı sprite'lar. Şeffaf arka plan üretemediği için siyah zeminde istenir, alfa parlaklıktan türetilir, sonra posterize + paletle yeniden renklendirilir |

**Flipbook (animasyonlu sprite):** Aynı SVG/noise parametresi kare kare
değiştirilerek (ör. duman topunun erosion eşiği 0→1, alevin turbulence
seed'i) 8–16 kare üretilir → atlas'a (4×4) paketlenir → Shuriken'de
Texture Sheet Animation, VFX Graph'ta flipbook olarak bağlanır. Stilize
"çizilmiş animasyon" hissi buradan gelir.

**Renk stratejisi:** Sprite'lar çoğunlukla **gri tonlu maske** olarak üretilir
(R = şekil, G = iç detay, B = erosion ramp). Renk shader'da palet/gradient
map ile verilir. Böylece aynı sprite ateş, buz ya da zehir varyantında yeniden
kullanılır (`/vfx:variant`).

**Kendi işini görerek kontrol:** Ajan ürettiği PNG'yi görsel olarak açıp
inceler (Claude görsel okuyabilir). Ayrıca otomatik kontroller çalışır:
kenara taşma (partikülde kesik görünür), alfa halo'su, tile dikişi, değer
dağılımı (çok gri/düz mü?), 64 px'e küçültüldüğünde okunurluk.

**Sınırı (dürüst değerlendirme):** Geometrik ve stilize şekillerde SVG ile
çok iyi sonuç beklenir. Karakter/yaratık illüstrasyonu gibi figüratif
çizimlerde (ör. efektin içinde bir ejderha kafası silüeti) SVG vasat kalır.
O durumda 4. yol ya da kullanıcının vereceği bir görsel kullanılır.

### Shader Artist
- Ortak `VFXCore.hlsl` kütüphanesi: soft particle, flipbook blend, polar UV,
  UV distort, erosion step/smoothstep, fresnel, HDR tint, dither fade.
- İki çıktı: Shuriken için URP `.shader`, VFX Graph için aynı HLSL'i
  `Custom Function` ile çağıran Shader Graph şablonları. Blend modları:
  Additive, Alpha, Premultiplied, Multiply.
- Her shader yazımından sonra hook ile derleme; hata varsa otomatik düzeltme.
- Keyword/varyant sayısını sınırlı tut (derleme süresi ve bellek).

### Particle Artist
- Shuriken'in tüm modüllerine hâkim skill paketi (modül referansı + "tarifler":
  kıvılcım, duman, kor, toz, sihirli toz, yağmur, kan, enerji akışı).
- Sub-emitter (ölüm/çarpışma), trail, noise, limit velocity, collision.
- Custom vertex stream ile shader'a veri taşıma (sözleşmeye uygun).
- VFX Graph: şablon kütüphanesi + exposed property ile kurulum, GPU event,
  strip, SDF/depth-buffer çarpışma, 6-way lit duman (bkz. §6.1).

### 6.1 İki partikül backend'i: Shuriken + VFX Graph

Tek bir efekt **karışık** olabilir: aynı prefab içinde bir katman Shuriken,
diğeri VFX Graph. Seçim katman bazında, Particle Artist tarafından ve spec'te
görünür biçimde yapılır (`layers[].backend`).

**Seçim kuralları (varsayılan):**

| Durum | Backend | Neden |
|---|---|---|
| Katmanda < ~1.000 partikül, CPU'dan kontrol (fizik callback, script ile tek tek partikül) | Shuriken | Basit, her yerde çalışır, `OnParticleCollision` |
| Binlerce–milyonlarca partikül (toz bulutu, sürü, kıvılcım yağmuru) | VFX Graph | GPU simülasyonu |
| Uzun kesintisiz şeritler, GPU event zincirleri, SDF'e yapışan/akan partiküller, depth buffer çarpışması | VFX Graph | Shuriken'de yok ya da pahalı |
| Işık almış hacimsel duman (6-way lighting) | VFX Graph | URP'de 6-way lit output |
| Mesh partikül + özel HLSL shader, basit burst | Shuriken | Doğrudan `.shader` kullanır |

**VFX Graph üretim stratejisi** — `.vfx` dosyaları çok nesneli YAML'dır ve
grafik düzenleme API'si büyük oranda `internal`'dır; elle yazmak kırılgandır.
Bu yüzden üç kademe:

1. **Şablon + exposed property (MVP-1)** — Eklentiyle gelen, insan eliyle
   yapılmış kaliteli bir `.vfx` şablon kütüphanesi (burst sparks, GPU dust
   cloud, strip trail, 6-way smoke, mesh shockwave, orbiting motes, SDF
   attractor…). Her şablonun exposed property imzası JSON olarak belgelenir.
   Ajan şablonu seçer, kopyalar, property'leri atar. Kalitenin tabanı yüksek,
   kırılganlık sıfır.
2. **Modüler kompozisyon + Custom HLSL (MVP-2)** — Bir efekt birden çok
   `VisualEffect` bileşeninin (katman başına bir şablon) birleşimi.
   Davranış farkları Unity 6'nın **Custom HLSL** blok/operatörleri ve
   subgraph'larla eklenir; ajan HLSL yazar, şablon sabit kalır.
3. **Programatik grafik kurucu (MVP-3, deneysel)** — VFX Toolkit'te, VFX Graph'ın
   editor modelini (reflection ile) kullanıp context/blok ekleyen bir builder.
   Unity sürüm yükseltmelerinde kırılabileceği için sürüm-kilitli ve testli.

**Ortak dil:** İki backend için de recipe'ler aynı kavramları kullanır
(ömür, hız, boyut/renk eğrileri, burst zamanları, palet referansları). Motion
Designer'ın beat haritası ve eğrileri iki backend'e de aynı şekilde çevrilir;
Critic hangi backend olduğunu bilmek zorunda değildir.

### 6.2 URP'ye özel teknik kurallar (Unity 6)

- **Render Graph:** Unity 6 URP varsayılan olarak Render Graph kullanır;
  özel Renderer Feature gerekirse (ör. ekran distorsiyonu, özel blur)
  Render Graph API'siyle yazılır.
- **Distorsiyon/refraksiyon:** URP asset'inde *Opaque Texture* açık olmalı
  (`_CameraOpaqueTexture`); VFX Toolkit kontrol eder, kapalıysa uyarır.
- **Soft particle / depth fade:** *Depth Texture* açık olmalı.
- **Tek HLSL, iki tüketici:** `VFXCore.hlsl` hem Shuriken için yazılan
  `.shader` dosyalarında, hem de VFX Graph çıktılarının kullandığı Shader
  Graph'larda (`Support VFX Graph` açık, mantık `Custom Function` düğümünde)
  kullanılır. Böylece dissolve/erosion gibi bir teknik bir kez yazılır, iki
  backend'de aynı görünür.
- **HDR & bloom:** Renkler HDR yoğunluğuyla verilir; Volume'daki bloom eşiği
  VFX Toolkit'ten okunur ki "parlıyor mu" kararı gerçek ayara göre verilsin.
- **Decal:** Zemin izleri için URP Decal Projector (Renderer'da Decal
  feature açık olmalı).
- **SRP Batcher uyumu:** Shader'lar `CBUFFER_START(UnityPerMaterial)` kuralına uyar.

### 6.3 Stilize efekt dili

Hedef stil stilize olduğu için ekibin tüm skill'leri bu dile göre yazılır:

- **Şekil önce gelir:** Net silüetler, keskin/kavisli kontrast, büyük–orta–küçük
  ölçek hiyerarşisi. Partikül "bulutu" yerine **okunur şekiller**.
- **Mesh ağırlıklı:** Slash yayları, swirl'ler, halkalar, koniler, yarım küreler
  üzerinde kayan (panning) dokular — stilize VFX'in omurgası. Mesh Artist
  bu yüzden MVP-1'e alınır.
- **Elle boyanmış görünümlü dokular:** Yumuşak gradient yerine sert kenarlı
  maskeler, 2–3 tonlu basamaklı (posterize) geçişler, fırça hissi veren
  noise'lar. Texture & Vector Artist'in ana aracı SVG + posterize noise.
- **Stepped / toon shading:** Erosion'da `smoothstep` yerine keskin eşik +
  ince parlak kenar (edge glow), 2–3 renk ramp'i (gradient map), ana renk +
  koyu kontur tonu.
- **Flipbook düşük kare sayısı:** 8–16 karelik, "animasyonlu çizim" hissi
  veren flipbook'lar (ör. anime tarzı duman topları, patlama kareleri).
- **Zamanlama:** Hızlı impact (1–3 kare), belirgin anticipation, uzun ve
  yavaşlayan dissipation; smear/stretch.
- **Renk:** Doygun ana renk, beyaza yakın çekirdek, tamamlayıcı aksan rengi;
  koyu arka planda ve açık arka planda okunurluk kontrolü (Critic rubriği).
- **Stil ön ayarları:** `stylized_hand_painted`, `anime_cel`, `moba_readable`
  — Director brief'e göre birini seçer, skill'ler buna göre parametre
  aralıklarını daraltır.

### Mesh Artist
- Prosedürel mesh üretimi (Python → OBJ/FBX veya C# `Mesh` API): düz halka,
  koni, swirl/tornado, slash yayı, yarım küre, ribbon. **UV düzeni efekte
  göre tasarlanır** (ör. halka için U = çevre, V = yarıçap → erosion ve scroll
  doğal çalışır).

### Motion & Timing Designer
- Beat haritasından tüm eğrileri türetir (boyut, alfa, emisyon, ışık, erosion).
- Easing kütüphanesi (expo-out patlamalar, back-in anticipation).
- Kamera sarsıntısı (Cinemachine Impulse varsa), ışık eğrisi, Volume pulse,
  opsiyonel hit-stop.

### VFX Critic
- Sadece spec'e değil **VFX temel ilkelerine** göre de değerlendirir: okunurluk
  (silüet), değer kontrastı, zamanlama ritmi, şekil dili tutarlılığı, renk
  uyumu, "ucuz görünme" işaretleri (tekdüze boyut, sabit hız, düz alfa sönmesi).

---

## 7. Kalite Döngüsü (Critic Rubriği)

| Kriter | Ağırlık | Ölçüm |
|---|---|---|
| Spec / referansa sadakat | 25 | Katmanlar var mı, palet tutuyor mu, referansla benzerlik |
| Okunurluk & silüet | 15 | Koyu ve açık arka planda, küçük boyutta seçilebilir mi |
| Zamanlama & his | 20 | Beat'ler doğru mu, anticipation/impact/dissipation hissediliyor mu |
| Şekil & renk ustalığı | 15 | Değer kontrastı, renk geçişleri, şekil çeşitliliği |
| Teknik temizlik | 10 | Derleme hatası yok, alfa halo yok, sorting problemi yok |
| Performans | 15 | Platform bütçesine uygun mu (`unity_profile_effect`) |

- **Eşik:** ≥ 80 → kullanıcıya sun; < 80 → düzeltme turu (maks. 3).
- Critic çıktısı serbest metin değil, **yönlendirilebilir düzeltme listesi**:
  `{ owner: shader-artist, layer: shockwave, issue: "..", suggestion: ".." }`.
- Hareketi görebilmek için: kare şeridi + GIF + katman başına alfa/boyut eğrisi
  grafiği.

---

## 8. Kullanıcı Deneyimi — "Başlı Başına İyi Hissettirmesi" İçin

1. **Konsept panosu önce** — Palet swatch'ları, katman diyagramı, zaman
   çizelgesi. Kullanıcı daha tek asset üretilmeden "evet, bu" der.
2. **Üç yön (A/B/C)** — `/vfx:explore` ile aynı brief'ten üç farklı yorum
   (örn. zarif / agresif / kaotik) küçük thumbnail'larla; kullanıcı seçer.
3. **Canlı ilerleme** — Görev listesi: "Texture Artist: 4/6 doku", "Shader:
   derlendi ✓".
4. **Sonuç paketi** — Kontakt sayfa, GIF, katman dökümü, performans raporu,
   kullanılan dosyaların listesi.
5. **Sanatçı dostu kontrol** — `EffectController` üzerinde özel inspector:
   *Intensity, Tint, Scale, Speed, Duration* slider'ları; tasarımcı ajana
   dönmeden ince ayar yapabilir.
6. **Doğal dil ayarı** — `/vfx:iterate "sonu daha yumuşak, kıvılcımlar daha
   az"` → sadece ilgili recipe/parametre yamalanır.
7. **Varyant üretimi** — `/vfx:variant --element ice` → aynı efektin buz/zehir/
   kutsal versiyonları (palet + şekil dili dönüşümü, sistem aynı kalır).
8. **Her tur bir commit** — Git ile iterasyon geçmişi; "2 tur önceki daha
   iyiydi" dendiğinde geri dönülebilir.
9. **Arketip kütüphanesi** — Projectile, impact, aura, beam, portal, buff,
   heal, pickup, magic circle, weather, UI sparkle… Her biri kanıtlanmış bir
   katman şablonu; sıfırdan başlamak yerine iyi bir iskeletten başlanır
   (kaliteyi en çok artıran şeylerden biri).

---

## 9. Plugin Dizin Yapısı

```
unity-vfx-designer/
├── .claude-plugin/
│   └── plugin.json
├── commands/
│   ├── create.md          # /vfx:create "<brief>" [--ref img]  — ana orkestrasyon
│   ├── explore.md         # /vfx:explore  — A/B/C yönleri
│   ├── iterate.md         # /vfx:iterate "<geri bildirim>"
│   ├── variant.md         # /vfx:variant --element ice
│   ├── review.md          # /vfx:review   — mevcut bir efekti eleştir
│   └── optimize.md        # /vfx:optimize  — bütçe ve LOD
├── agents/
│   ├── vfx-architect.md
│   ├── texture-artist.md
│   ├── shader-artist.md
│   ├── particle-artist.md
│   ├── mesh-artist.md
│   ├── motion-designer.md
│   ├── vfx-critic.md
│   └── perf-engineer.md
├── skills/
│   ├── vfx-director/            # orkestrasyon protokolü, spec yazımı
│   ├── unity-adapter/           # MCP for Unity araç haritası, tool group açma, yaygın çağrı kalıpları
│   ├── vfx-fundamentals/        # zamanlama, şekil dili, renk, okunurluk
│   ├── effect-archetypes/       # arketip şablonları
│   ├── shuriken-reference/
│   ├── vfxgraph-reference/
│   ├── urp-vfx-shaders/         # HLSL tarif kitabı + VFXCore.hlsl + Shader Graph sarmalayıcıları
│   ├── texture-authoring/       # SVG kalıpları, noise tarifleri, kanal paketleme
│   ├── procedural-meshes/
│   └── pc-budgets/
├── hooks/
│   └── hooks.json               # *.shader → derle, *.svg → rasterize, recipe → şema doğrula
├── schemas/
│   ├── effect-spec.schema.json
│   ├── manifest.schema.json
│   ├── particle-recipe.schema.json
│   ├── vfxgraph-recipe.schema.json
│   └── material-recipe.schema.json
├── tools/                       # Python yardımcıları
│   ├── svg_rasterize.py
│   ├── noise_gen.py
│   ├── flipbook_pack.py
│   ├── mesh_gen.py
│   └── contact_sheet.py
└── unity-package/
    └── com.effectdesigner.vfxtoolkit/   # MCP for Unity'ye [McpForUnityTool] ile custom tool ekler
        ├── Editor/              # vfx_capture_timeline, vfx_apply_particle_recipe, vfx_compile_report, …
        ├── Runtime/             # EffectController (iki backend'i birlikte yönetir), pool arayüzü
        ├── Shaders/             # VFXCore.hlsl, Shader Graph şablonları
        └── VFXTemplates/        # İnsan eliyle yapılmış .vfx şablonları + property imzaları (JSON)
```

### Örnek ajan tanımı

```markdown
---
name: shader-artist
description: Unity 6 URP VFX shader uzmanı. Shuriken için HLSL/ShaderLab,
  VFX Graph için Custom Function tabanlı Shader Graph sarmalayıcıları yazar
  (dissolve, erosion, distortion, flipbook, fresnel).
  manifest.yaml'daki property ve vertex stream sözleşmesine uyar.
tools: Read, Write, Edit, Glob, Grep, mcp__unityMCP__manage_shader,
  mcp__unityMCP__manage_material, mcp__unityMCP__read_console,
  mcp__unityMCP__vfx_compile_report   # MCP sunucu adı kullanıcının kurulumuna göre
skills: urp-vfx-shaders, vfx-fundamentals, unity-adapter
---
Sen kıdemli bir VFX teknik sanatçısısın...
(1) effect.spec.yaml ve manifest.yaml'ı oku
(2) Her katman için gereken shader'ı VFXCore.hlsl üzerine kur
(3) Derle, hata varsa düzelt; derlenmeyen shader teslim etme
(4) Material recipe'lerini yaz
(5) Director'a kısa özet + sözleşmeden sapma varsa gerekçesi
```

### Model dağılımı
- **Director, Critic, Architect:** en güçlü model (yargı ve görsel analiz).
- **Shader, Particle, Mesh, Motion:** güçlü/orta model.
- **Texture yardımcı işleri, şema doğrulama:** hızlı/ucuz model.

---

## 10. Yol Haritası

| Faz | Kapsam | Başarı kriteri |
|---|---|---|
| **MVP-1** | MCP for Unity entegrasyonu (`unity-adapter` skill); VFX Toolkit custom tool'ları (`vfx_capture_timeline`, `vfx_apply_particle_recipe`, `vfx_compile_report`, `vfx_project_check`); VFX Graph şablon kütüphanesi (MCP'nin `vfx_*` araçlarıyla doldurulur); Architect, Texture&Vector, Shader (`VFXCore.hlsl` + `.shader` + Shader Graph sarmalayıcı, stilize/stepped), Particle (Shuriken + ~8 VFX Graph şablonu), Mesh, Critic; 4 arketip (impact, projectile, aura, pickup) | "Mor bir büyü çarpması" brief'inden 3 tur içinde kabul edilebilir, çalışan, iki backend'i karışık kullanan prefab |
| **MVP-2** | Motion Designer (kamera/ışık), Performance Engineer, konsept panosu, `/vfx:iterate`, `/vfx:variant`, VFX Graph modüler kompozisyon + Custom HLSL blokları | Stilize MOBA kalitesine yaklaşan katmanlı efektler; PC bütçe raporu |
| **MVP-3** | Programatik VFX Graph kurucu (deneysel), Style Librarian (proje hafızası), `/vfx:explore` A/B/C | Proje stilini öğrenen, tutarlı efekt setleri üreten stüdyo |

---

## 11. Riskler

| Risk | Azaltma |
|---|---|
| LLM'in hareketi statik karelerden yargılaması zor | Yoğun kare şeridi + GIF + eğri grafikleri; zamanlama kararları Motion Designer'ın sayısal beat haritasına dayanır |
| Unity YAML/GUID kırılganlığı | Deklaratif recipe + C# builder; ham YAML yazımı yasak |
| VFX Graph / Shader Graph dosya formatları kırılgan, grafik API'si `internal` | Şablon + exposed property (MVP-1), Custom HLSL (MVP-2); programatik kurucu sürüm-kilitli ve deneysel (MVP-3) |
| VFX Graph şablon kütüphanesinin kalitesi tüm sonucu belirler | Şablonlar insan eliyle yapılır/gözden geçirilir; her şablonun referans render'ı ve property imzası testlerle korunur |
| Token maliyeti (çok ajan, çok tur) | Skill'lerle ihtiyaç anında bilgi yükleme; tur limiti; ucuz model yardımcı işlerde; sadece değişen katmanın yeniden üretimi |
| Editor açık olmadan render yok | Yerel çalışma şartı; GPU'lu batchmode yedeği |
| MCP for Unity'nin araç adları/parametreleri sürümle değişebilir | Desteklenen sürüm aralığı belgelenir; araç haritası tek yerde (`unity-adapter`); kritik işler zaten bizim custom tool'larımızda |
| Renk uzayı / HDR tutarsızlığı | VFX Toolkit Linear/Gamma ve HDR ayarını okur, spec renkleri buna göre dönüştürülür |

---

## 12. Karar Bekleyen Sorular

1. ~~Hedef Unity sürümü ve pipeline?~~ → **Unity 6 + URP** ✓
2. ~~Partikül önceliği?~~ → **Shuriken + VFX Graph birlikte** ✓
3. ~~Stil?~~ → **Stilize** ✓
4. ~~Platform?~~ → **PC / konsol, mobil yok** ✓
5. ~~Köprü?~~ → **CoplayDev/unity-mcp + VFX Toolkit custom tool'ları** ✓
6. ~~Efekt içi 2D sprite/doku çizimi?~~ → **Evet, çekirdek yetenek** (Texture & Vector Artist, §6) ✓
7. **2D oyun efektleri** (SpriteRenderer sahneleri, UI Canvas üzerinde
   partikül) de kapsamda mı? Bu, 3D sahnedeki efektin sprite kullanmasından
   farklı: kamera, sorting layer ve UI render akışı değişir.
