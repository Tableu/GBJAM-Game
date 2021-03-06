using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Enemies
{
    public abstract class EnemyBase : MonoBehaviour, IDamageable
    {
        // todo: clean up interface
        [Header("Core Enemy Config")] [SerializeField]
        public float walkingSpeed;
        [SerializeField] private float knockbackFactor;
        [SerializeField] public int collisionDamage=1;
        [SerializeField] public int collisionKnockback=20;
        [SerializeField] private int maxHealth;
        [SerializeField] private int currentHealth;
        [SerializeField] private GameObject shell;
        [SerializeField] public AttackScriptableObject attackConfig;
        [SerializeField] private ParticleSystem _particleSystem;

        [Header("Player Detection Config")] [Range(10, 360)] [SerializeField]
        private float fieldOfView;

        [SerializeField] private float visionRange;
        [SerializeField] private float detectionRange;
        [FormerlySerializedAs("attackTime")] [SerializeField] protected float deaggroTime = 5;


        [SerializeField] protected LayerMask sightBlockingLayers;

        public AttackCommand Attack { get; set; }
        public MovementController MovementController { get; private set; }
        private LayerMask _playerLayer;

        [NonSerialized] public EnemyAnimatorController Animator;
        [NonSerialized] public bool CanAttack = false;
        protected LayerMask groundLayer;

        protected PlayerController Player;
        public Transform PlayerTransform { get; private set; }
        protected FSM StateMachine;
        protected float timeSinceSawPlayer;

        protected Vector2 Forward => Vector2.right * transform.localScale.x;

        protected void Awake()
        {
            var playerGO = GameObject.FindWithTag("Player");
            groundLayer = LayerMask.GetMask("Ground");
            _playerLayer = LayerMask.GetMask("Player");

            Animator = GetComponentInChildren<EnemyAnimatorController>();
            Player = playerGO.GetComponent<PlayerController>();

            MovementController = new MovementController(gameObject, walkingSpeed);
            StateMachine = new FSM();
            Attack = attackConfig?.MakeAttack();

            currentHealth = maxHealth;
            PlayerTransform = playerGO.transform;
        }

        protected void Update()
        {
            if (Player != null && PlayerTransform != null)
            {
                LookForPlayer();
            }

            StateMachine.Tick();
            if (Attack is {IsRunning: false} && CanAttack)
            {
                Animator.TriggerAttack();
                StartCoroutine(Attack.DoAttack(gameObject));
            }
        }

        private void OnCollisionEnter2D(Collision2D other)
        {
            if (!other.gameObject.CompareTag("Player")) return;
            // todo: add variable for these properties
            var dmg = new Damage(transform.position, collisionKnockback, collisionDamage);
            Player.TakeDamage(dmg);
        }

        public void TakeDamage(Damage dmg)
        {
            currentHealth -= dmg.RawDamage;
            // kill if zero health
            if (currentHealth <= 0)
            {
                Instantiate(shell, new Vector3(transform.position.x,transform.position.y,0), Quaternion.identity);
                Animator.TriggerDeath();
                Destroy(gameObject);
                return;
            }

            Animator.TriggerHurt();
            _particleSystem.Emit(12);
            // apply knockback
            dmg.Knockback *= knockbackFactor;
            StartCoroutine(MovementController.Knockback(dmg));
        }
        
        protected bool PlayerVisible()
        {
            // Check if player is in fov
            if (PlayerTransform != null)
            {
                Vector2 playerPos = PlayerTransform.position - transform.position;
                var angle = Vector2.Angle(Forward, playerPos);
                var distance = playerPos.magnitude;
                if (angle <= fieldOfView / 2 && distance <= visionRange || distance < detectionRange)
                {
                    // Check for line of sight
                    var hit = Physics2D.Raycast(transform.position, playerPos, distance + 1, sightBlockingLayers);
                    if (hit.transform == PlayerTransform) return true;
                }
            }

            return false;
        }

        private void LookForPlayer()
        {
            if (PlayerVisible())
                timeSinceSawPlayer = 0;
            else
                timeSinceSawPlayer += Time.deltaTime;
        }

        protected class FallState : IState
        {
            private readonly EnemyBase _enemy;

            public FallState(EnemyBase enemy)
            {
                _enemy = enemy;
            }

            public void Tick()
            {
                // todo: die after falling for a long time?
            }

            public void OnEnter()
            {
                _enemy.Animator.SetIsMoving(false);
            }

            public void OnExit()
            {
                _enemy.Animator.SetIsMoving(true);
            }
        }
    }
}