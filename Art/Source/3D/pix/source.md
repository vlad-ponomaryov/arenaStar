# Пикс — 3D из image-to-3D (спайк 2026-10-19, `Docs/Spike-ImageTo3D.md`)

Вход у всех — RGBA-вырезка `Art/Source/Flow/2026-10-19_pix-3dref/001.png`. Вне `Assets`, в сборку не входит. GLB/FBX — через Git LFS.

| Файл | Откуда | Лицензия результата | Цифры |
| --- | --- | --- | --- |
| `trellis2_001_1024.glb` | TRELLIS.2-4B (MIT), Space `Upsampler/trellis-2` (MIT, сторонняя копия: фон — BiRefNet MIT, DINOv3 — DINOv3 License), разрешение 1024, seed 42, decimation 40 000, текстура 2048; без входа в аккаунт, 19.10 12:31 | наш (MIT не ограничивает результат); оговорка про nvdiffrast — в спайке | 37 346 треуг., 1425 кусков, 1571 неманиф. рёбер; **текстура не совпадает с UV** (брак этой копии) — используем только форму |
| `sf3d_001_tri10k.glb` | Stable Fast 3D, Space `stabilityai/stable-fast-3d`, ремеш Triangle, 10 000 вершин, текстура 2048 | Stability Community License: коммерция < $1 млн **после регистрации у Stability** — в игру не берём | 18 076 треуг., 1 кусок, плоский «рельеф» толщиной 0,19 высоты, спина — бурое пятно |
| `pix_game4k_rigged.fbx/.glb` | `trellis2_001_1024.glb` → `Tools/Blender/prepare_fighter.py --tris 4000 --snap` (Blender 5.2.2 LTS) | наш | 3 999 треуг., 1 770 верш., вершинные цвета из видов Flow (палитра ТЗ), 8 костей FighterBone, автовеса |
| `pix_show20k_rigged.fbx/.glb` | то же, `--tris 20000 --snap` | наш | 19 998 треуг., 9 770 верш. |
