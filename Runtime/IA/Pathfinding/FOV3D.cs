using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FOV3D : MonoBehaviour
{


    [SerializeField] LayerMask _wallLayer;
    public LayerMask WallLayer => _wallLayer;
    [SerializeField] [Range(0, 360)] float _viewAngle;
    [SerializeField] [Range(0, 100)] float _viewRange;




    public bool InFOV(Vector3 desiredPos)
    {
        Vector3 dir = desiredPos - transform.position;
        if (dir.magnitude > _viewRange) return false;
        if (Vector3.Angle(transform.forward, dir) > _viewAngle / 2) return false;
        if (!InLOS(transform.position, desiredPos)) return false;
        return true;
    }


    public bool InLOS(Vector3 start, Vector3 desiredPos)
    {
        Vector3 dir = desiredPos - start;
        return !Physics.Raycast(start, dir, dir.magnitude, _wallLayer);
    }



    void OnDrawGizmos()
    {
        //Gizmos.color = Color.white;
        //Gizmos.DrawWireSphere(transform.position, _viewRange);
    
        //Vector3 dirA = GetAngleFromDir(_viewAngle / 2 + transform.eulerAngles.y);
        //Vector3 dirB = GetAngleFromDir(-_viewAngle / 2 + transform.eulerAngles.y);
        //Gizmos.DrawLine(transform.position, transform.position + dirA.normalized * _viewRange);
        //Gizmos.DrawLine(transform.position, transform.position + dirB.normalized * _viewRange);
    }

    Vector3 GetAngleFromDir(float angleInDegrees)
    {
        return new Vector3(Mathf.Sin(angleInDegrees * Mathf.Deg2Rad), 0, Mathf.Cos(Mathf.Deg2Rad * angleInDegrees));
    }

}
