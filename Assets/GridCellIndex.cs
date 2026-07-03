using UnityEngine;

public class GridCellIndex : MonoBehaviour
{
    public int Column { get; private set; }
    public int Row { get; private set; }

    public void Initialize(int column, int row)
    {
        Column = column;
        Row = row;
    }
}
