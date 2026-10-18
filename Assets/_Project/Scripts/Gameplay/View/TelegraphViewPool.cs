using Arena.Core.Defs;
using Arena.Core.Sim;
using Arena.Core.State;
using UnityEngine;

namespace Arena.Gameplay.View
{
    /// <summary>
    /// Telegraphs of <see cref="MatchState.Areas"/> by slot (S2.10 frame for the area attacks of S2.11–S2.12): an area that has not
    /// triggered yet (bursts only — mines and fields are drawn by <see cref="AreaViewPool"/>) is shown during the last
    /// <c>telegraphTicks</c> before its <see cref="AreaState.ActivateTick"/>, coloured by
    /// the owner's team relative to the local player. Views are created on first need and reused; no allocations afterwards.
    /// </summary>
    public sealed class TelegraphViewPool
    {
        private readonly TelegraphView[] _views = new TelegraphView[SimLimits.MaxAreas];
        private readonly TelegraphView _prefab;
        private readonly Transform _parent;

        public TelegraphViewPool(TelegraphView prefab, Transform parent)
        {
            _prefab = prefab;
            _parent = parent;
        }

        /// <summary>Views currently shown (tests).</summary>
        public int ShownCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _views.Length; i++)
                    if (_views[i] != null && _views[i].gameObject.activeSelf) n++;
                return n;
            }
        }

        /// <summary>
        /// Telegraph progress of an area at <paramref name="tick"/> + <paramref name="alpha"/> of the next tick: 0..1 while it
        /// is shown (1 = triggers now), −1 if it is not shown (inactive, triggered, or more than <paramref name="telegraphTicks"/> away).
        /// </summary>
        public static float Progress(in AreaState a, int tick, int telegraphTicks, float alpha)
        {
            if (!a.Active || a.Triggered || a.Kind != AreaKind.Burst || telegraphTicks <= 0) return -1f;
            float left = a.ActivateTick - tick - alpha;
            if (left > telegraphTicks) return -1f;
            if (left < 0f) left = 0f;
            return 1f - left / telegraphTicks;
        }

        public void Render(MatchState state, int telegraphTicks, float alpha, int localTeam, Material allyZone, Material allyFill,
            Material enemyZone, Material enemyFill)
        {
            AreaState[] areas = state.Areas;
            int tick = state.Globals.Tick;
            for (int i = 0; i < areas.Length; i++)
            {
                ref AreaState a = ref areas[i];
                float p = Progress(in a, tick, telegraphTicks, alpha);
                TelegraphView view = _views[i];
                if (p < 0f)
                {
                    if (view != null) view.Hide();
                    continue;
                }
                if (view == null)
                {
                    if (_prefab == null) continue;
                    view = Object.Instantiate(_prefab, _parent);
                    view.name = "Telegraph" + i;
                    _views[i] = view;
                }
                bool ally = a.Team == localTeam;
                view.SetMaterials(ally ? allyZone : enemyZone, ally ? allyFill : enemyFill);
                view.Show(WorldCoords.ToWorld(a.Position), a.Radius, p);
            }
        }

        public void HideAll()
        {
            for (int i = 0; i < _views.Length; i++)
                if (_views[i] != null) _views[i].Hide();
        }
    }
}
