using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Base for in-world computers. Interact to open a screen, even while carrying something.
/// Subclasses only add the [InteractableAddon] label.
/// </summary>
public abstract class InteractableTerminal : MonoBehaviour, IPromptContextFlag
{
    [Tooltip("Screen opened on interact. Must implement IScreen.")]
    [FormerlySerializedAs("screen")]
    [SerializeField]
    GameObject screenBehaviour;

    Interactable _interactable;
    IScreen _screen;

    public PromptContext Flag => PromptContext.LookAtComputer;

    void Awake()
    {
        _interactable = GetComponent<Interactable>();
        _screen = screenBehaviour.GetComponent<IScreen>();
    }

    void OnEnable()
    {
        if (_interactable == null || _screen == null)
        {
            Debug.LogError(
                $"{name}: Interactable or screen missing (the screen must implement IScreen).",
                this
            );
            enabled = false;
            return;
        }
        _interactable.OnInteractEvent += HandleInteract;
    }

    void OnDisable()
    {
        if (_interactable != null)
            _interactable.OnInteractEvent -= HandleInteract;
    }

    void HandleInteract(Interactor who)
    {
        if (who != null)
            _screen.Open();
    }
}
