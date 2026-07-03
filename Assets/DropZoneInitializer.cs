using UnityEngine;

public class DropZoneInitializer : MonoBehaviour
{
    [Header("Drop-Zone Collider Size")]
    [Tooltip(
        "Size of the trigger box relative to cellSize. " +
        "0.9 means 90% of cell size - leaves a small gap so adjacent cells don't double-fire."
    )]
    [Range(0.5f, 1.0f)]
    public float triggerFillRatio = 0.9f;

    private GridController gridController;

    private void Start()
    {
        gridController = GridController.Instance;

        if (gridController == null)
        {
            Debug.LogError("[DropZoneInitializer] GridController.Instance not found.");
            return;
        }

        if (gridController.IsGridReady)
        {
            SetupDropZone();
        }
        else
        {
            gridController.GridReady += SetupDropZone;
        }
    }

    private void OnDestroy()
    {
        if (gridController != null)
        {
            gridController.GridReady -= SetupDropZone;
        }
    }

    private void SetupDropZone()
    {
        GridCellIndex[] allCells = gridController.GetComponentsInChildren<GridCellIndex>();

        int count = 0;

        foreach (GridCellIndex cell in allCells)
        {
            if (cell.Row != 0)
            {
                continue;
            }

            GameObject obj = cell.gameObject;

            if (!obj.TryGetComponent(out BoxCollider boxCollider))
            {
                boxCollider = obj.AddComponent<BoxCollider>();
            }

            boxCollider.isTrigger = true;
            boxCollider.size = Vector3.one * triggerFillRatio;
            boxCollider.center = Vector3.zero;

            if (!obj.TryGetComponent(out DropZoneTrigger _))
            {
                obj.AddComponent<DropZoneTrigger>();
            }

            count++;
        }

        Debug.Log($"[DropZoneInitializer] Configured {count} drop-zone trigger cells (row 0).");
    }
}
