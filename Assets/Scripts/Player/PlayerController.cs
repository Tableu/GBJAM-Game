using System.Collections;
using Attacks;
using UnityEngine.InputSystem;
using UnityEngine;
using Cinemachine;
using UnityEngine.PlayerLoop;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerController : MonoBehaviour, IDamageable
{
    private PlayerInputActions _playerInputActions;
    private ContactFilter2D _enemyFilter2D;
    private AttackCommand _attackCommand;
    public MovementController MovementController { get; private set; }

    [SerializeField] private ShellStats meleeStats;
    [SerializeField] private ShellStats playerStats;
    [SerializeField] private Rigidbody2D rigidBody;
    [SerializeField] private Collider2D col;
    [SerializeField] private PlayerAnimatorController playerAnimatorController;
    [SerializeField] private SpriteRenderer playerShellSpriteRenderer;
    [SerializeField] private AttackScriptableObject _attack;
    [SerializeField] private ParticleSystem particleSystem;
    [SerializeField] private SpriteRenderer smearSprite;
    [SerializeField] private ShellStats shell;
    [SerializeField] private ShellManager shellManager;

    [SerializeField] private int health;
    [SerializeField] private int armor;
    [SerializeField] private int coins;
    [SerializeField] private Vector2 speed;
    [Header("Bools")]
    [SerializeField] private bool grounded;
    [SerializeField] public bool frontClear;
    [SerializeField] public bool inputLocked;
    [SerializeField] private bool hiding;
    [SerializeField] private bool nearCeiling;
    private bool finish;
    private float hideStartTime;
    
    public const string NO_SHELL = "NoShell";
    public const string SNAIL_SHELL = "SnailShell";
    public const string SPIKY_SHELL = "SpikyShell";
    public const string CONCH_SHELL = "ConchShell";

    public const string ARMOR = "Armor";
    public const string COINS = "Coins";
    public const string HEALTH = "Health";
    public const string SHELL = "Shell";
    public const string REDEEM_AMOUNT = "RedeemAmount";
    public int Health
    {
        get { return health; }
    }
    public int Armor
    {
        get { return armor; }
    }
    // [SerializeField, Range(0, 1f)] private float knockBackDuration = 0.25f;
    // private bool isInKnockback = false;

    private void Awake()
    {
        _playerInputActions = new PlayerInputActions();
        MovementController = new MovementController(gameObject, speed.x);
    }

    private void OnEnable()
    {
        _playerInputActions.Enable();
    }

    private void OnDisable()
    {
        _playerInputActions.Disable();
    }
    void Start()
    {
        _playerInputActions.Player.Jump.started += context =>
        {
            float height = speed.y;
            if (Time.time - hideStartTime > 1f && hiding)
            {
                height += 2.5f;
            }
            if (MovementController.Jump(height))
            {
                pSoundManager.PlaySound(pSoundManager.Sound.pJump);
                if (hiding)
                {
                    _playerInputActions.Player.Hide.Disable();
                    _playerInputActions.Player.Hide.Enable();
                }
            }
        };
        _playerInputActions.Player.Jump.canceled += context =>
        {
            rigidBody.gravityScale = shell.speed.y * 0.8f;
        };
        _playerInputActions.Player.Move.canceled += Idle;
        _playerInputActions.Player.PickUpShell.started += SwitchShells;

        _playerInputActions.Player.Hide.started += Hide;
        _playerInputActions.Player.Hide.canceled += Hide;

        _playerInputActions.Player.Attack.started += Attack;
        _enemyFilter2D = new ContactFilter2D
        {
            layerMask = LayerMask.GetMask("Enemy"),
            useLayerMask = true
        };
        finish = false;
        
        GameObject savedShell = shellManager.GetShell(PlayerPrefs.GetString(SHELL, NO_SHELL));

        if (savedShell == null)
        {
            SetStats(gameObject.GetComponent<ShellStats>());
        }
        else
        {
            shell = Instantiate(savedShell, transform.position, Quaternion.identity).GetComponent<ShellStats>();
            EquipShell();
            SetStats(shell);
        }
        armor = PlayerPrefs.GetInt(ARMOR, 0);
        coins = PlayerPrefs.GetInt(COINS, 0);
        health = PlayerPrefs.GetInt(HEALTH, 2);
        if (armor == 1 && shell != null)
        {
            playerShellSpriteRenderer.sprite = shell.SwitchSprite(playerShellSpriteRenderer);
        }
        HUDManager.Instance.UpdateCoins(coins);
        HUDManager.Instance.UpdateArmor(armor);
        HUDManager.Instance.UpdateHealth(health);
        CinemachineVirtualCamera virtualCamera = FindObjectOfType<CinemachineVirtualCamera>();
        if (virtualCamera)
        {
            virtualCamera.Follow = transform;
        }
    }

    // Update is called once per frame
    void Update()
    {
        grounded = MovementController.Grounded();
        nearCeiling = MovementController.NearCeiling();
        if (grounded)
        {
            playerAnimatorController.SetIsGrounded(true);
            rigidBody.gravityScale = 3f;
        }
        else
        {
            playerAnimatorController.SetIsGrounded(false);
        }
        frontClear = MovementController.FrontClear();
        Move();
        if (_attackCommand != null)
        {
            if (_attackCommand.LockInput)
            {
                inputLocked = true;
            }
            else
            {
                inputLocked = false;
            }
        }
    }

    public void EnableInputActions(bool enable)
    {
        if (enable)
        {
            _playerInputActions.Enable();
        }
        else
        {
            _playerInputActions.Disable();
        }
    }

    public void SetStats(ShellStats shellStats)
    {
        armor = shellStats.armor;
        speed = shellStats.speed;
        MovementController.WalkingSpeed = shellStats.speed.x;
        if (hiding)
        {
            MovementController.WalkingSpeed *= 0.4f;
        }
        _attack = shellStats.attackConfig;
        if (_attack != null)
        {
            _attackCommand = _attack.MakeAttack();
        }
        playerStats = shellStats;
        _playerInputActions.Player.Attack.Enable();
        //Update UI each time stats are changed.
        HUDManager.Instance.UpdateHealth(health);
        HUDManager.Instance.UpdateArmor(armor);
    }
    private void Move()
    {
        var horizontal = _playerInputActions.Player.Move.ReadValue<float>();
        var horizontalVelocity = horizontal * speed.x;
        if (!inputLocked)
        {
            MovementController.MoveHorizontally(horizontalVelocity);
            if (horizontalVelocity != 0)
            {
                playerAnimatorController.SetIsMoving(true);
            }
        }
    }

    private void Idle(InputAction.CallbackContext context)
    {
        rigidBody.velocity = new Vector2(0, rigidBody.velocity.y);
        playerAnimatorController.SetIsMoving(false);
    }

    private void Attack(InputAction.CallbackContext context)
    {
        if (_attackCommand != null)
        {
            Debug.Log("Attack");
            if (!_attackCommand.IsRunning && !hiding)
            {
                Debug.Log(_attack.GetType());
                if (_attack.GetType() == typeof(Attacks.MeleeAttack))
                {
                    playerAnimatorController.TriggerAttack();
                }

                if (_attack.GetType() == typeof(Attacks.DashAttack))
                {
                    playerAnimatorController.TriggerAttack();
                    particleSystem.Emit(20);
                    StartCoroutine(DashCooldown(1f));
                }
                StartCoroutine(_attackCommand.DoAttack(gameObject));
                pSoundManager.PlaySound(pSoundManager.Sound.pAttack);
            }
        }
    }

    private IEnumerator DashCooldown(float coolDown)
    {
        _playerInputActions.Player.Attack.Disable();
        yield return new WaitForSeconds(coolDown);
        _playerInputActions.Player.Attack.Enable();
    }
    private void SwitchShells(InputAction.CallbackContext context)
    {
        if (finish)
        {
            return;
        }
        ContactFilter2D contactFilter2D = new ContactFilter2D
        {
            layerMask = LayerMask.GetMask("Shells"),
            useLayerMask = true
        };

        Collider2D[] collider2D = new Collider2D[1];

        if (Physics2D.OverlapCollider(col, contactFilter2D, collider2D) == 1)
        {
            DropShell();
            shell = collider2D[0].gameObject.GetComponent<ShellStats>();
            SetStats(shell);
            playerAnimatorController.TriggerShellSwap();
            EquipShell();
            pSoundManager.PlaySound(pSoundManager.Sound.pPickup);
        }
        else if(_attack.GetType() != typeof(MeleeAttack))
        { 
            DropShell(); 
            playerShellSpriteRenderer.sprite = null;
            SetStats(meleeStats);
            playerAnimatorController.TriggerShellSwap();
            pSoundManager.PlaySound(pSoundManager.Sound.pPickup);
        }
    }

    private void EquipShell()
    {
        playerShellSpriteRenderer.sprite = shell.GetComponent<SpriteRenderer>().sprite;
        shell.Equipped(gameObject.transform);
    }
    private void DropShell()
    {
        if (shell != null)
        {
            shell.Dropped(armor, transform.localScale.x);
            shell = null;
        }
    }
    private void Hide(InputAction.CallbackContext context)
    {
        if ((context.started || context.performed) && !hiding && grounded)
        {
            hiding = true;
            MovementController.WalkingSpeed *= 0.4f;
            MovementController.Stop();
            playerAnimatorController.SetIsHiding(true);
            pSoundManager.PlaySound(pSoundManager.Sound.pHide);
            BoxCollider2D box = (BoxCollider2D)col;
            box.size = new Vector2(box.size.x, box.size.y*0.5f);
            box.offset = new Vector2(box.offset.x, box.offset.y*0.5f);
        }
        else if (context.canceled && hiding && !nearCeiling)
        {
            StopHiding();
        }
        else if(context.canceled && hiding && nearCeiling)
        {
            StartCoroutine(AutoStopHide());
        }

        if (context.started)
        {
            hideStartTime = Time.time;
        }
        
    }

    private IEnumerator AutoStopHide()
    {
        while (hiding)
        {
            if (!nearCeiling && hiding && grounded)
            {
                StopHiding();
            }
            yield return null;
        }
    }
    
    private void StopHiding()
    {
        hiding = false;
        MovementController.WalkingSpeed = playerStats.speed.x;
        playerAnimatorController.SetIsHiding(false);
        BoxCollider2D box = (BoxCollider2D)col;
        box.offset = new Vector2(box.offset.x, box.offset.y*2f);
        box.size = new Vector2(box.size.x, box.size.y*2f);
    }
    public void TakeDamage(Damage dmg)
    {
        if (_attackCommand.IsRunning && _attack.GetType() == typeof(DashAttack))
        {
            return;
        }

        if (_attackCommand.IsRunning && _attack.GetType() == typeof(MeleeAttack) && !col.IsTouching(_enemyFilter2D))
        {
            return;
        }
        if (armor > 0)
        {
            LoseArmor(dmg);
        }
        else
        {
            LoseHealth(dmg);
        }
        pSoundManager.PlaySound(pSoundManager.Sound.pHit);
        if (health > 0)
        {
            StartCoroutine(Invulnerable());
        }

        //Only do the Knockback coroutine if knockback on dmg isn't 0, so player doesn't come to a full stop for a moment if knockback is 0.
        if (dmg.Knockback != 0)
        {
            StartCoroutine(MovementController.Knockback(dmg));
        }
    }

    private void LoseArmor(Damage dmg)
    {
        armor -= dmg.RawDamage;
        HUDManager.Instance.UpdateArmor(Mathf.Max(0, armor));
        if (armor <= 0)
        {
            BreakShell();
        }
        else if (armor == 1)
        {
            playerShellSpriteRenderer.sprite = shell.SwitchSprite(playerShellSpriteRenderer);
        }
        Debug.Log("Lose Armor");
    }
    private void LoseHealth(Damage dmg)
    {
        health -= dmg.RawDamage;
        HUDManager.Instance.UpdateHealth(Mathf.Max(0, health));
        if (health == 0)
        {
            Death();
        }
        Debug.Log("Lose Health");
    }

    private void Death()
    {
        pSoundManager.PlaySound(pSoundManager.Sound.pDie);
        gameObject.layer = LayerMask.NameToLayer("Invulnerable");
        playerAnimatorController.TriggerDeath();
        _playerInputActions.Disable();
        if (shell == null)
        {
            PlayerPrefs.SetString(SHELL, NO_SHELL);
        }
        else
        {
            PlayerPrefs.SetString(SHELL, shell.shell);
        }
        PlayerPrefs.SetInt(ARMOR, 0);
        //Tell MapManager the player died, it handles respawn and such.
        if (MapManager.Instance)
        {
            MapManager.Instance.PlayerDied();
        }
        
        Destroy(gameObject, 1f);
    }

    private void BreakShell()
    {
        playerShellSpriteRenderer.sprite = null;
        if (transform.childCount > 1)
        {
            shell.BreakShell(gameObject.transform);
        }
        shell = null;
        SetStats(meleeStats);
        //switch to melee
    }

    private void RedeemCoins()
    {
        if (coins > PlayerPrefs.GetInt(REDEEM_AMOUNT,20)-1)
        {
            if (_attack.GetType() == typeof(Attacks.MeleeAttack))
            {
                shell = shellManager.RedeemShell(gameObject);
                EquipShell();
                SetStats(shell);
                playerShellSpriteRenderer.sprite = shell.SwitchSprite(playerShellSpriteRenderer);
                coins = 0;
                pSoundManager.PlaySound((pSoundManager.Sound.shellRedeem));
            }else if(health < PlayerPrefs.GetInt(HEALTH))
            {
                health++;
                coins = 0;
                HUDManager.Instance.UpdateHealth(Mathf.Max(0, health));
                pSoundManager.PlaySound(pSoundManager.Sound.hpIncrease);
            }
            else
            {
                armor++;
                HUDManager.Instance.UpdateArmor(Mathf.Max(0, armor));
                if (armor == 2)
                {
                    playerShellSpriteRenderer.sprite = shell.SwitchSprite(playerShellSpriteRenderer);
                }
                coins = 0;
                pSoundManager.PlaySound((pSoundManager.Sound.hpIncrease));
            }
        }
    }
    private IEnumerator Invulnerable()
    {
        gameObject.layer = LayerMask.NameToLayer("Invulnerable");
        playerAnimatorController.SetIsInvulnerable(true);
        yield return new WaitForSeconds(1);
        gameObject.layer = LayerMask.NameToLayer("Player");
        playerAnimatorController.SetIsInvulnerable(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        switch (LayerMask.LayerToName(other.transform.gameObject.layer))
        {
            case "Secret":
                //Do something??
                break;
            case "Spikes":
                //TakeDamage(new Damage(new Vector2(transform.position.x - transform.localScale.x, transform.position.y), 20f, 1));
                Damage dmg = new Damage(transform.position, 0, 1);
                if (armor > 0)
                {
                    LoseArmor(dmg);
                }
                else
                {
                    LoseHealth(dmg);
                }
                pSoundManager.PlaySound(pSoundManager.Sound.pHit);
                if (health > 0)
                {
                    StartCoroutine(Invulnerable());
                }

                //Only do the Knockback coroutine if knockback on dmg isn't 0, so player doesn't come to a full stop for a moment if knockback is 0.
                if (dmg.Knockback != 0)
                {
                    StartCoroutine(MovementController.Knockback(dmg));
                }
                break;
            case "Coins":
                Destroy(other.gameObject);
                coins++;
                RedeemCoins();                
                HUDManager.Instance.UpdateCoins(Mathf.Max(0, coins));
                pSoundManager.PlaySound(pSoundManager.Sound.pCoin);
                break;
        }

        if (other.CompareTag("Finish"))
        {
            finish = true;
        }
    }
    
    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.CompareTag("Finish") && _playerInputActions.Player.PickUpShell.phase == InputActionPhase.Started)
        {
            other.gameObject.GetComponent<LevelEndTrigger>().OpenChest();
            PlayerPrefs.SetString(SHELL, shell.shell);
            PlayerPrefs.SetInt(HEALTH, health);
            PlayerPrefs.SetInt(ARMOR, armor);
            PlayerPrefs.SetInt(COINS, coins);
            _playerInputActions.Disable();
        }
    }

    //private void OnCollisionExit(Collision other)
    //{
    //    if (other.collider.gameObject.layer == LayerMask.NameToLayer("Ground"))
    //    {
    //        Grounded();
    //    }
    //}
}



#if UNITY_EDITOR
[CustomEditor(typeof(PlayerController))]
class PlayerControllerEditor : Editor
{
    PlayerController player { get { return target as PlayerController; } }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (Application.isPlaying)
        {
            EditorExtensionMethods.DrawSeparator(Color.gray);
            if (GUILayout.Button("Damage left"))
            {
                Damage auxDamage = new Damage((Vector2)player.transform.position + new Vector2(0.5f, -0.5f), 20f, 0);
                player.TakeDamage(auxDamage);
            }
            if (GUILayout.Button("Damage right"))
            {
                Damage auxDamage = new Damage((Vector2)player.transform.position + new Vector2(-0.5f, -0.5f), 20f, 0);
                player.TakeDamage(auxDamage);
            }
        }
    }
}
#endif