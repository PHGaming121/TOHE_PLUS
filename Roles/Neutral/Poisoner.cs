using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using TOHE.Roles.Crewmate;
using UnityEngine;
using static TOHE.Translator;

namespace TOHE.Roles.Neutral;

public static class Poisoner
{
    private class PoisonedInfo(byte poisonerId, float killTimer)
    {
        public byte PoisonerId = poisonerId;
        public float KillTimer = killTimer;
    }

    private static readonly int Id = 12700;
    public static List<byte> playerIdList = [];
    private static OptionItem OptionKillDelay;
    private static float KillDelay;
    public static OptionItem CanVent;
    public static OptionItem KillCooldown;
    private static readonly Dictionary<byte, PoisonedInfo> PoisonedPlayers = [];
    public static void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.NeutralRoles, CustomRoles.Poisoner);
        KillCooldown = FloatOptionItem.Create(Id + 10, "PoisonCooldown", new(0f, 180f, 2.5f), 25f, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Poisoner])
            .SetValueFormat(OptionFormat.Seconds);
        OptionKillDelay = FloatOptionItem.Create(Id + 11, "PoisonerKillDelay", new(1f, 30f, 1f), 3f, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Poisoner])
            .SetValueFormat(OptionFormat.Seconds);
        CanVent = BooleanOptionItem.Create(Id + 12, "CanVent", true, TabGroup.NeutralRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Poisoner]);
    }
    public static void Init()
    {
        playerIdList = [];
        PoisonedPlayers.Clear();

        KillDelay = OptionKillDelay.GetFloat();
    }

    public static void Add(byte playerId)
    {
        playerIdList.Add(playerId);

        if (!AmongUsClient.Instance.AmHost) return;
        if (!Main.ResetCamPlayerList.Contains(playerId))
            Main.ResetCamPlayerList.Add(playerId);
    }

    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);
    public static void SetKillCooldown(byte id) => Main.AllPlayerKillCooldown[id] = KillCooldown.GetFloat();

    public static bool OnCheckMurder(PlayerControl killer, PlayerControl target)
    {
        if (!IsThisRole(killer.PlayerId)) return true;
        if (target.Is(CustomRoles.Bait)) return true;
        if (target.Is(CustomRoles.Guardian) && target.AllTasksCompleted()) return true;
        if (target.Is(CustomRoles.Opportunist) && target.AllTasksCompleted() && Options.OppoImmuneToAttacksWhenTasksDone.GetBool()) return false;
        if (target.Is(CustomRoles.Pestilence)) return true;
        if (target.Is(CustomRoles.Veteran) && Main.VeteranInProtect.ContainsKey(target.PlayerId)) return true;
        if (Medic.ProtectList.Contains(target.PlayerId)) return false;

        killer.SetKillCooldown();
        _ = new LateTask(() => { killer.SetKillCooldown(); }, OptionKillDelay.GetFloat());

        //誰かに噛まれていなければ登録
        if (!PoisonedPlayers.ContainsKey(target.PlayerId))
        {
            PoisonedPlayers.Add(target.PlayerId, new(killer.PlayerId, 0f));
        }
        return false;
    }

    public static void OnFixedUpdate(PlayerControl poisoner)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInTask) return;

        var poisonerID = poisoner.PlayerId;
        if (!IsThisRole(poisoner.PlayerId)) return;

        List<byte> targetList = new(PoisonedPlayers.Where(b => b.Value.PoisonerId == poisonerID).Select(b => b.Key));

        foreach (byte targetId in targetList.ToArray())
        {
            var poisonedPoisoner = PoisonedPlayers[targetId];
            if (poisonedPoisoner.KillTimer >= KillDelay)
            {
                var target = Utils.GetPlayerById(targetId);
                KillPoisoned(poisoner, target);
                PoisonedPlayers.Remove(targetId);
            }
            else
            {
                poisonedPoisoner.KillTimer += Time.fixedDeltaTime;
                PoisonedPlayers[targetId] = poisonedPoisoner;
            }
        }
    }
    public static void KillPoisoned(PlayerControl poisoner, PlayerControl target, bool isButton = false)
    {
        if (poisoner == null || target == null || target.Data.Disconnected) return;
        if (target.IsAlive())
        {
            target.Suicide(PlayerState.DeathReason.Poison, poisoner);
            Logger.Info($"Poisonerに噛まれている{target.name}を自爆させました。", "Poisoner");
            if (!isButton && poisoner.IsAlive())
            {
                RPC.PlaySoundRPC(poisoner.PlayerId, Sounds.KillSound);
                if (target.Is(CustomRoles.Trapper))
                    poisoner.TrapperKilled(target);
                poisoner.Notify(GetString("PoisonerTargetDead"));
            }
        }
        else
        {
            Logger.Info("Poisonerに噛まれている" + target.name + "はすでに死んでいました。", "Poisoner");
        }
    }
    public static void ApplyGameOptions(IGameOptions opt) => opt.SetVision(true);

    public static void OnStartMeeting()
    {
        foreach (var targetId in PoisonedPlayers.Keys)
        {
            var target = Utils.GetPlayerById(targetId);
            var poisoner = Utils.GetPlayerById(PoisonedPlayers[targetId].PoisonerId);
            KillPoisoned(poisoner, target);
        }
        PoisonedPlayers.Clear();
    }
    public static void SetKillButtonText()
    {
        HudManager.Instance.KillButton.OverrideText(GetString("PoisonerPoisonButtonText"));
    }
}
