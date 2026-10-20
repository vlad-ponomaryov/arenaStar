# Комбо — базовая 3D-модель (A-Roster3D волна 2, 2026-10-20)

Сборка: `blender -b --factory-startup --python Tools/Blender/fighters/build_kombo.py -- [out_dir]`; лист — `render_kombo_sheet.py` → `Tools/out/art/roster3d/kombo_sheet.png`.
`kombo_game.fbx/.glb` — 3 923 треуг., 12 костей (Root, Body, Head, ArmR/L, LegR/L, Prop = правая лапа, EyeR/L, LidR/L), текстура `kombo_tex.png`;
`kombo_weapon.fbx/.glb` — снежок 1 173 треуг. (origin = хват на Prop). Пушистый белый ком-йети, лицо сиреневато-серое, кубик льда
`#BFEFFF` на макушке, сиреневый шарф `#B48CE6`/`#8A62C4` с хвостом по спине, лапы/подошвы `#8C8198`. Масштаб в игре — как у Тео.
WIP: не сделано — звуки (synth_kombo.py), последний лист после правки хвоста шарфа не просмотрен, таяние кубика/эмиссия — в Unity.
