# AGENTS.md — Project Skill Registry & Engineering Standards (`Puzzle 2D`)

Tệp cấu hình này ghi nhớ toàn bộ **162 skills** trong `.agents/skills/` (161 thư mục chuyên biệt + bộ định tuyến `router` / `unity-skills-index`) vào hệ thống của dự án **Puzzle 2D (Unity 6000.4.0f1 URP)**, cùng các quy tắc kỹ thuật bắt buộc cho mọi tác vụ phát triển và kiểm chứng.

---

## 1. Quy Tắc Vận Hành & Kiểm Chứng Bắt Buộc

1. **Cấm tuyệt đối dùng Editor tự ý sửa setup UI trong Scene / Prefab**:
   - **CẤM 100%** dùng Unity Editor CLI / `run_script` / `AgentScripts` để tự động tạo, sửa, di chuyển hoặc gán lại bất kỳ đối tượng UI, `RectTransform`, `Canvas` hay tham chiếu UI nào trong `Assets/Scenes/GameScene.unity` hoặc Prefab UI.
   - Unity Editor **chỉ được phép dùng để spawn file Asset & `ScriptableObject`** (và chạy `recompile` / `run_tests`), đồng thời **mọi bản kế hoạch (`Plan`) bắt buộc phải xin phép trước** sẽ dùng Editor như thế nào và viết code script như thế nào.
   - Không tự đoán mò hay tự quyết định hành vi UX/UI khi chưa hỏi ý kiến người dùng.
2. **Điều khiển Unity Editor qua `unity-cli` & `unity-pipeline` (Chỉ cho Asset / ScriptableObject & Test)**:
   - Luôn bật `unity command set_autotick --enable true` trước khi chạy tác vụ không tiêu điểm (headless/unfocused).
   - Chỉ dùng script hỗ trợ khi được phép tạo `.asset` (`ScriptableObject`).
   - Sau khi sửa script trong `Assets/`, gọi `unity command recompile` -> chờ `unity command recompile_status` báo hoàn tất -> chạy `unity command run_tests --mode editor`.
3. **Quy chuẩn uGUI (`ui-ugui`, `game-ui-ux`, `unity-ui`)**:
   - **Thanh tiến độ (`Progress Bar`)**: Ưu tiên dùng `UnityEngine.UI.Slider` (`interactable = false`, `transition = None`, `handleRect = null`, `fillRect` trỏ tới ảnh `Sliced`) thay vì `Image.fillAmount` trên sprite `Sliced`.
   - **Vùng cuộn dọc / ngang (`ScrollView`)**: Mọi danh sách động (`VerticalLayoutGroup` / `HorizontalLayoutGroup`) có khả năng vượt khung nhìn bắt buộc phải đặt trong cấu trúc 3 tầng chuẩn:
     `ScrollView (ScrollRect)` -> `Viewport (RectMask2D + Image alpha=0 raycastTarget=true)` -> `Content (LayoutGroup + ContentSizeFitter, pivot chuẩn (0.5, 1) cho cuộn dọc hoặc (0, 0.5) cho cuộn ngang)`.
   - **Thứ tự vẽ & Raycast (Sibling Z-Order)**: Trong uGUI, con nằm dưới cùng trong Hierarchy vẽ đè lên trên và nhận Raycast trước. Không bao giờ để template ẩn/nút thừa hoặc khung trang trí `raycastTarget = true` nằm đè lên vùng danh sách nút bấm.
   - **Độ mờ (`Opacity / Alpha`) & Animation**: Không tự ý hạ opacity (`alpha < 1f` hoặc `Button.colors.disabledColor` mờ 50%) lên icon rương/vật phẩm khi người dùng không yêu cầu; phân biệt trạng thái qua Sprite (`incompleteSprite` / `completeSprite` / `claimedSprite`). Không dùng `GetComponent<UIPanelAnimator>()` tự bắt ngầm — chỉ chạy animation khi trường `[SerializeField] private UIPanelAnimator panelAnimator` được gán tường minh trong Inspector.
4. **Quy chuẩn `DOTween` (`unity-dotween-design`)**:
   - Mọi tween trên UI phải gọi `.DOKill()` trước khi tạo mới, gắn `.SetUpdate(true)` (để vẫn chạy mượt khi `Time.timeScale == 0` lúc Pause) và `.SetLink(gameObject)` (tự hủy an toàn khi object bị Destroy).
5. **Quy chuẩn Dữ liệu `ScriptableObject` (`unity-scriptableobjects`)**:
   - Mọi cấu hình nhiệm vụ (`MissionDefinition`), mốc thưởng tuần (`MissionMilestoneDefinition`), vật phẩm cửa hàng (`ShopItemDefinition`), kỹ năng (`PowerUpData`), màn chơi (`LevelData`) phải tách thành `ScriptableObject` có `[CreateAssetMenu]` và校验 dữ liệu trong `OnValidate()`.

---

## 2. Bản Đồ Định Tuyến Toàn Bộ Danh Mục Skills Trong `.agents/skills/`

Khi thực hiện tác vụ thuộc nhóm nào, đọc trực tiếp `SKILL.md` tương ứng tại `.agents/skills/<skill-name>/SKILL.md`:

### A. Điều Khiển Unity Editor, CLI, Pipeline & Kiểm Thử (22 skills)
- **Định tuyến tổng**: `router`, `unity-skills`, `unity-skills-index`
- **CLI & Live Pipeline**: `unity-cli`, `unity-pipeline`, `unity-editor`, `unity-batch`, `unity-workflow`, `unity-history`, `unity-bookmark`, `unity-sample`
- **Kiểm thử, Console & Audit**: `unity-test`, `unity-testability`, `unity-console`, `unity-debug`, `unity-validation`, `unity-cleaner`, `project-auditor-fixes`, `unity-perception`, `unity-project`, `unity-project-scout`, `generate-editor-search-query`

### B. Hệ Thống Giao Diện UI/UX & TextMeshPro (9 skills)
- **Định tuyến & Kiến trúc UI**: `ui`, `game-ui-ux`
- **uGUI (Canvas / RectTransform / ScrollRect / Slider)**: `ui-ugui`, `unity-ui`
- **UI Toolkit & IMGUI Editor**: `ui-uitk`, `unity-uitoolkit`, `ui-imgui`
- **Font & Đa ngôn ngữ**: `optimize-text-mesh-pro`, `localization`

### C. Kiến Trúc C#, ScriptableObject & Design Patterns (15 skills)
- **Kiến trúc & Vai trò Script**: `game-architect`, `unity-architecture`, `unity-blueprints`, `unity-adr`, `unity-patterns`, `unity-script-roles`, `unity-scriptdesign`, `unity-scene-contracts`
- **Lập trình C# & Dữ liệu**: `unity-csharp-scripting`, `unity-script`, `unity-scriptableobjects`, `unity-scriptableobject`, `unity-asmdef`, `unity-async`, `unity-unitask-design`, `unity-inspector`, `unity-event`, `skill-creator`

### D. Gameplay 2D Puzzle, Vật Lý, Tilemap & Điều Khiển (14 skills)
- **Puzzle & Level Design**: `puzzle`, `level-design`, `prototype-fast`, `procedural-gen`
- **2D Tilemap, Sprite & Pixel Perfect**: `unity-tilemap-2d`, `create-tile-palette-or-rule-tiles`, `2d-pixel-perfect`, `sprite-editor`, `sprite-segment-3x3grid`, `manage-sprite-atlas`
- **Vật lý & Điều khiển & Camera**: `create-2d-physics`, `physics-tuning`, `unity-physics`, `physics-3d-collision`, `input-systems`, `unity-input-system`, `camera-systems`, `unity-camera`, `unity-cinemachine`

### E. Kinh Tế Game, Lưu Trữ, Live-Ops, IAP & Quảng Cáo (8 skills)
- **Cân bằng số liệu & Review thiết kế**: `game-balance-analysis`, `game-design-review`
- **Save/Load & Cloud**: `save-systems`, `build-live-game`
- **Kiếm tiền (IAP & Ads)**: `implement-in-app-purchases`, `levelplay-unity-integration`
- **Mạng & Voice**: `setup-multiplayer-services`, `unity-netcode`, `unity-netcode-design`, `setup-vivox-voice-chat`

### F. Hiệu Ứng, Tweening, Đồ Họa URP, Shader & Âm Thanh (24 skills)
- **Game Feel & Tweening**: `game-feel`, `unity-dotween`, `unity-dotween-design`, `unity-primetween`, `unity-primetween-design`
- **Hoạt ảnh & Cutscene**: `unity-animation`, `unity-animator`, `unity-timeline`
- **URP, Ánh sáng, Post-Processing & Shader**: `unity-urp`, `migrate-birp-to-urp`, `urp-postprocessing`, `unity-postprocess`, `unity-volume`, `validate-urp-render-graph-renderer-feature`, `unity-graphics`, `unity-light`, `unity-material`, `unity-decal`, `shader-programming`, `unity-shader`, `unity-shadergraph`, `unity-shadergraph-design`, `shader-graph-create-custom-node`, `animation-shader`
- **Âm thanh**: `audio-design`, `audio-setup-mixers`, `optimize-audio`

### G. Quản Lý Scene, Prefab, Asset, Tối Ưu Hóa & Build/Publish (27 skills)
- **Scene, GameObject & Prefab**: `unity-scene`, `unity-gameobject`, `unity-component`, `unity-prefab`, `unity-asset`, `unity-importer`, `unity-yaml-editing`, `unity-manual-scene`, `unity-manual-gameobject`, `unity-manual-component`, `unity-manual-material`, `unity-smart`
- **Tài nguyên Mỹ thuật**: `create-game-assets`, `game-image-generation-rules`, `asset-transformer-toolkit`, `unity-probuilder`, `unity-terrain`
- **Hiệu năng & Bộ nhớ**: `performance-optimization`, `unity-performance`, `unity-optimization`, `unity-profiler`, `optimize-web`
- **Gói tài nguyên, Đóng gói & Phát hành**: `new-unity-project`, `unity-package`, `unity-package-management`, `unity-addressables`, `unity-addressables-design`, `unity-yooasset`, `unity-yooasset-design`, `unity-hybridclr`, `unity-build-pipeline`, `itch-publish`, `steam-publish`, `game-jam`

### H. AI, Thể Loại Mở Rộng, Framework Khác & XR (Tham chiếu mở rộng)
- **AI & Hội thoại**: `game-ai`, `ai-behavior-trees-utility-ai`, `unity-behavior`, `initialize-ai-navigation`, `unity-navmesh`, `dialogue-systems`
- **Mẫu thể loại & Framework tham khảo**: `platformer`, `roguelike`, `rpg`, `card-game`, `fps-shooter`, `survival-crafting`, `tower-defense`, `unity-qframework`, `unity-qframework-design`, `build-gtk`, `unity-xr`, `unity-pico-design`, `defold-core`, `ebitengine-game`, `phaser-core`, `phaser-arcade-physics`, `pixijs-rendering`
