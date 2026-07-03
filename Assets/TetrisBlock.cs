using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class TetrisBlock : MonoBehaviour
{
    public Vector3 rotationPoint = Vector3.zero;

    [Header("Fall Settings")]
    public float fallTime = 0.2f;

    [Header("Drop Zone Start Settings")]
    [Tooltip("The piece starts falling only after it is placed in the drop zone and is no longer grabbed.")]
    [SerializeField] private float startStillTime = 0.15f;

    [Header("Input")]
    [SerializeField] private KeyCode moveLeftKey = KeyCode.LeftArrow;
    [SerializeField] private KeyCode moveRightKey = KeyCode.RightArrow;
    [SerializeField] private KeyCode rotateKey = KeyCode.UpArrow;
    [SerializeField] private KeyCode softDropKey = KeyCode.DownArrow;

    private const float PositionStillnessThreshold = 0.0005f;
    private const float RotationStillnessThresholdDegrees = 0.5f;
    private const float SoftDropFallTimeDivisor = 10f;

    private static readonly string[] GrabStateMemberNames = { "isGrabbed", "IsGrabbed", "Selected", "IsSelected" };

    private enum PieceState
    {
        WaitingForDropZone,
        Falling,
        Locked
    }

    private static Transform[,] grid;

    private GridController gridController;
    private SpawnTetramino spawner;

    private int width;
    private int height;
    private float cellSize;
    private float cachedCellSize;
    private float cachedCubeFill;

    private float previousTime;

    private PieceState state = PieceState.WaitingForDropZone;

    private bool hasDropZoneCandidate;
    private GridCellIndex candidateCell;
    private Transform candidateTouchedCube;

    private Vector3 lastWaitingPosition;
    private Quaternion lastWaitingRotation;
    private float stillSince;

    private readonly List<Transform> blockCubes = new List<Transform>();

    private readonly List<Behaviour> grabRelatedBehaviours = new List<Behaviour>();
    private readonly List<GrabStateAccessor> grabStateAccessors = new List<GrabStateAccessor>();
    private bool grabComponentsCached;
    private bool grabComponentsDisabled;

    public bool HasStartedFalling => state == PieceState.Falling || state == PieceState.Locked;

    public bool CanBeStartedFromDropZone => state == PieceState.WaitingForDropZone;

    private readonly struct GrabStateAccessor
    {
        private readonly Component component;
        private readonly FieldInfo field;
        private readonly PropertyInfo property;

        public GrabStateAccessor(Component component, FieldInfo field)
        {
            this.component = component;
            this.field = field;
            property = null;
        }

        public GrabStateAccessor(Component component, PropertyInfo property)
        {
            this.component = component;
            this.property = property;
            field = null;
        }

        public bool GetValue()
        {
            if (component == null)
            {
                return false;
            }

            if (field != null)
            {
                return (bool)field.GetValue(component);
            }

            if (property != null)
            {
                return (bool)property.GetValue(component, null);
            }

            return false;
        }
    }

    private void Start()
    {
        if (gridController == null)
        {
            gridController = GridController.Instance;
        }

        RefreshCachedValues();
        EnsureGridArray();
        EnsureChildColliders();
        RefreshBlockCubesCache();
        CacheGrabRelatedComponents();
        ApplyCurrentGridScale();

        lastWaitingPosition = transform.position;
        lastWaitingRotation = transform.rotation;
        stillSince = Time.time;
        previousTime = Time.time;
    }

    private void Update()
    {
        if (gridController != null &&
            (!Mathf.Approximately(cachedCellSize, gridController.cellSize) ||
             !Mathf.Approximately(cachedCubeFill, gridController.cubeFill)))
        {
            RefreshCachedValues();
        }

        ApplyCurrentGridScale();

        if (state == PieceState.WaitingForDropZone)
        {
            HardStopRigidbody();
            UpdateWaitingForDropZone();
            return;
        }

        if (state != PieceState.Falling)
        {
            return;
        }

        HardStopRigidbody();

        HandlePlayerInput();
        HandleAutomaticFall();
    }

    public void InitializeAsWaitingPiece(SpawnTetramino ownerSpawner)
    {
        spawner = ownerSpawner;
        state = PieceState.WaitingForDropZone;
        hasDropZoneCandidate = false;
        candidateCell = null;
        candidateTouchedCube = null;

        if (gridController == null)
        {
            gridController = GridController.Instance;
        }

        RefreshCachedValues();
        EnsureGridArray();
        EnsureChildColliders();
        RefreshBlockCubesCache();
        CacheGrabRelatedComponents();
        ApplyCurrentGridScale();

        previousTime = Time.time;
        lastWaitingPosition = transform.position;
        lastWaitingRotation = transform.rotation;
        stillSince = Time.time;

        SetPhysicsForWaiting();
        enabled = true;
    }

    public void PrepareStartFromDropZone(GridCellIndex cell, Transform touchedCube)
    {
        if (state != PieceState.WaitingForDropZone)
        {
            return;
        }

        candidateCell = cell;
        candidateTouchedCube = touchedCube;
        hasDropZoneCandidate = candidateCell != null && candidateTouchedCube != null;
    }

    public void ClearDropZoneCandidate()
    {
        if (state != PieceState.WaitingForDropZone)
        {
            return;
        }

        hasDropZoneCandidate = false;
        candidateCell = null;
        candidateTouchedCube = null;
        stillSince = Time.time;
        lastWaitingPosition = transform.position;
        lastWaitingRotation = transform.rotation;
    }

    public void ClearDropZoneCandidate(GridCellIndex cell, Transform touchedCube)
    {
        if (state != PieceState.WaitingForDropZone)
        {
            return;
        }

        if (candidateCell == cell && candidateTouchedCube == touchedCube)
        {
            ClearDropZoneCandidate();
        }
    }

    private void UpdateWaitingForDropZone()
    {
        if (!hasDropZoneCandidate)
        {
            lastWaitingPosition = transform.position;
            lastWaitingRotation = transform.rotation;
            stillSince = Time.time;
            return;
        }

        bool moved =
            Vector3.Distance(transform.position, lastWaitingPosition) > PositionStillnessThreshold ||
            Quaternion.Angle(transform.rotation, lastWaitingRotation) > RotationStillnessThresholdDegrees;

        if (moved)
        {
            lastWaitingPosition = transform.position;
            lastWaitingRotation = transform.rotation;
            stillSince = Time.time;
            return;
        }

        if (IsCurrentlyGrabbed())
        {
            stillSince = Time.time;
            return;
        }

        if (Time.time - stillSince >= startStillTime)
        {
            StartFallingFromDropZone();
        }
    }

    private void StartFallingFromDropZone()
    {
        if (state != PieceState.WaitingForDropZone || !hasDropZoneCandidate)
        {
            return;
        }

        if (gridController == null || candidateCell == null || candidateTouchedCube == null)
        {
            return;
        }

        SnapToNearest90();

        Vector3 targetCellPosition = gridController.GetWorldPosition(candidateCell.Column, 0);
        Vector3 delta = targetCellPosition - candidateTouchedCube.position;
        transform.position += delta;

        Vector3 p = transform.position;
        p.z += (gridController.transform.position.z + gridController.startZ) - candidateTouchedCube.position.z;
        transform.position = p;

        ClampHorizontallyInsideGrid();

        SetPhysicsForFalling();
        DisableGrabComponentsOnce();

        state = PieceState.Falling;
        hasDropZoneCandidate = false;
        candidateCell = null;
        candidateTouchedCube = null;

        previousTime = Time.time;
        MoveDownOrLock();

        Debug.Log("Tetromino started falling from drop zone.");
    }

    private void HandlePlayerInput()
    {
        if (Input.GetKeyDown(moveLeftKey))
        {
            Move(new Vector3(-cellSize, 0f, 0f));
        }
        else if (Input.GetKeyDown(moveRightKey))
        {
            Move(new Vector3(cellSize, 0f, 0f));
        }
        else if (Input.GetKeyDown(rotateKey))
        {
            Rotate(90f);
        }
    }

    private void HandleAutomaticFall()
    {
        float currentFallTime = Input.GetKey(softDropKey) ? fallTime / SoftDropFallTimeDivisor : fallTime;

        if (Time.time - previousTime <= currentFallTime)
        {
            return;
        }

        MoveDownOrLock();
        previousTime = Time.time;
    }

    private void Move(Vector3 movement)
    {
        transform.position += movement;

        if (!ValidMove())
        {
            transform.position -= movement;
        }
    }

    private void Rotate(float angle)
    {
        Vector3 pivot = transform.TransformPoint(rotationPoint);

        transform.RotateAround(pivot, Vector3.forward, angle);

        if (!ValidMove())
        {
            transform.RotateAround(pivot, Vector3.forward, -angle);
        }
    }

    private void MoveDownOrLock()
    {
        Vector3 down = new Vector3(0f, -cellSize, 0f);
        transform.position += down;

        if (!ValidMove())
        {
            transform.position -= down;
            LockPiece();
        }
    }

    private void LockPiece()
    {
        if (state == PieceState.Locked)
        {
            return;
        }

        state = PieceState.Locked;
        SnapToNearest90();
        SetPhysicsForLocked();

        if (AddToGrid())
        {
            CheckForLines();
        }

        enabled = false;

        SpawnTetramino activeSpawner = spawner != null ? spawner : FindObjectOfType<SpawnTetramino>();

        if (activeSpawner != null)
        {
            activeSpawner.SpawnNextAfterLockedPiece(this);
        }
        else
        {
            Debug.LogError("SpawnTetramino was not found in the scene.");
        }
    }

    private int WorldXToGridX(float worldX) => gridController.WorldXToColumn(worldX);

    private int WorldYToGridY(float worldY) => gridController.WorldYToRowFromBottom(worldY);

    private bool IsInsideGrid(int x, int y) => gridController.IsInsideGrid(x, y);

    private bool ValidMove()
    {
        EnsureGridArray();

        foreach (Transform cube in blockCubes)
        {
            int x = WorldXToGridX(cube.position.x);
            int y = WorldYToGridY(cube.position.y);

            if (x < 0 || x >= width)
            {
                return false;
            }

            if (y < 0)
            {
                return false;
            }

            if (y >= height)
            {
                continue;
            }

            if (grid[x, y] != null)
            {
                return false;
            }
        }

        return true;
    }

    private bool AddToGrid()
    {
        EnsureGridArray();

        bool addedAtLeastOneCube = false;

        foreach (Transform cube in blockCubes)
        {
            int x = WorldXToGridX(cube.position.x);
            int y = WorldYToGridY(cube.position.y);

            if (y >= height)
            {
                continue;
            }

            if (!IsInsideGrid(x, y))
            {
                Debug.LogError($"Cannot add block to grid. Index is outside grid: [{x}, {y}] from position {cube.position}");
                continue;
            }

            grid[x, y] = cube;
            addedAtLeastOneCube = true;
        }

        return addedAtLeastOneCube;
    }

    private void CheckForLines()
    {
        for (int y = 0; y < height; y++)
        {
            if (HasLine(y))
            {
                DeleteLine(y);
                RowDown(y);
                y--;
            }
        }
    }

    private bool HasLine(int y)
    {
        for (int x = 0; x < width; x++)
        {
            if (grid[x, y] == null)
            {
                return false;
            }
        }

        return true;
    }

    private void DeleteLine(int y)
    {
        for (int x = 0; x < width; x++)
        {
            if (grid[x, y] != null)
            {
                Destroy(grid[x, y].gameObject);
                grid[x, y] = null;
            }
        }
    }

    private void RowDown(int deletedRow)
    {
        for (int y = deletedRow + 1; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (grid[x, y] != null)
                {
                    grid[x, y - 1] = grid[x, y];
                    grid[x, y] = null;
                    grid[x, y - 1].position += new Vector3(0f, -cellSize, 0f);
                }
            }
        }
    }

    private void RefreshCachedValues()
    {
        if (gridController == null)
        {
            return;
        }

        width = gridController.columns;
        height = gridController.rows;
        cellSize = gridController.cellSize;
        cachedCellSize = cellSize;
        cachedCubeFill = gridController.cubeFill;

        EnsureGridArray();
    }

    private void ApplyCurrentGridScale()
    {
        if (gridController == null)
        {
            return;
        }

        Vector3 desiredRootScale = Vector3.one * gridController.cellSize;
        if (transform.localScale != desiredRootScale)
        {
            transform.localScale = desiredRootScale;
        }

        Vector3 desiredChildScale = Vector3.one * gridController.cubeFill;

        foreach (Transform cube in blockCubes)
        {
            if (cube.localScale != desiredChildScale)
            {
                cube.localScale = desiredChildScale;
            }
        }
    }

    private void EnsureGridArray()
    {
        if (gridController == null)
        {
            return;
        }

        if (grid == null || grid.GetLength(0) != gridController.columns || grid.GetLength(1) != gridController.rows)
        {
            grid = new Transform[gridController.columns, gridController.rows];
        }
    }

    private void SnapToNearest90()
    {
        Vector3 euler = transform.eulerAngles;
        euler.x = 0f;
        euler.y = 0f;
        euler.z = Mathf.Round(euler.z / 90f) * 90f;
        transform.eulerAngles = euler;
    }

    private void ClampHorizontallyInsideGrid()
    {
        if (!TryGetHorizontalBounds(out int minX, out int maxX))
        {
            return;
        }

        int shiftColumns = 0;

        if (minX < 0)
        {
            shiftColumns = -minX;
        }
        else if (maxX >= width)
        {
            shiftColumns = (width - 1) - maxX;
        }

        if (shiftColumns != 0)
        {
            transform.position += new Vector3(shiftColumns * cellSize, 0f, 0f);
        }
    }

    private bool TryGetHorizontalBounds(out int minX, out int maxX)
    {
        minX = int.MaxValue;
        maxX = int.MinValue;

        bool found = false;

        foreach (Transform cube in blockCubes)
        {
            int x = WorldXToGridX(cube.position.x);
            minX = Mathf.Min(minX, x);
            maxX = Mathf.Max(maxX, x);
            found = true;
        }

        return found;
    }

    private void RefreshBlockCubesCache()
    {
        blockCubes.Clear();

        foreach (Transform child in transform)
        {
            if (child.GetComponent<Renderer>() != null || child.GetComponent<Collider>() != null)
            {
                blockCubes.Add(child);
            }
        }
    }

    private void EnsureChildColliders()
    {
        foreach (Transform child in transform)
        {
            if (child.GetComponent<Renderer>() == null || child.GetComponent<Collider>() != null)
            {
                continue;
            }

            BoxCollider boxCollider = child.gameObject.AddComponent<BoxCollider>();
            boxCollider.size = Vector3.one;
            boxCollider.center = Vector3.zero;
            boxCollider.isTrigger = false;
        }
    }

    private void SetPhysicsForWaiting() => SetRigidbodyKinematic();

    private void SetPhysicsForFalling() => SetRigidbodyKinematic();

    private void SetPhysicsForLocked() => SetRigidbodyKinematic();

    private void HardStopRigidbody() => SetRigidbodyKinematic();

    private void SetRigidbodyKinematic()
    {
        if (TryGetComponent(out Rigidbody rb))
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }
    }

    private void CacheGrabRelatedComponents()
    {
        if (grabComponentsCached)
        {
            return;
        }

        grabComponentsCached = true;
        grabRelatedBehaviours.Clear();
        grabStateAccessors.Clear();

        Component[] components = GetComponentsInChildren<Component>(true);

        foreach (Component component in components)
        {
            if (component == null || component == this)
            {
                continue;
            }

            System.Type type = component.GetType();
            string typeName = type.Name;

            bool isGrabOrInteractable =
                typeName.IndexOf("Grab", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Interactable", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (isGrabOrInteractable && component is Behaviour behaviour)
            {
                grabRelatedBehaviours.Add(behaviour);
            }

            foreach (string memberName in GrabStateMemberNames)
            {
                FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(bool))
                {
                    grabStateAccessors.Add(new GrabStateAccessor(component, field));
                    continue;
                }

                PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0)
                {
                    grabStateAccessors.Add(new GrabStateAccessor(component, property));
                }
            }
        }
    }

    private bool IsCurrentlyGrabbed()
    {
        foreach (GrabStateAccessor accessor in grabStateAccessors)
        {
            if (accessor.GetValue())
            {
                return true;
            }
        }

        return false;
    }

    private void DisableGrabComponentsOnce()
    {
        if (grabComponentsDisabled)
        {
            return;
        }

        grabComponentsDisabled = true;

        foreach (Behaviour behaviour in grabRelatedBehaviours)
        {
            if (behaviour != null)
            {
                behaviour.enabled = false;
            }
        }
    }
}
