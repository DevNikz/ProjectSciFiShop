using UnityEngine;

/// <summary>
/// Saves where the player stands and which way they face. Put it on the player (next to the
/// CharacterController). The ES3 Auto Save must not save the player's Transform: a CharacterController
/// ignores a plain position change, so this component moves it the right way.
/// </summary>
public class PlayerSaveAdapter : SaveParticipant
{
    // Saved by Easy Save (public fields of a listed component).
    public Vector3 position;
    public float yaw;
    public bool hasPose;

    CharacterController _controller;

    protected override void Awake()
    {
        base.Awake();
        _controller = GetComponent<CharacterController>();
    }

    public override void BeforeSave()
    {
        position = transform.position;
        yaw = transform.eulerAngles.y;
        hasPose = true;
    }

    public override void AfterLoad()
    {
        if (!hasPose)
            return;
        bool wasEnabled = _controller != null && _controller.enabled;
        if (_controller != null)
            _controller.enabled = false; // or the controller undoes the move
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        if (_controller != null)
            _controller.enabled = wasEnabled;
    }
}
