﻿using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using InnerNet;

namespace TOHE;

// 来源：https://github.com/Yumenopai/TownOfHost_Y
public static class DarkHide
{
    public static readonly int Id = 12900;
    public static List<byte> playerIdList = [];

    public static OptionItem KillCooldown;
    public static OptionItem HasImpostorVision;
    public static OptionItem CanCountNeutralKiller;
    public static OptionItem CanVent;
    public static OptionItem SnatchesWin;

    public static Dictionary<byte, float> CurrentKillCooldown = [];
    public static Dictionary<byte, bool> IsWinKill = [];

    public static void SetupCustomOption()
    {
        Options.SetupSingleRoleOptions(Id, TabGroup.NeutralRoles, CustomRoles.DarkHide, 1);
        KillCooldown = FloatOptionItem.Create(Id + 10, "KillCooldown", new(0f, 180f, 2.5f), 30f, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.DarkHide])
            .SetValueFormat(OptionFormat.Seconds);
        CanVent = BooleanOptionItem.Create(Id + 14, "CanVent", true, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.DarkHide]);
        HasImpostorVision = BooleanOptionItem.Create(Id + 11, "ImpostorVision", false, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.DarkHide]);
        CanCountNeutralKiller = BooleanOptionItem.Create(Id + 12, "CanCountNeutralKiller", false, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.DarkHide]);
        SnatchesWin = BooleanOptionItem.Create(Id + 13, "SnatchesWin", false, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.DarkHide]);

    }
    public static void Init()
    {
        playerIdList = [];
        CurrentKillCooldown = [];
        IsWinKill = [];
    }
    public static void Add(byte playerId)
    {
        playerIdList.Add(playerId);
        CurrentKillCooldown.Add(playerId, KillCooldown.GetFloat());
        IsWinKill[playerId] = false;

        Utils.GetPlayerById(playerId)?.DRpcSetKillCount();

        if (!AmongUsClient.Instance.AmHost) return;
        if (!Main.ResetCamPlayerList.Contains(playerId))
            Main.ResetCamPlayerList.Add(playerId);
    }
    public static bool IsEnable => playerIdList.Count > 0;

    public static void ReceiveRPC(MessageReader msg)
    {
        byte DarkHiderId = msg.ReadByte();
        bool IsKillerKill = msg.ReadBoolean();
        if (!IsWinKill.TryAdd(DarkHiderId, false))
            IsWinKill[DarkHiderId] = IsKillerKill;

        Logger.Info($"Player{DarkHiderId}:ReceiveRPC", "DarkHide");
    }
    public static void DRpcSetKillCount(this PlayerControl player)
    {
        if (!IsEnable || !Utils.DoRPC || !AmongUsClient.Instance.AmHost) return;
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetDarkHiderKillCount, SendOption.Reliable);
        writer.Write(player.PlayerId);
        writer.Write(IsWinKill[player.PlayerId]);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    public static void SetKillCooldown(byte id) => Main.AllPlayerKillCooldown[id] = CurrentKillCooldown[id];
    public static bool CanUseKillButton(PlayerControl player) => !player.Data.IsDead;

    public static void ApplyGameOptions(IGameOptions opt) => opt.SetVision(HasImpostorVision.GetBool());

    public static void OnCheckMurder(PlayerControl killer, PlayerControl Ktarget)
    {
        var targetRole = Ktarget.GetCustomRole();
        var succeeded = targetRole.IsImpostor();
        if (CanCountNeutralKiller.GetBool() && !Ktarget.Is(CustomRoles.Arsonist) && !Ktarget.Is(CustomRoles.Revolutionist))
        {
            succeeded = succeeded || Ktarget.IsNeutralKiller();
        }
        if (succeeded && SnatchesWin.GetBool())
            IsWinKill[killer.PlayerId] = true;

        killer.DRpcSetKillCount();
        MessageWriter SabotageFixWriter = AmongUsClient.Instance.StartRpcImmediately(ShipStatus.Instance.NetId, (byte)RpcCalls.UpdateSystem, SendOption.Reliable, killer.GetClientId());
        SabotageFixWriter.Write((byte)SystemTypes.Electrical);
        SabotageFixWriter.WriteNetObject(killer);
        AmongUsClient.Instance.FinishRpcImmediately(SabotageFixWriter);

        foreach (PlayerControl target in Main.AllPlayerControls)
        {
            if (target.PlayerId == killer.PlayerId || target.Data.Disconnected)
                continue;
            SabotageFixWriter = AmongUsClient.Instance.StartRpcImmediately(ShipStatus.Instance.NetId, (byte)RpcCalls.UpdateSystem, SendOption.Reliable, target.GetClientId());
            SabotageFixWriter.Write((byte)SystemTypes.Electrical);
            SabotageFixWriter.WriteNetObject(target);
            AmongUsClient.Instance.FinishRpcImmediately(SabotageFixWriter);
        }
    }

}
