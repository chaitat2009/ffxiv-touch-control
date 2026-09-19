using System;
using System.Collections.Concurrent;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace TouchControl.Game;

/// <summary>
/// Everything that pokes the game directly. Reads are done wherever they are needed (the draw callback runs on the
/// main thread), but anything that *executes* game logic is queued here and drained from Framework.Update so we
/// never call into the game mid-render.
/// </summary>
public sealed unsafe class GameActions
{
    private readonly ConcurrentQueue<Action> queue = new();

    /// <summary>GeneralAction row ids used by the mount toggle button.</summary>
    public const uint GeneralActionMountRoulette = 9;
    public const uint GeneralActionDismount = 23;

    // ---- queue ----------------------------------------------------------------------------------------

    public void Enqueue(Action action) => queue.Enqueue(action);

    /// <summary>Called from IFramework.Update.</summary>
    public void Drain()
    {
        while (queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Queued game action failed");
            }
        }
    }

    // ---- hotbars --------------------------------------------------------------------------------------

    public readonly struct SlotInfo
    {
        public readonly bool Valid;
        public readonly bool Empty;
        public readonly uint IconId;
        public readonly RaptureHotbarModule.HotbarSlotType Type;
        public readonly uint CommandId;
        public readonly string Keybind;

        public SlotInfo(bool valid, bool empty, uint iconId, RaptureHotbarModule.HotbarSlotType type, uint commandId, string keybind)
        {
            Valid = valid;
            Empty = empty;
            IconId = iconId;
            Type = type;
            CommandId = commandId;
            Keybind = keybind;
        }

        public static readonly SlotInfo Invalid = new(false, true, 0, RaptureHotbarModule.HotbarSlotType.Empty, 0, string.Empty);
    }

    public SlotInfo ReadSlot(int hotbar, int slot)
    {
        if (hotbar is < 0 or > 17 || slot is < 0 or > 15) return SlotInfo.Invalid;

        var module = RaptureHotbarModule.Instance();
        if (module == null) return SlotInfo.Invalid;

        var s = module->GetSlotById((uint)hotbar, (uint)slot);
        if (s == null) return SlotInfo.Invalid;

        return new SlotInfo(true, s->IsEmpty, s->IconId, s->CommandType, s->CommandId, s->KeybindHintString);
    }

    public void ExecuteSlot(int hotbar, int slot) => Enqueue(() =>
    {
        var module = RaptureHotbarModule.Instance();
        if (module == null) return;
        module->ExecuteSlotById((uint)hotbar, (uint)slot);
    });

    /// <summary>Remaining / total recast for an Action-type slot. Returns false when the slot has no cooldown running.</summary>
    public bool TryGetCooldown(in SlotInfo slot, out float remaining, out float total)
    {
        remaining = total = 0;
        if (!slot.Valid || slot.Empty) return false;

        var am = ActionManager.Instance();
        if (am == null) return false;

        ActionType type;
        var id = slot.CommandId;
        switch (slot.Type)
        {
            case RaptureHotbarModule.HotbarSlotType.Action:
                type = ActionType.Action;
                id = am->GetAdjustedActionId(id);
                break;
            case RaptureHotbarModule.HotbarSlotType.GeneralAction:
                type = ActionType.GeneralAction;
                break;
            case RaptureHotbarModule.HotbarSlotType.Item:
                type = ActionType.Item;
                break;
            default:
                return false;
        }

        total = am->GetRecastTime(type, id);
        if (total <= 0) return false;

        var elapsed = am->GetRecastTimeElapsed(type, id);
        remaining = total - elapsed;
        return remaining > 0.05f;
    }

    // ---- general actions / main commands --------------------------------------------------------------

    public void UseGeneralAction(uint id) => Enqueue(() =>
    {
        var am = ActionManager.Instance();
        if (am == null) return;
        am->UseAction(ActionType.GeneralAction, id);
    });

    public void ExecuteMainCommand(uint id) => Enqueue(() =>
    {
        var ui = UIModule.Instance();
        if (ui == null) return;
        ui->ExecuteMainCommand(id);
    });

    public bool IsMounted => Plugin.Condition[ConditionFlag.Mounted];

    public void ToggleMount() => UseGeneralAction(IsMounted ? GeneralActionDismount : GeneralActionMountRoulette);

    // ---- camera ---------------------------------------------------------------------------------------

    /// <summary>Rotate the active third-person camera by the given yaw/pitch in radians.</summary>
    public void RotateCamera(float yaw, float pitch) => Enqueue(() =>
    {
        var manager = CameraManager.Instance();
        if (manager == null) return;

        var cam = manager->GetActiveCamera();
        if (cam == null) return;

        cam->DirH += yaw;
        cam->DirV = Math.Clamp(cam->DirV + pitch, cam->DirVMin, cam->DirVMax);
    });
}
