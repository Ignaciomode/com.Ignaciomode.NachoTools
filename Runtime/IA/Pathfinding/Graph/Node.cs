using System.Collections.Generic;
using UnityEngine;

public class Node : MonoBehaviour
{
    public List<Node> neighbours;
    public int cost;
    public NodeManager manager;
    public bool blocked = true;

    [SerializeField] LayerMask _wallLayer;
    [SerializeField] [Range(0, 50)] float _viewRange;
    
    public LayerMask WallLayer => _wallLayer;
    public float ViewRange => _viewRange;

}