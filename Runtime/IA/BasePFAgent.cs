using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(FOV3D))]
public class BasePFAgent : MonoBehaviour
{
    #region Declarations
    [Header("Values")]
    public Vector3 _velocity;
    [SerializeField] public float _maxSpeed, arriveRange, _obstacleAvoidanceRange;
    private IEnumerable<BasePFAgent> nearbyAgents;
    public float viewRange, separationRange;
    [Range(0, 1f)] [SerializeField] protected float _maxForce;
    [Range(0, 3f)] [SerializeField] protected float separationWeight, seekWeight, obstacleAvoidanceWeight;
    
    [SerializeField] protected LayerMask obstacleLayer, agentsLayer;
    
    [Header("References")]
    public FOV3D fov { get; protected set; }
    [field: SerializeField] public NodeManagerVariable _nodeManager { get; protected set; }
    public PathFindingThetaStar pf { get; protected set; }
    [SerializeField] public List<Node> _path = new();
    [SerializeField] protected FSM fsm;
    [HideInInspector] public Rigidbody rb;
    
    #endregion

    protected virtual void Awake()
    {
        fov = GetComponent<FOV3D>();
        rb = GetComponent<Rigidbody>();
        fsm = new FSM();
    }

    #region Steering
    public void Move()
    {
        _velocity.y = 0;
        rb.MovePosition((transform.position + _velocity * Time.fixedDeltaTime));
        transform.forward = _velocity;
    }

    public Vector3 Seek(Vector3 target)
    {
        return CalculateSteering((target - transform.position).normalized * _maxSpeed);
    }
    
    public Vector3 CalculateSteering(Vector3 desired)
    {
        return Vector3.ClampMagnitude(desired - _velocity, _maxForce);
    }

    public void AddForce(Vector3 force)
    {
        _velocity = Vector3.ClampMagnitude(_velocity + force, _maxSpeed);
    }

    // public Vector3 Pursuit(NewPlayer target)
    // {
    //     Vector3 futurePos = target.transform.position + target.pm.movement.isometricInput;
    //
    //     if (Vector3.Distance(transform.position, futurePos) < (target.rb.velocity.magnitude))
    //     {
    //         Debug.DrawLine(transform.position, target.transform.position, Color.green);
    //         return Seek(target.transform.position);
    //     }
    //     Debug.DrawLine(transform.position, futurePos, Color.red);
    //
    //
    //     return Seek(futurePos);
    // }

    public Vector3 Arrive(Vector3 targetPos)
    {
        float dist = Vector3.Distance(targetPos, transform.position);
        if (dist > arriveRange)
            return Seek(targetPos);

        Vector3 desired = (targetPos - transform.position).normalized;
        desired *= ((dist / arriveRange) * _maxSpeed);

        return CalculateSteering(desired);
    }
    
    public Vector3 ObstacleAvoidance()
    {
        Vector3 desired = default;
        if (Physics.Raycast(transform.position + transform.forward / 2, _velocity, _obstacleAvoidanceRange, obstacleLayer))
            desired = -transform.forward;
        else if (Physics.Raycast(transform.position - transform.forward / 2, _velocity, _obstacleAvoidanceRange, obstacleLayer))
            desired = transform.forward;
        else return desired;

        return CalculateSteering(desired.normalized * _maxSpeed);
    }

    public Vector3 Separation()
    {
        nearbyAgents = Physics.OverlapSphere(transform.position, separationRange, agentsLayer).Select(x => x.GetComponent<BasePFAgent>());
        Vector3 desired = Vector3.zero;
        foreach (BasePFAgent b in nearbyAgents)
        {
            desired += b.transform.position - transform.position;
        }
        if (desired == Vector3.zero) return desired;
        desired = -desired.normalized * _maxSpeed;
        return CalculateSteering(desired);
    }
    #endregion
    #region PathFinding

    public virtual void TravelPath()
    {
        AddForce(Seek(_path[0].transform.position));
        AddForce(Separation());
        AddForce(ObstacleAvoidance());
        if (Vector3.Distance(_path[0].transform.position, transform.position) <= arriveRange)
        {
            _path.RemoveAt(0);
        }
    }
    
    
    #endregion
}
