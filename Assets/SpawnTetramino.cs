using UnityEngine;

public class SpawnTetramino : MonoBehaviour
{
    [Header("Tetramino Prefabs")]
    public GameObject[] Tetraminoes;

    [Header("Spawn Settings")]
    [Tooltip("The spawner is intentionally OUTSIDE the grid. New tetrominoes appear at this GameObject position.")]
    [SerializeField] private bool spawnFirstTetrominoOnStart = true;

    private GridController gridController;
    private TetrisBlock currentTetromino;

    private void Start()
    {
        gridController = GridController.Instance;

        if (gridController == null)
        {
            Debug.LogError("GridController.Instance was not found. Make sure GridController exists in the scene.");
            return;
        }

        if (spawnFirstTetrominoOnStart)
        {
            NewTetramino();
        }
    }

    public void NewTetramino()
    {
        if (Tetraminoes == null || Tetraminoes.Length == 0)
        {
            Debug.LogError("No Tetramino prefabs assigned in SpawnTetramino.");
            return;
        }

        DropZoneTrigger.ClearAllDropZoneVisuals();

        GameObject newTetromino = Instantiate(
            Tetraminoes[Random.Range(0, Tetraminoes.Length)],
            transform.position,
            transform.rotation
        );

        ScaleTetramino(newTetromino);

        if (!newTetromino.TryGetComponent(out currentTetromino))
        {
            currentTetromino = newTetromino.AddComponent<TetrisBlock>();
        }

        currentTetromino.InitializeAsWaitingPiece(this);
    }

    public void SpawnNextAfterLockedPiece(TetrisBlock lockedPiece)
    {
        if (currentTetromino == lockedPiece)
        {
            currentTetromino = null;
        }

        NewTetramino();
    }

    private void ScaleTetramino(GameObject tetromino)
    {
        if (gridController == null)
        {
            return;
        }

        tetromino.transform.localScale = Vector3.one * gridController.cellSize;

        foreach (Transform child in tetromino.transform)
        {
            child.localScale = Vector3.one * gridController.cubeFill;
        }
    }
}
