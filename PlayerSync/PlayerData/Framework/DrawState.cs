using System;
using System.Collections.Generic;
using System.Text;

namespace MareSynchronos.PlayerData
{
    public readonly record struct CharacterDrawState(DrawState State, DrawCondition Condition);
    public readonly record struct ObjectTableCharacter(string Name, ushort ObjectIndex, ulong GameObjectId, ulong ContentId = 0, bool GPoseOnly = false);
    public readonly record struct CharacterVisibilityUpdate(string Name, ulong ContentId, bool Visible, bool Ready);
    public readonly record struct FrameworkCharacterChange(string Ident, CharacterVisibilityUpdate Previous, CharacterVisibilityUpdate Current);

    public enum DrawState
    {
        Missing,
        Loading,
        Ready
    }

    public enum DrawCondition
    {
        None,
        NotRenderable,
        ObjectZero,
        DrawObjectZero,
        CharacterBaseZero,
        ModelInSlotLoaded,
        ModelFilesInSlotLoaded,
        Zoning
    }

    public enum DrawWaitResult
    {
        Ready,
        Missing,
        TimedOut
    }
}
