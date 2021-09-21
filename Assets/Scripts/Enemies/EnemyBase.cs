using System;
using UnityEngine;

namespace Enemies
{
    public abstract class EnemyBase : MonoBehaviour, IDamageable
    {
        // todo: clean up interface
        [Header("Core Enemy Config")] [SerializeField]
        private float walkingSpeed;

        [SerializeField] private float knockbackFactor;
        [SerializeField] private int knockbackDamage;
        [SerializeField] private int maxHealth;
        [SerializeField] private int currentHealth;
        [SerializeField] public AttackScriptableObject attackConfig;


        [Header("Player Detection Config")] [Range(10, 360)] [SerializeField]
        private float fieldOfView;

        [SerializeField] private float visionRange;
        [SerializeField] private float detectionRange;
        [SerializeField] protected float attackTime = 5;


        [SerializeField] protected LayerMask sightBlockingLayers;

        private AttackCommand _attack;
        protected MovementController _movementController;
        private LayerMask _playerLayer;

        [NonSerialized] public EnemyAnimatorController Animator;
        [NonSerialized] public bool CanAttack = false;
        protected LayerMask groundLayer;

        protected PlayerController Player;
        protected Transform PlayerTransform;
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

            _movementController = new MovementController(gameObject, walkingSpeed);
            StateMachine = new FSM();
            _attack = attackConfig.MakeAttack();

            currentHealth = maxHealth;
            PlayerTransform = playerGO.transform;
        }

        protected void Update()
        {
            LookForPlayer();
            StateMachine.Tick();
            if (!_attack.IsRunning && CanAttack)
            {
                Animator.TriggerAttack();
                StartCoroutine(_attack.DoAttack(gameObject));
            }
        }

        private void OnCollisionEnter2D(Collision2D other)
        {
            if (1 << other.gameObject.layer != _playerLayer) return;
            // todo: add variable for these properties
            var dmg = new Damage(transform.position, 20, 1);
            Player.TakeDamage(dmg);
        }

        public void TakeDamage(Damage dmg)
        {
            currentHealth -= dmg.RawDamage;
            // kill if zero health
            if (currentHealth <= 0)
            {
                // todo: add death state to play animation (Coroutine)
                Animator.TriggerDeath();
                Destroy(gameObject);
                return;
            }

            Animator.TriggerHurt();
            // apply knockback
            dmg.Knockback *= knockbackFactor;
            StartCoroutine(_movementController.Knockback(dmg));
        }

        protected bool PlayerVisible()
        {
            // Check if player is in fov
            Vector2 playerPos = PlayerTransform.position - transform.position;
            var angle = Vector2.Angle(Forward, playerPos);
            var distance = playerPos.magnitude;
            if (angle <= fieldOfView / 2 && distance <= visionRange || distance < detectionRange)
            {
                // Check for line of sight
                var hit = Physics2D.Raycast(transform.position, playerPos, distance + 1, sightBlockingLayers);
                if (hit.transform == PlayerTransform) return true;
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