using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// START zone trigger for the VR Tetris workflow.
///
/// Visual rules:
/// 1. Only the currently collided tetromino cube is painted red.
/// 2. Only the currently collided first-row/drop-zone grid cell is painted red.
/// 3. The cells in the same column below the collided first-row cell keep their original gray color,
///    but their alpha is set to previewAlpha.
/// 4. When the tetromino cube is no longer colliding with this trigger, its original color is restored.
/// 5. When this trigger no longer has any tetromino cube inside it, the first-row grid cell and
///    the column-preview cells are restored.
///
/// Refactor notes:
/// - Colors are applied through a MaterialPropertyBlock instead of renderer.material. Grid cells keep
///   sharing one material (GPU instancing / batching friendly), and "restoring" simply means clearing
///   the property block back to the shared material's real values - no manual save/restore bookkeeping needed.
/// - Highlight reference counting is keyed per DropZoneTrigger instance (HashSet) rather than a raw int
///   capped at 1. The original int-counter could only ever reach 1, so if the same renderer were ever
///   highlighted by two different triggers at once, one trigger exiting would incorrectly clear it for
///   the other. The HashSet approach tracks each trigger independently and is correct in that case.
/// - The column preview below the touched cell is built once per "occupied" session instead of being
///   torn down and rebuilt on every OnTriggerStay physics frame.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DropZoneTrigger : MonoBehaviour
{
    [Header("Collision Highlight")]
    public Color collidedGridCellColor = Color.red;
    public Color collidedTetrominoCubeColor = Color.red;

    [Header("Column Preview")]
    [Tooltip("Alpha value used for the cells in the same column below the touched cell. 1 = fully visible, 0 = invisible.")]
    [Range(0f, 1f)]
    public float previewAlpha = 0.2f;

    private Renderer cellRenderer;
    private GridCellIndex cellIndex;
    private TetrisBlock preparedBlock;
    private bool columnPreviewBuilt;

    private readonly HashSet<Collider> activeTetrominoColliders = new HashSet<Collider>();
    private readonly List<Renderer> previewRenderers = new List<Renderer>();

    private static readonly Dictionary<Renderer, HashSet<DropZoneTrigger>> highlightSources =
        new Dictionary<Renderer, HashSet<DropZoneTrigger>>();

    private static readonly List<DropZoneTrigger> activeTriggers = new List<DropZoneTrigger>();
    private static readonly MaterialPropertyBlock scratchBlock = new MaterialPropertyBlock();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticStateOnLoad()
    {
        highlightSources.Clear();
        activeTriggers.Clear();
    }

    private void Awake()
    {
        if (TryGetComponent(out Collider col))
        {
            col.isTrigger = true;
        }

        TryGetComponent(out cellIndex);
        TryGetComponent(out cellRenderer);
    }

    private void OnEnable()
    {
        if (!activeTriggers.Contains(this))
        {
            activeTriggers.Add(this);
        }
    }

    private void OnDisable()
    {
        activeTriggers.Remove(this);
        RestoreOwnVisuals();
    }

    private void Update()
    {
        if (preparedBlock != null && preparedBlock.HasStartedFalling)
        {
            preparedBlock = null;
            RestoreOwnVisuals();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PrepareBlock(other);
    }

    private void OnTriggerStay(Collider other)
    {
        PrepareBlock(other);
    }

    private void OnTriggerExit(Collider other)
    {
        TetrisBlock block = other.GetComponentInParent<TetrisBlock>();
        if (block == null)
        {
            return;
        }

        if (activeTetrominoColliders.Remove(other) && other.TryGetComponent(out Renderer tetrominoCubeRenderer))
        {
            RestoreRedHighlight(tetrominoCubeRenderer);
        }

        if (!block.HasStartedFalling)
        {
            block.ClearDropZoneCandidate(cellIndex, other.transform);
        }

        if (activeTetrominoColliders.Count == 0)
        {
            if (cellRenderer != null)
            {
                RestoreRedHighlight(cellRenderer);
            }

            preparedBlock = null;
            RestoreColumnPreviewOnly();
        }
    }

    private void PrepareBlock(Collider other)
    {
        TetrisBlock block = other.GetComponentInParent<TetrisBlock>();
        if (block == null || !block.CanBeStartedFromDropZone)
        {
            return;
        }

        preparedBlock = block;

        if (activeTetrominoColliders.Add(other) && other.TryGetComponent(out Renderer tetrominoCubeRenderer))
        {
            ApplyRedHighlight(tetrominoCubeRenderer, collidedTetrominoCubeColor);
        }

        if (cellRenderer != null)
        {
            ApplyRedHighlight(cellRenderer, collidedGridCellColor);
        }

        if (!columnPreviewBuilt)
        {
            ApplyPreviewToColumnBelow();
            columnPreviewBuilt = true;
        }

        block.PrepareStartFromDropZone(cellIndex, other.transform);
    }

    private void ApplyPreviewToColumnBelow()
    {
        if (cellIndex == null || GridController.Instance == null)
        {
            return;
        }

        GridController grid = GridController.Instance;

        for (int row = cellIndex.Row + 1; row < grid.rows; row++)
        {
            if (!grid.TryGetCell(cellIndex.Column, row, out GridCellIndex cell))
            {
                continue;
            }

            if (!cell.TryGetComponent(out Renderer renderer))
            {
                continue;
            }

            ApplyAlphaPreview(renderer, previewAlpha);

            if (!previewRenderers.Contains(renderer))
            {
                previewRenderers.Add(renderer);
            }
        }
    }

    private void ApplyRedHighlight(Renderer renderer, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        if (!highlightSources.TryGetValue(renderer, out HashSet<DropZoneTrigger> sources))
        {
            sources = new HashSet<DropZoneTrigger>();
            highlightSources.Add(renderer, sources);
        }

        bool wasUnhighlighted = sources.Count == 0;
        sources.Add(this);

        if (wasUnhighlighted)
        {
            ApplyOverrideColor(renderer, color);
        }
    }

    private void RestoreRedHighlight(Renderer renderer)
    {
        if (renderer == null || !highlightSources.TryGetValue(renderer, out HashSet<DropZoneTrigger> sources))
        {
            return;
        }

        sources.Remove(this);

        if (sources.Count == 0)
        {
            highlightSources.Remove(renderer);
            ClearOverride(renderer);
        }
    }

    private static void ApplyAlphaPreview(Renderer renderer, float alpha)
    {
        if (renderer == null)
        {
            return;
        }

        Color color = GetSharedColor(renderer);
        color.a = alpha;
        ApplyOverrideColor(renderer, color);
    }

    private static Color GetSharedColor(Renderer renderer)
    {
        Material material = renderer.sharedMaterial;
        if (material == null)
        {
            return Color.white;
        }

        if (material.HasProperty("_BaseColor"))
        {
            return material.GetColor("_BaseColor");
        }

        if (material.HasProperty("_Color"))
        {
            return material.GetColor("_Color");
        }

        return material.color;
    }

    private static void ApplyOverrideColor(Renderer renderer, Color color)
    {
        Material material = renderer.sharedMaterial;
        if (material == null)
        {
            return;
        }

        renderer.GetPropertyBlock(scratchBlock);

        if (material.HasProperty("_BaseColor"))
        {
            scratchBlock.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            scratchBlock.SetColor("_Color", color);
        }

        renderer.SetPropertyBlock(scratchBlock);
    }

    private static void ClearOverride(Renderer renderer)
    {
        renderer.SetPropertyBlock(null);
    }

    private static void ClearOverrideIfNotHighlighted(Renderer renderer)
    {
        if (renderer == null)
        {
            return;
        }

        if (highlightSources.TryGetValue(renderer, out HashSet<DropZoneTrigger> sources) && sources.Count > 0)
        {
            return;
        }

        ClearOverride(renderer);
    }

    private void RestoreOwnVisuals()
    {
        foreach (Collider activeCollider in activeTetrominoColliders)
        {
            if (activeCollider != null && activeCollider.TryGetComponent(out Renderer tetrominoCubeRenderer))
            {
                RestoreRedHighlight(tetrominoCubeRenderer);
            }
        }

        activeTetrominoColliders.Clear();

        if (cellRenderer != null)
        {
            RestoreRedHighlight(cellRenderer);
        }

        RestoreColumnPreviewOnly();
    }

    private void RestoreColumnPreviewOnly()
    {
        foreach (Renderer renderer in previewRenderers)
        {
            ClearOverrideIfNotHighlighted(renderer);
        }

        previewRenderers.Clear();
        columnPreviewBuilt = false;
    }

    public void ResetTrigger()
    {
        preparedBlock = null;
        RestoreOwnVisuals();
    }

    public static void ClearAllDropZoneVisuals()
    {
        for (int i = activeTriggers.Count - 1; i >= 0; i--)
        {
            DropZoneTrigger trigger = activeTriggers[i];

            if (trigger == null)
            {
                activeTriggers.RemoveAt(i);
                continue;
            }

            trigger.ResetTrigger();
        }

        foreach (Renderer renderer in highlightSources.Keys)
        {
            ClearOverride(renderer);
        }

        highlightSources.Clear();
    }
}
