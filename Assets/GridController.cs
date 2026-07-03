using System;
using UnityEngine;

public class GridController : MonoBehaviour
{
    public static GridController Instance { get; private set; }

    public bool IsGridReady { get; private set; }

    public event Action GridReady;

    [Header("Prefab")]
    [SerializeField] private GameObject prefab;

    [Header("Matrix Size")]
    public int columns = 10;
    public int rows = 20;

    [Header("Cell Logic Size")]
    public float cellSize = 0.07f;

    [Header("Cube Visual Size")]
    [Range(0.1f, 1f)]
    public float cubeFill = 0.9f;

    [Header("Start Position")]
    public float startX = 0f;
    public float startY = 2.2f;
    public float startZ = 0f;

    [Header("Materials")]
    [SerializeField] private Material blackMaterial;

    [Header("Background Plane")]
    [SerializeField] private bool createBackgroundPlane = true;

    [SerializeField] private float backgroundExtraOffset = 0.01f;

    [SerializeField] private bool invertBackgroundDirection = false;

    private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
    private const string StandardShaderName = "Standard";

    private GameObject backgroundPlane;
    private Material runtimeBlackBackgroundMaterial;

    private GridCellIndex[,] cellLookup;

    public Vector3 GridOriginWorld =>
        new Vector3(
            transform.position.x + startX,
            transform.position.y + startY,
            transform.position.z + startZ
        );

    public float BottomY => GridOriginWorld.y - (rows - 1) * cellSize;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GridController] Another instance already exists. Destroying the duplicate.");
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        DrawMatrix();

        if (createBackgroundPlane)
        {
            CreateOrUpdateBackgroundPlane();
        }

        IsGridReady = true;
        GridReady?.Invoke();
    }

    private void DrawMatrix()
    {
        cellLookup = new GridCellIndex[columns, rows];

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                Vector3 position = GetWorldPosition(column, row);

                GameObject obj = Instantiate(prefab, position, Quaternion.identity, transform);

                float visualSize = cellSize * cubeFill;
                obj.transform.localScale = new Vector3(visualSize, visualSize, visualSize);
                obj.name = $"Cell [{row}, {column}]";

                if (!obj.TryGetComponent(out GridCellIndex index))
                {
                    index = obj.AddComponent<GridCellIndex>();
                }

                index.Initialize(column, row);
                cellLookup[column, row] = index;

                if (row == 0 && blackMaterial != null && obj.TryGetComponent(out Renderer cellRenderer))
                {
                    cellRenderer.material = blackMaterial;
                }
            }
        }
    }

    public bool TryGetCell(int column, int row, out GridCellIndex cell)
    {
        cell = null;

        if (cellLookup == null || column < 0 || column >= columns || row < 0 || row >= rows)
        {
            return false;
        }

        cell = cellLookup[column, row];
        return cell != null;
    }

    private void CreateOrUpdateBackgroundPlane()
    {
        if (backgroundPlane == null)
        {
            backgroundPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backgroundPlane.name = "Grid Background Plane";
            backgroundPlane.transform.SetParent(transform);

            if (backgroundPlane.TryGetComponent(out Collider planeCollider))
            {
                Destroy(planeCollider);
            }

            if (backgroundPlane.TryGetComponent(out Renderer planeRenderer))
            {
                Shader shader = Shader.Find(UrpLitShaderName);

                if (shader == null)
                {
                    shader = Shader.Find(StandardShaderName);
                }

                runtimeBlackBackgroundMaterial = new Material(shader)
                {
                    name = "Runtime Grid Black Background",
                    color = Color.black
                };

                if (runtimeBlackBackgroundMaterial.HasProperty("_BaseColor"))
                {
                    runtimeBlackBackgroundMaterial.SetColor("_BaseColor", Color.black);
                }

                if (runtimeBlackBackgroundMaterial.HasProperty("_Color"))
                {
                    runtimeBlackBackgroundMaterial.SetColor("_Color", Color.black);
                }

                planeRenderer.material = runtimeBlackBackgroundMaterial;
            }
        }

        float width = columns * cellSize;
        float height = rows * cellSize;

        float cubeDepth = cellSize * cubeFill;
        float zOffset = (cubeDepth / 2f) + backgroundExtraOffset;

        if (invertBackgroundDirection)
        {
            zOffset *= -1f;
        }

        Vector3 center = new Vector3(
            GridOriginWorld.x + ((columns - 1) * cellSize) / 2f,
            GridOriginWorld.y - ((rows - 1) * cellSize) / 2f,
            GridOriginWorld.z + zOffset
        );

        backgroundPlane.transform.position = center;
        backgroundPlane.transform.rotation = Quaternion.identity;

        backgroundPlane.transform.localScale = new Vector3(width, height, 1f);
    }

    public void SetCellSize(float newCellSize)
    {
        if (newCellSize <= 0f)
        {
            Debug.LogWarning("Cell size must be greater than 0.");
            return;
        }

        float oldCellSize = cellSize;
        if (Mathf.Approximately(oldCellSize, newCellSize))
        {
            return;
        }

        Vector3 oldOriginWorld = GridOriginWorld;
        float oldBottomY = BottomY;

        cellSize = newCellSize;

        UpdateVisualGridCells();
        ResizeTetraminoBlocks(oldCellSize, oldOriginWorld, oldBottomY);

        if (createBackgroundPlane)
        {
            CreateOrUpdateBackgroundPlane();
        }
    }

    public void SetCubeFill(float newCubeFill)
    {
        cubeFill = Mathf.Clamp(newCubeFill, 0.1f, 1f);

        UpdateVisualGridCells();
        UpdateTetraminoVisualSize();

        if (createBackgroundPlane)
        {
            CreateOrUpdateBackgroundPlane();
        }
    }

    private void UpdateVisualGridCells()
    {
        if (cellLookup == null)
        {
            return;
        }

        float visualSize = cellSize * cubeFill;

        foreach (GridCellIndex cell in cellLookup)
        {
            if (cell == null)
            {
                continue;
            }

            Transform t = cell.transform;
            t.position = GetWorldPosition(cell.Column, cell.Row);
            t.localScale = new Vector3(visualSize, visualSize, visualSize);
        }
    }

    private void ResizeTetraminoBlocks(float oldCellSize, Vector3 oldOriginWorld, float oldBottomY)
    {
        TetrisBlock[] blocks = FindObjectsOfType<TetrisBlock>();
        float newBottomY = BottomY;

        foreach (TetrisBlock block in blocks)
        {
            if (block == null || block.transform.childCount == 0)
            {
                continue;
            }

            Transform blockTransform = block.transform;
            Transform referenceChild = blockTransform.GetChild(0);
            Vector3 referencePosition = referenceChild.position;

            int column = Mathf.RoundToInt((referencePosition.x - oldOriginWorld.x) / oldCellSize);
            int rowFromBottom = Mathf.RoundToInt((referencePosition.y - oldBottomY) / oldCellSize);

            blockTransform.localScale = Vector3.one * cellSize;

            float newX = GridOriginWorld.x + column * cellSize;
            float newY = newBottomY + rowFromBottom * cellSize;

            Vector3 desiredReferencePosition = new Vector3(newX, newY, GridOriginWorld.z);
            Vector3 delta = desiredReferencePosition - referenceChild.position;
            blockTransform.position += delta;

            foreach (Transform child in blockTransform)
            {
                if (child != null)
                {
                    child.localScale = Vector3.one * cubeFill;
                }
            }
        }
    }

    private void UpdateTetraminoVisualSize()
    {
        TetrisBlock[] blocks = FindObjectsOfType<TetrisBlock>();

        foreach (TetrisBlock block in blocks)
        {
            if (block == null)
            {
                continue;
            }

            foreach (Transform child in block.transform)
            {
                if (child != null)
                {
                    child.localScale = Vector3.one * cubeFill;
                }
            }
        }
    }

    public Vector3 GetWorldPosition(int column, int row)
    {
        return new Vector3(
            GridOriginWorld.x + column * cellSize,
            GridOriginWorld.y - row * cellSize,
            GridOriginWorld.z
        );
    }

    public int WorldXToColumn(float worldX) => Mathf.RoundToInt((worldX - GridOriginWorld.x) / cellSize);

    public int WorldYToRowFromBottom(float worldY) => Mathf.RoundToInt((worldY - BottomY) / cellSize);

    public bool IsInsideGrid(int column, int rowFromBottom)
    {
        return column >= 0 &&
               column < columns &&
               rowFromBottom >= 0 &&
               rowFromBottom < rows;
    }
}
