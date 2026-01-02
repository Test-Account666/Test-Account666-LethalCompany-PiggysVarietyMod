using LethalCompanyInputUtils.Api;
using LethalCompanyInputUtils.BindingPathEnums;
using UnityEngine.InputSystem;

namespace PiggysVarietyMod.Utils;

public class ItemInputs : LcInputActions {
    [InputAction(KeyboardControl.R, Name = "Reload", GamepadPath = "<Gamepad>/buttonNorth")]
    public InputAction RifleReloadKey { get; set; }

    [InputAction(MouseControl.LeftButton, Name = "Fire Weapon", GamepadPath = "<Gamepad>/rightTrigger")]
    public InputAction FireKey { get; set; }

    [InputAction(KeyboardControl.C, Name = "Switch Fire Mode", GamepadPath = "<Gamepad>/buttonEast")]
    public InputAction SwitchFireModeKey { get; set; }

    [InputAction(KeyboardControl.F, Name = "Inspect Weapon", GamepadPath = "<Gamepad>/leftStickPress")]
    public InputAction InspectKey { get; set; }
}