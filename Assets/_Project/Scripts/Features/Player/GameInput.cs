using System;
using UnityEngine.InputSystem;

namespace Game.Features.Player
{
    /// <summary>
    /// All gameplay input actions (New Input System), defined in code so bindings are visible in one place.
    /// One instance per application, owned by the container.
    /// </summary>
    public sealed class GameInput : IDisposable
    {
        private readonly InputActionMap _map = new InputActionMap("Gameplay");

        public GameInput()
        {
            Move = _map.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            Move.AddBinding("<Gamepad>/leftStick");

            Look = _map.AddAction("Look", InputActionType.Value);
            Look.AddBinding("<Mouse>/delta");
            Look.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=8,y=8)");

            Jump = AddButton("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            Sprint = AddButton("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            Grab = AddButton("Grab", "<Keyboard>/e", "<Gamepad>/buttonWest");
            Throw = AddButton("Throw", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            Drop = AddButton("Drop", "<Keyboard>/q", "<Gamepad>/buttonEast");
            Delete = AddButton("Delete", "<Keyboard>/x", "<Gamepad>/buttonNorth");
            SpawnItem = AddButton("SpawnItem", "<Keyboard>/f", "<Gamepad>/rightShoulder");
            NextItemType = AddButton("NextItemType", "<Keyboard>/r", "<Gamepad>/leftShoulder");
            ChangeColor = AddButton("ChangeColor", "<Keyboard>/c", "<Gamepad>/dpad/up");

            _map.Enable();
        }

        public InputAction Move { get; }
        public InputAction Look { get; }
        public InputAction Jump { get; }
        public InputAction Sprint { get; }
        public InputAction Grab { get; }
        public InputAction Throw { get; }
        public InputAction Drop { get; }
        public InputAction Delete { get; }
        public InputAction SpawnItem { get; }
        public InputAction NextItemType { get; }
        public InputAction ChangeColor { get; }

        public void Dispose()
        {
            _map.Disable();
            _map.Dispose();
        }

        private InputAction AddButton(string name, string keyboard, string gamepad)
        {
            InputAction action = _map.AddAction(name, InputActionType.Button);
            action.AddBinding(keyboard);
            action.AddBinding(gamepad);
            return action;
        }
    }
}
