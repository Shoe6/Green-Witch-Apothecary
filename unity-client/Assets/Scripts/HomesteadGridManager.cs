using System.Collections.Generic;
using UnityEngine;

namespace GreenWitchApothecary.World
{
    public enum MicroClimateLight
    {
        FullSun,
        PartialShade,
        FullShade
    }

    public enum SoilPhLevel
    {
        Acidic,
        Neutral,
        Alkaline
    }

    /// <summary>
    /// Generates and manages the 3x3 open-concept homestead grid with micro-climate and soil pH metadata.
    /// </summary>
    public class HomesteadGridManager : MonoBehaviour
    {
        [Header("Grid Layout")]
        [SerializeField] private int gridSize = 3;
        [SerializeField] private float tileSize = 4f;
        [SerializeField] private float tileSpacing = 0.35f;
        [SerializeField] private Material soilPlaceholderMaterial;

        public readonly Dictionary<Vector2Int, HomesteadPlot> Plots = new Dictionary<Vector2Int, HomesteadPlot>();

        private void Start()
        {
            GenerateGrid();
        }

        public void GenerateGrid()
        {
            Plots.Clear();
            float step = tileSize + tileSpacing;
            float offset = (gridSize - 1) * step * 0.5f;

            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    var coords = new Vector2Int(x, y);
                    Vector3 worldPos = new Vector3(x * step - offset, 0f, y * step - offset);

                    GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"HomesteadPlot_{x}_{y}";
                    tile.transform.SetParent(transform, false);
                    tile.transform.localPosition = worldPos;
                    tile.transform.localScale = new Vector3(tileSize, 0.25f, tileSize);

                    if (soilPlaceholderMaterial != null)
                    {
                        var renderer = tile.GetComponent<Renderer>();
                        renderer.sharedMaterial = soilPlaceholderMaterial;
                    }

                    var plot = tile.AddComponent<HomesteadPlot>();
                    plot.Initialize(
                        coords,
                        DetermineInitialLight(x, y),
                        SoilPhLevel.Neutral,
                        requiresClearing: (x == 2 || y == 2)
                    );
                    Plots[coords] = plot;
                }
            }
        }

        private static MicroClimateLight DetermineInitialLight(int x, int y)
        {
            if (x == 0 && y == 0) return MicroClimateLight.FullShade;
            if (x == 2 || y == 2) return MicroClimateLight.PartialShade;
            return MicroClimateLight.FullSun;
        }
    }

    public class HomesteadPlot : MonoBehaviour
    {
        public Vector2Int Coordinates { get; private set; }
        public MicroClimateLight LightLevel { get; private set; }
        public SoilPhLevel SoilPh { get; private set; }
        public bool IsCleared { get; private set; }
        public string PlantedItemId { get; private set; }

        public void Initialize(Vector2Int coords, MicroClimateLight light, SoilPhLevel ph, bool requiresClearing)
        {
            Coordinates = coords;
            LightLevel = light;
            SoilPh = ph;
            IsCleared = !requiresClearing;
        }

        public void AmendSoilPh(SoilPhLevel targetPh)
        {
            SoilPh = targetPh;
        }
    }
}
