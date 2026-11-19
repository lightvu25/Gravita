using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Central input hub for the game.
///
/// Directional input (arrow keys / WASD bindings or touch swipe) is surfaced
/// through <see cref="OnDirectionPressed"/> and consumed by PlayerMovement.
/// Held-direction input is available via <see cref="MovementInput"/>.
/// </summary>
[DefaultExecutionOrder(-100)]
public partial class GameInput : MonoBehaviour
{
    public static GameInput Instance { get; private set; }

    public event EventHandler OnMenuButtonPressed;
    public event Action OnInteractPressed;
    public event Action OnConfirmPressed;
    public event Action OnCancelPressed;

    /// <summary>
    /// Edge-triggered directional input: a keyboard direction press or a
    /// completed touch/mouse swipe. Argument is a cardinal direction
    /// (Vector2.left/right/up/down).
    /// </summary>
    public event Action<Vector2> OnDirectionPressed;

    [Header("Input Configuration")]
    [SerializeField] private InputConfig inputConfig;

    [Header("Swipe")]
    [Tooltip("Minimum pointer drag distance in pixels to count as a swipe.")]
    [SerializeField, Min(10f)] private float minSwipeDistance = 60f;

    private InputActions inputActions;
    private InputConfig.ControlScheme lastScheme;

    private static readonly Dictionary<KeyCode, Key> KeyLookupCache = new Dictionary<KeyCode, Key>(16);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetKeyCache()
    {
        KeyLookupCache.Clear();
    }

    public KeyCode InteractKey => inputConfig != null ? inputConfig.InteractKey : KeyCode.F;
    public KeyCode ConfirmKey => inputConfig != null ? inputConfig.ConfirmKey : KeyCode.Space;
    public KeyCode CancelKey => inputConfig != null ? inputConfig.CancelKey : KeyCode.Escape;

    /// <summary>Minimum pointer drag distance in pixels to count as a swipe.</summary>
    public float MinSwipeDistance => inputConfig != null ? inputConfig.MinSwipePixels : minSwipeDistance;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        inputActions = new InputActions();
        inputActions.Enable();

        inputActions.Player.Menu.performed += Menu_performed;

        inputActions.Player.PlayerLeft.performed  += Left_performed;
        inputActions.Player.PlayerRight.performed += Right_performed;
        inputActions.Player.PlayerUp.performed    += Up_performed;
        inputActions.Player.PlayerDown.performed  += Down_performed;
    }

    private void Start()
    {
        if (inputConfig != null)
        {
            lastScheme = inputConfig.ActiveScheme;
            ApplyBindings();
        }
    }

    private void Update()
    {
        UpdateMobileInput();
        UpdateSwipeInput();
        if (!isActiveAndEnabled) return;

        if (inputConfig != null && inputConfig.ActiveScheme != lastScheme)
        {
            lastScheme = inputConfig.ActiveScheme;
            ApplyBindings();
        }

        if (WasKeyPressed(InteractKey)) OnInteractPressed?.Invoke();
        if (WasKeyPressed(ConfirmKey)) OnConfirmPressed?.Invoke();
        if (WasKeyPressed(CancelKey)) OnCancelPressed?.Invoke();
    }

    private void ApplyBindings()
    {
        if (inputConfig == null) return;

        bool isWASD = inputConfig.ActiveScheme == InputConfig.ControlScheme.WASD;

        ApplyBinding(inputActions.Player.PlayerLeft,  isWASD ? "<Keyboard>/a" : "<Keyboard>/leftArrow");
        ApplyBinding(inputActions.Player.PlayerRight, isWASD ? "<Keyboard>/d" : "<Keyboard>/rightArrow");
        ApplyBinding(inputActions.Player.PlayerUp,    isWASD ? "<Keyboard>/w" : "<Keyboard>/upArrow");
        ApplyBinding(inputActions.Player.PlayerDown,  isWASD ? "<Keyboard>/s" : "<Keyboard>/downArrow");
    }

    private void ApplyBinding(InputAction action, string path)
    {
        if (action == null || string.IsNullOrEmpty(path)) return;
        if (action.bindings.Count == 0)
        {
            bool wasEnabled = action.enabled;
            if (wasEnabled) action.Disable();
            action.AddBinding(path);
            if (wasEnabled) action.Enable();
            return;
        }
        action.ApplyBindingOverride(0, new InputBinding { overridePath = path });
    }

    private void Menu_performed(InputAction.CallbackContext obj) => OnMenuButtonPressed?.Invoke(this, EventArgs.Empty);
    private void Left_performed(InputAction.CallbackContext obj)  => OnDirectionPressed?.Invoke(Vector2.left);
    private void Right_performed(InputAction.CallbackContext obj) => OnDirectionPressed?.Invoke(Vector2.right);
    private void Up_performed(InputAction.CallbackContext obj)    => OnDirectionPressed?.Invoke(Vector2.up);
    private void Down_performed(InputAction.CallbackContext obj)  => OnDirectionPressed?.Invoke(Vector2.down);

    private void OnDestroy()
    {
        if (inputActions != null)
        {
            inputActions.Player.Menu.performed -= Menu_performed;
            inputActions.Player.PlayerLeft.performed  -= Left_performed;
            inputActions.Player.PlayerRight.performed -= Right_performed;
            inputActions.Player.PlayerUp.performed    -= Up_performed;
            inputActions.Player.PlayerDown.performed  -= Down_performed;
            inputActions.Disable();
        }

        if (Instance == this)
            Instance = null;
    }

    public void SetInputsEnabled(bool isEnabled)
    {
        if (isEnabled)
        {
            inputActions?.Enable();
            this.enabled = true;
        }
        else
        {
            inputActions?.Disable();
            this.enabled = false;
        }
    }

    public bool IsUpActionPressed()    => MovementInput.y > 0.5f;
    public bool IsDownActionPressed()  => MovementInput.y < -0.5f;
    public bool IsLeftActionPressed()  => MovementInput.x < -0.5f;
    public bool IsRightActionPressed() => MovementInput.x > 0.5f;

    public bool IsInteractActionPressed() => isActiveAndEnabled && (WasKeyPressed(InteractKey) || IsMobilePressed(MobileInputAction.Interact));
    public bool IsConfirmActionPressed()  => isActiveAndEnabled && (WasKeyPressed(ConfirmKey)  || IsMobilePressed(MobileInputAction.Confirm));
    public bool IsPauseActionPressed()    => isActiveAndEnabled && (inputActions.Player.Menu.IsPressed() || IsMobilePressed(MobileInputAction.Menu));

    /// <summary>
    /// Input-System equivalent of legacy <c>Input.GetKeyDown</c>: maps a
    /// <see cref="KeyCode"/> to <see cref="Key"/> via a static lookup cache
    /// so zero string/enum allocations occur per frame.
    /// </summary>
    public static bool WasKeyPressed(KeyCode keyCode)
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return false;

        Key key = ResolveInputSystemKey(keyCode);
        return key != Key.None && keyboard[key].wasPressedThisFrame;
    }

    public static Key ResolveInputSystemKey(KeyCode keyCode)
    {
        if (KeyLookupCache.TryGetValue(keyCode, out Key cached))
            return cached;

        // KeyCode names that differ from InputSystem Key enum names.
        string name = keyCode.ToString();
        if (name.StartsWith("Alpha", StringComparison.Ordinal)) name = "Digit" + name.Substring(5);
        else if (name == "Return") name = "Enter";

        Key resolved = Enum.TryParse(name, out Key parsed) ? parsed : Key.None;
        KeyLookupCache[keyCode] = resolved;
        return resolved;
    }
}
