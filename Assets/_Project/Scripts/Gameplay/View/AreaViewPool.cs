using Arena.Core.Defs;
using Arena.Core.Math;
using Arena.Core.Sim;
using Arena.Core.State;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Views of <see cref="MatchState.Areas"/> by slot (S2.11), besides the telegraph discs of <see cref="TelegraphViewPool"/>:
    /// a lobbed bomb flying along a parabola from <see cref="AreaState.Origin"/> to the landing point until it bursts, a mine
    /// (only if the local team sees it, D-030) and a translucent dome of a field area coloured by team. Views are created on
    /// first need per slot and kind and reused; no allocations afterwards.
    /// </summary>
    public sealed class AreaViewPool
    {
        private readonly Transform[] _bombs = new Transform[SimLimits.MaxAreas];
        private readonly Transform[] _mines = new Transform[SimLimits.MaxAreas];
        private readonly Transform[] _domes = new Transform[SimLimits.MaxAreas];
        private readonly Renderer[] _domeRenderers = new Renderer[SimLimits.MaxAreas];
        private readonly ViewCatalog _catalog;
        private readonly Transform _parent;

        public AreaViewPool(ViewCatalog catalog, Transform parent)
        {
            _catalog = catalog;
            _parent = parent;
        }

        /// <summary>Views currently shown (tests).</summary>
        public int ShownCount => Count(_bombs) + Count(_mines) + Count(_domes);

        /// <summary>
        /// Flight fraction 0..1 of a lob at <paramref name="tick"/> + <paramref name="alpha"/>, or −1 if no bomb is in the air
        /// (not a burst, already burst, or created live without a flight).
        /// </summary>
        public static float FlightProgress(in AreaState a, int tick, float alpha)
        {
            if (!a.Active || a.Triggered || a.Kind != AreaKind.Burst) return -1f;
            int flight = a.ActivateTick - a.SpawnTick;
            if (flight <= 0) return -1f;
            return MathUtil.Clamp01((tick - a.SpawnTick + alpha) / flight);
        }

        /// <summary>Height of a lob at flight fraction <paramref name="t"/>: from the launch height down to the ground plus an arc.</summary>
        public static float ArcHeight(float t, float launchHeight, float peak) => launchHeight * (1f - t) + 4f * peak * t * (1f - t);

        public void Render(MatchState state, float alpha, int localTeam, float launchHeight, Material allyDome, Material enemyDome)
        {
            AreaState[] areas = state.Areas;
            int tick = state.Globals.Tick;
            for (int i = 0; i < areas.Length; i++)
            {
                ref AreaState a = ref areas[i];
                float flight = FlightProgress(in a, tick, alpha);
                bool bomb = flight >= 0f;
                bool mine = a.Active && a.Kind == AreaKind.Mine && !a.Triggered && a.IsVisibleTo(localTeam);
                bool dome = a.Active && a.Kind == AreaKind.Field;

                if (Show(_bombs, i, bomb, _catalog.LobBombPrefab, "LobBomb"))
                {
                    Vec2 p = Vec2.Lerp(a.Origin, a.Position, flight);
                    _bombs[i].position = WorldCoords.ToWorld(p, ArcHeight(flight, launchHeight, _catalog.LobArcHeight));
                }
                if (Show(_mines, i, mine, _catalog.MinePrefab, "Mine")) _mines[i].position = WorldCoords.ToWorld(a.Position);
                if (Show(_domes, i, dome, _catalog.DomePrefab, "Dome"))
                {
                    Transform t = _domes[i];
                    float d = a.Radius * 2f;
                    t.position = WorldCoords.ToWorld(a.Position);
                    t.localScale = new Vector3(d, d, d);
                    if (_domeRenderers[i] == null) _domeRenderers[i] = t.GetComponentInChildren<Renderer>();
                    Material m = a.Team == localTeam ? allyDome : enemyDome;
                    Renderer r = _domeRenderers[i];
                    if (r != null && m != null && r.sharedMaterial != m) r.sharedMaterial = m;
                }
            }
        }

        public void HideAll()
        {
            HideAll(_bombs);
            HideAll(_mines);
            HideAll(_domes);
        }

        /// <summary>Shows (creating on first need) or hides the view of slot <paramref name="i"/>; true if it is shown.</summary>
        private bool Show(Transform[] views, int i, bool shown, GameObject prefab, string name)
        {
            Transform view = views[i];
            if (!shown)
            {
                if (view != null && view.gameObject.activeSelf) view.gameObject.SetActive(false);
                return false;
            }
            if (view == null)
            {
                if (prefab == null) return false;
                GameObject go = Object.Instantiate(prefab, _parent);
                go.name = name + i;
                view = go.transform;
                views[i] = view;
            }
            if (!view.gameObject.activeSelf) view.gameObject.SetActive(true);
            return true;
        }

        private static int Count(Transform[] views)
        {
            int n = 0;
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null && views[i].gameObject.activeSelf) n++;
            return n;
        }

        private static void HideAll(Transform[] views)
        {
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null) views[i].gameObject.SetActive(false);
        }
    }
}
