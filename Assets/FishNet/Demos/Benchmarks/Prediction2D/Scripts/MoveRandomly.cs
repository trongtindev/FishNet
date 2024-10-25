using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using FishNet.Utility.Template;
using GameKit.Dependencies.Utilities;
using UnityEngine;

namespace FishNet.Demo.Benchmarks.Prediction2D
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class MoveRandomly : TickNetworkBehaviour
    {
        #region Types
        public struct ReplicateData : IReplicateData
        {
            public float Horizontal;
            public float Vertical;

            public ReplicateData(float horizontal, float vertical) : this()
            {
                Horizontal = horizontal;
                Vertical = vertical;
            }

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }

        public struct ReconcileData : IReconcileData
        {
            //PredictionRigidbody is used to synchronize rigidbody states
            //and forces. This could be done manually but the PredictionRigidbody
            //type makes this process considerably easier. Velocities, kinematic state,
            //transform properties, pending velocities and more are automatically
            //handled with PredictionRigidbody.
            public PredictionRigidbody2D PredictionRigidbody2D;

            public ReconcileData(PredictionRigidbody2D pr) : this()
            {
                PredictionRigidbody2D = pr;
            }

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }
        #endregion

        #region Fields
        [SerializeField] private float _moveRate = 1f;
        [SerializeField] private float _moveInterval = 1f;
        [SerializeField] private float _moveDistance = 3f;
        [SerializeField] private Vector2 _moveDirection;

        private PredictionRigidbody2D _predictionRigidbody2D;
        private float _nextMove;
        #endregion

        #region Unity
        private void Awake()
        {
            _predictionRigidbody2D = ObjectCaches<PredictionRigidbody2D>.Retrieve();
            _predictionRigidbody2D.Initialize(GetComponent<Rigidbody2D>());
        }

        private void OnDestroy()
        {
            ObjectCaches<PredictionRigidbody2D>.StoreAndDefault(ref _predictionRigidbody2D);
        }

        private void Update()
        {
            if (_nextMove > 0)
            {
                _nextMove -= Time.deltaTime;
                return;
            }
            _nextMove = _moveInterval;

            if (Vector2.Distance(transform.position, Vector2.zero) > _moveDistance)
            {
                _moveDirection = (Vector3.zero - transform.position).normalized;
            }
            else
            {
                _moveDirection = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
            }
        }
        #endregion

        #region Methods
        private ReplicateData CreateReplicateData()
        {
            // if without benchmark
            //if (!base.IsOwner) return default;
            if (!IsServerInitialized) return default;

            //float horizontal = Input.GetAxisRaw("Horizontal");
            //float vertical = Input.GetAxisRaw("Vertical");

            var md = new ReplicateData(_moveDirection.x, _moveDirection.y);

            return md;
        }

        [Replicate]
        private void RunInputs(ReplicateData data, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            /* ReplicateState is set based on if the data is new, being replayed, ect.
            * Visit the ReplicationState enum for more information on what each value
            * indicates. At the end of this guide a more advanced use of state will
            * be demonstrated. */

            //Be sure to always apply and set velocties using PredictionRigidbody
            //and never on the rigidbody itself; this includes if also accessing from
            //another script.
            Vector3 forces = new Vector3(data.Horizontal, data.Vertical) * _moveRate;
            _predictionRigidbody2D.Velocity(forces);

            //Simulate the added forces.
            //Typically you call this at the end of your replicate. Calling
            //Simulate is ultimately telling the PredictionRigidbody to iterate
            //the forces we added above.
            _predictionRigidbody2D.Simulate();
        }

        public override void CreateReconcile()
        {
            //We must send back the state of the rigidbody. Using your
            //PredictionRigidbody field in the reconcile data is an easy
            //way to accomplish this. More advanced states may require other
            //values to be sent; this will be covered later on.
            var rd = new ReconcileData(_predictionRigidbody2D);
            //Like with the replicate you could specify a channel here, though
            //it's unlikely you ever would with a reconcile.
            ReconcileState(rd);
        }

        [Reconcile]
        private void ReconcileState(ReconcileData data, Channel channel = Channel.Unreliable)
        {
            //Call reconcile on your PredictionRigidbody field passing in
            //values from data.
            _predictionRigidbody2D.Reconcile(data.PredictionRigidbody2D);
        }
        #endregion

        #region Network Hook Events
        protected override void TimeManager_OnTick()
        {
            base.TimeManager_OnTick();
            RunInputs(CreateReplicateData());
        }

        protected override void TimeManager_OnPostTick()
        {
            base.TimeManager_OnPostTick();
            CreateReconcile();
        }
        #endregion
    }
}
