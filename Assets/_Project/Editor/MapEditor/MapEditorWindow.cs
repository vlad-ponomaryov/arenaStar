using System;
using Arena.Core.Map;
using Arena.Core.Sim;
using Arena.Data;
using Arena.Data.Conversion;
using UnityEditor;
using UnityEngine;

namespace Arena.Editor.MapEditor
{
    /// <summary>
    /// Arena/Map Editor (S2.6): paint the cells of a <see cref="MapConfig"/> with a 1×1 / 3×3 brush, place spawns and the
    /// hollow, resize, mirror one half onto the other, check symmetry and the match-start rules. Thin view: all edits go
    /// through <see cref="MapEditorLogic"/>; every stroke is written to the config at once under <see cref="Undo"/>.
    /// Note: DataSeeder rewrites the seeded maps (D-010, D-055) — use "Copy rows" to move a drawn layout into the seeder.
    /// Never opened in batchmode.
    /// </summary>
    public sealed class MapEditorWindow : EditorWindow
    {
        private const string MenuPath = "Arena/Map Editor";
        private const float MinCellSize = 8f;
        private const float MaxCellSize = 40f;
        private const float DefaultCellSize = 18f;
        private const float CellGap = 1f;
        private const int MinSide = 3;

        private static readonly PaletteEntry[] Palette =
        {
            new PaletteEntry(MapConverter.FloorChar, "Floor", new Color(0.55f, 0.75f, 0.45f)),
            new PaletteEntry(MapConverter.WallChar, "Wall", new Color(0.35f, 0.3f, 0.28f)),
            new PaletteEntry(MapConverter.BreakableWallChar, "Breakable", new Color(0.7f, 0.5f, 0.3f)),
            new PaletteEntry(MapConverter.BushChar, "Bush", new Color(0.15f, 0.45f, 0.15f)),
            new PaletteEntry(MapConverter.WaterChar, "Water", new Color(0.25f, 0.5f, 0.85f)),
            new PaletteEntry(MapConverter.BerryBoxChar, "Berry box", new Color(0.75f, 0.3f, 0.55f)),
            new PaletteEntry(MapConverter.Team0SpawnChar, "Spawn A (team 0)", new Color(0.3f, 0.6f, 1f)),
            new PaletteEntry(MapConverter.Team1SpawnChar, "Spawn B (team 1)", new Color(1f, 0.35f, 0.3f)),
            new PaletteEntry(MapConverter.HollowChar, "Hollow", new Color(0.95f, 0.8f, 0.2f)),
            new PaletteEntry(MapConverter.SharedSpawnChar, "Spawn S (shared)", new Color(0.85f, 0.85f, 0.85f))
        };

        private static readonly string[] BrushLabels = { "Brush 1×1", "Brush 3×3" };
        private static readonly int[] BrushSizes = { 1, 3 };
        private static readonly Color MismatchColor = new Color(1f, 0f, 1f);
        private static readonly Color BackgroundColor = new Color(0.1f, 0.1f, 0.1f);

        [SerializeField] private MapConfig _config;
        [SerializeField] private int _paletteIndex = 1;
        [SerializeField] private int _brushIndex;
        [SerializeField] private float _cellSize = DefaultCellSize;
        [SerializeField] private int _newWidth;
        [SerializeField] private int _newHeight;

        private MapGrid _grid;
        private string _loadError;
        private string _status;
        private MessageType _statusType = MessageType.None;
        private bool _hasMismatch;
        private GridCell _mismatch;
        private Vector2 _scroll;
        private GUIStyle _markerStyle;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<MapEditorWindow>("Map Editor");
            window.minSize = new Vector2(420f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            Reload();
        }

        private void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

        private void OnUndoRedo()
        {
            Reload();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            _config = (MapConfig)EditorGUILayout.ObjectField("Map", _config, typeof(MapConfig), false);
            if (EditorGUI.EndChangeCheck())
            {
                Reload();
                ClearStatus();
            }
            if (_config == null)
            {
                EditorGUILayout.HelpBox("Select a MapConfig asset (Assets/_Project/Data/Maps).", MessageType.Info);
                return;
            }
            if (_grid == null)
            {
                DrawBrokenMap();
                return;
            }

            DrawToolbar();
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, _statusType);
            DrawGrid();
        }

        private void DrawBrokenMap()
        {
            EditorGUILayout.HelpBox("Rows cannot be edited: " + _loadError, MessageType.Error);
            DrawSizeFields();
            if (GUILayout.Button("Replace with an empty " + _newWidth + "×" + _newHeight + " map"))
            {
                _grid = new MapGrid(Mathf.Max(MinSide, _newWidth), Mathf.Max(MinSide, _newHeight), MapConverter.FloorChar);
                WriteToConfig("New map");
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.LabelField(_config.Id + " — " + _grid.Width + "×" + _grid.Height +
                                       (_config.RequireCentralSymmetry ? ", symmetric" : "") +
                                       ", spawns per team: " + _config.RequiredSpawnsPerTeam, EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < Palette.Length; i++)
                {
                    Color old = GUI.backgroundColor;
                    GUI.backgroundColor = Palette[i].Color;
                    bool selected = GUILayout.Toggle(_paletteIndex == i, Palette[i].Symbol.ToString(), EditorStyles.miniButton,
                        GUILayout.MinWidth(24f));
                    GUI.backgroundColor = old;
                    if (selected) _paletteIndex = i;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Paint: " + Palette[_paletteIndex].Name, GUILayout.MinWidth(140f));
                _brushIndex = GUILayout.Toolbar(_brushIndex, BrushLabels);
            }
            _cellSize = EditorGUILayout.Slider("Cell size", _cellSize, MinCellSize, MaxCellSize);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSizeFields();
                if (GUILayout.Button("Resize", GUILayout.Width(70f))) ApplyResize();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Check symmetry")) RunSymmetryCheck();
                if (GUILayout.Button("Validate")) RunValidate();
                if (GUILayout.Button("Mirror top → bottom")) Mirror(true);
                if (GUILayout.Button("Mirror bottom → top")) Mirror(false);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save")) Save();
                if (GUILayout.Button("Copy rows")) CopyRows();
            }
        }

        private void DrawSizeFields()
        {
            _newWidth = EditorGUILayout.IntField("W", _newWidth);
            _newHeight = EditorGUILayout.IntField("H", _newHeight);
        }

        private void DrawGrid()
        {
            float step = _cellSize + CellGap;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            Rect area = GUILayoutUtility.GetRect(_grid.Width * step, _grid.Height * step, GUILayout.ExpandWidth(false),
                GUILayout.ExpandHeight(false));
            if (Event.current.type == EventType.Repaint) DrawCells(area, step);
            HandleMouse(area, step);
            EditorGUILayout.EndScrollView();
        }

        private void DrawCells(Rect area, float step)
        {
            if (_markerStyle == null)
                _markerStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            EditorGUI.DrawRect(area, BackgroundColor);
            for (int t = 0; t < _grid.Height; t++)
            {
                for (int c = 0; c < _grid.Width; c++)
                {
                    char ch = _grid.Get(c, t);
                    var cell = new Rect(area.x + c * step, area.y + t * step, _cellSize, _cellSize);
                    EditorGUI.DrawRect(cell, ColorOf(ch));
                    if (MapEditorLogic.IsMarker(ch)) GUI.Label(cell, ch.ToString(), _markerStyle);
                }
            }
            if (_hasMismatch)
            {
                int t = _grid.TextRowOfMapRow(_mismatch.Row);
                if (_grid.Contains(_mismatch.Column, t))
                {
                    var cell = new Rect(area.x + _mismatch.Column * step, area.y + t * step, _cellSize, _cellSize);
                    DrawFrame(cell, MismatchColor);
                    int mt = _grid.Height - 1 - t;
                    int mc = _grid.Width - 1 - _mismatch.Column;
                    DrawFrame(new Rect(area.x + mc * step, area.y + mt * step, _cellSize, _cellSize), MismatchColor);
                }
            }
        }

        private void HandleMouse(Rect area, float step)
        {
            Event e = Event.current;
            if (e.button != 0 || (e.type != EventType.MouseDown && e.type != EventType.MouseDrag)) return;
            if (!area.Contains(e.mousePosition)) return;
            int c = Mathf.FloorToInt((e.mousePosition.x - area.x) / step);
            int t = Mathf.FloorToInt((e.mousePosition.y - area.y) / step);
            if (!_grid.Contains(c, t)) return;

            if (e.type == EventType.MouseDown)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Paint map");
            }
            Undo.RecordObject(_config, "Paint map");
            int changed = MapEditorLogic.Paint(_grid, c, t, Palette[_paletteIndex].Symbol, BrushSizes[_brushIndex]);
            if (changed > 0)
            {
                _hasMismatch = false;
                WriteRows();
            }
            e.Use();
            Repaint();
        }

        private void ApplyResize()
        {
            int width = Mathf.Max(MinSide, _newWidth);
            int height = Mathf.Max(MinSide, _newHeight);
            if (width * height > SimLimits.MaxMapCells)
            {
                SetStatus("Too many cells: " + width * height + " > SimLimits.MaxMapCells " + SimLimits.MaxMapCells, MessageType.Error);
                return;
            }
            _grid = MapEditorLogic.Resize(_grid, width, height);
            WriteToConfig("Resize map");
            SetStatus("Resized to " + width + "×" + height + " (content kept at the bottom-left, team 0 side).", MessageType.Info);
        }

        private void Mirror(bool fromTop)
        {
            MapEditorLogic.MirrorCentral(_grid, fromTop);
            WriteToConfig(fromTop ? "Mirror top to bottom" : "Mirror bottom to top");
            RunSymmetryCheck();
        }

        private void RunSymmetryCheck()
        {
            bool ok = MapEditorLogic.CheckSymmetry(_grid, _config.Id, out _mismatch, out _hasMismatch, out string error);
            if (ok) SetStatus("Centrally symmetric.", MessageType.Info);
            else SetStatus(error, MessageType.Warning);
        }

        private void RunValidate()
        {
            _hasMismatch = false;
            bool ok = MapEditorLogic.Validate(_grid, _config.Id, _config.RequiredSpawnsPerTeam, _config.RequireCentralSymmetry,
                out string error);
            if (ok) SetStatus("Map is valid for a match.", MessageType.Info);
            else SetStatus(error, MessageType.Error);
            if (!ok && _config.RequireCentralSymmetry)
                MapEditorLogic.CheckSymmetry(_grid, _config.Id, out _mismatch, out _hasMismatch, out _);
        }

        private void Save()
        {
            WriteRows();
            AssetDatabase.SaveAssetIfDirty(_config);
            SetStatus("Saved " + AssetDatabase.GetAssetPath(_config) + ". DataSeeder overwrites seeded maps — see Copy rows.",
                MessageType.Info);
        }

        private void CopyRows()
        {
            string[] rows = MapEditorLogic.ToRows(_grid);
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < rows.Length; i++)
                text.Append("    \"").Append(rows[i].Replace("\"", "\\\"")).Append("\",\n");
            EditorGUIUtility.systemCopyBuffer = text.ToString();
            SetStatus("Rows copied to the clipboard as C# string literals.", MessageType.Info);
        }

        private void WriteToConfig(string undoName)
        {
            Undo.RecordObject(_config, undoName);
            _hasMismatch = false;
            WriteRows();
            Repaint();
        }

        private void WriteRows()
        {
            _config.Rows = MapEditorLogic.ToRows(_grid);
            EditorUtility.SetDirty(_config);
        }

        private void Reload()
        {
            _grid = null;
            _loadError = null;
            _hasMismatch = false;
            if (_config == null) return;
            try
            {
                _grid = MapEditorLogic.FromRows(_config.Rows);
                _newWidth = _grid.Width;
                _newHeight = _grid.Height;
            }
            catch (ArgumentException e)
            {
                _loadError = e.Message;
            }
        }

        private void SetStatus(string text, MessageType type)
        {
            _status = text;
            _statusType = type;
        }

        private void ClearStatus() => SetStatus(null, MessageType.None);

        private static Color ColorOf(char ch)
        {
            for (int i = 0; i < Palette.Length; i++)
                if (Palette[i].Symbol == ch) return Palette[i].Color;
            return Color.black;
        }

        private static void DrawFrame(Rect r, Color color)
        {
            const float w = 2f;
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, w), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - w, r.width, w), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, w, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - w, r.y, w, r.height), color);
        }
    }
}
