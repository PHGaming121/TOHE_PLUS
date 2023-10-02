using HarmonyLib;
using Hazel;
using System;
using System.Linq;
using TOHE.Modules;
using TOHE.Roles.Crewmate;
using TOHE.Roles.Impostor;
using static TOHE.Translator;

namespace TOHE;
/*
 * HUGE THANKS TO
 * ImaMapleTree / 단풍잎
 * FOR THE CODE
 *
 * THIS IS JUST SMALL FOR NOW BUT MAY EVENTUALLY BE BIG
*/

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.TryPet))]
class LocalPetPatch
{
    public static void Prefix(PlayerControl __instance)
    {
        if (!Options.UsePets.GetBool()) return;
        if (!(AmongUsClient.Instance.AmHost && AmongUsClient.Instance.AmClient)) return;
        __instance.petting = true;
        if (GameStates.IsLobby) return;
        ExternalRpcPetPatch.Prefix(__instance.MyPhysics, 51);
    }

    public static void Postfix(PlayerControl __instance)
    {
        if (!Options.UsePets.GetBool()) return;
        if (!(AmongUsClient.Instance.AmHost && AmongUsClient.Instance.AmClient)) return;
        __instance.petting = false;
    }
}

[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.HandleRpc))]
class ExternalRpcPetPatch
{
    public static void Prefix(PlayerPhysics __instance, [HarmonyArgument(0)] byte callId)
    {
        if (!Options.UsePets.GetBool()) return;
        if (!AmongUsClient.Instance.AmHost) return;
        var rpcType = callId == 51 ? RpcCalls.Pet : (RpcCalls)callId;
        if (rpcType != RpcCalls.Pet) return;

        PlayerControl pc = __instance.myPlayer;

        if (callId == 51 && pc.GetCustomRole().PetActivatedAbility() && GameStates.IsInGame) __instance.CancelPet();
        if (callId != 51)
        {
            if (AmongUsClient.Instance.AmHost && pc.GetCustomRole().PetActivatedAbility() && GameStates.IsInGame)
                __instance.CancelPet();
            foreach (PlayerControl player in PlayerControl.AllPlayerControls)
                AmongUsClient.Instance.FinishRpcImmediately(AmongUsClient.Instance.StartRpcImmediately(__instance.NetId, 50, SendOption.None, player.GetClientId()));
        }

        Logger.Info($"Player {pc.GetNameWithRole()} has Pet", "RPCDEBUG");

        OnPetUse(pc);
    }
    public static void OnPetUse(PlayerControl pc)
    {
        if (pc == null) return;

        switch (pc.GetCustomRole())
        {
            case CustomRoles.Doormaster:
                if (Main.DoormasterCD.ContainsKey(pc.PlayerId))
                {
                    pc.Notify(GetString("AbilityOnCooldown"));
                    break;
                }
                Doormaster.OnEnterVent(pc);
                pc.RpcResetAbilityCooldown();
                break;
            case CustomRoles.Tether:
                if (Main.TetherCD.ContainsKey(pc.PlayerId))
                {
                    pc.Notify(GetString("AbilityOnCooldown"));
                    break;
                }
                Tether.OnEnterVent(pc, 0, true);
                pc.RpcResetAbilityCooldown();
                break;
            case CustomRoles.Mayor:
                if (Main.MayorUsedButtonCount.TryGetValue(pc.PlayerId, out var count) && count < Options.MayorNumOfUseButton.GetInt() && !Main.MayorCD.ContainsKey(pc.PlayerId))
                {
                    pc?.ReportDeadBody(null);
                }
                break;
            case CustomRoles.Paranoia:
                if (Main.ParaUsedButtonCount.TryGetValue(pc.PlayerId, out var count2) && count2 < Options.ParanoiaNumOfUseButton.GetInt() && !Main.ParanoiaCD.ContainsKey(pc.PlayerId))
                {
                    Main.ParaUsedButtonCount[pc.PlayerId] += 1;
                    if (AmongUsClient.Instance.AmHost)
                    {
                        _ = new LateTask(() =>
                        {
                            Utils.SendMessage(GetString("SkillUsedLeft") + (Options.ParanoiaNumOfUseButton.GetInt() - Main.ParaUsedButtonCount[pc.PlayerId]).ToString(), pc.PlayerId);
                        }, 4.0f, "Skill Remain Message");
                    }
                    pc?.NoCheckStartMeeting(pc?.Data);
                }
                break;
            case CustomRoles.Veteran:
                if (Main.VeteranNumOfUsed[pc.PlayerId] >= 1)
                {
                    if (Main.VeteranCD.ContainsKey(pc.PlayerId))
                    {
                        pc.Notify(GetString("AbilityOnCooldown"));
                        break;
                    }
                    Main.VeteranInProtect.Remove(pc.PlayerId);
                    Main.VeteranInProtect.Add(pc.PlayerId, Utils.GetTimeStamp(DateTime.Now));
                    Main.VeteranNumOfUsed[pc.PlayerId] -= 1;
                    //pc.RpcGuardAndKill(pc);
                    pc.RPCPlayCustomSound("Gunload");
                    pc.Notify(GetString("VeteranOnGuard"), Options.VeteranSkillDuration.GetFloat());
                    Main.VeteranCD.TryAdd(pc.PlayerId, Utils.GetTimeStamp());
                    pc.RpcResetAbilityCooldown();
                    pc.MarkDirtySettings();
                }
                else
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                break;
            case CustomRoles.Grenadier:
                if (Main.GrenadierNumOfUsed[pc.PlayerId] >= 1)
                {
                    if (Main.GrenadierCD.ContainsKey(pc.PlayerId))
                    {
                        pc.Notify(GetString("AbilityOnCooldown"));
                        break;
                    }
                    if (pc.Is(CustomRoles.Madmate))
                    {
                        Main.MadGrenadierBlinding.Remove(pc.PlayerId);
                        Main.MadGrenadierBlinding.Add(pc.PlayerId, Utils.GetTimeStamp());
                        Main.AllPlayerControls.Where(x => x.IsModClient()).Where(x => !x.GetCustomRole().IsImpostorTeam() && !x.Is(CustomRoles.Madmate)).Do(x => x.RPCPlayCustomSound("FlashBang"));
                    }
                    else
                    {
                        Main.GrenadierBlinding.Remove(pc.PlayerId);
                        Main.GrenadierBlinding.Add(pc.PlayerId, Utils.GetTimeStamp());
                        Main.AllPlayerControls.Where(x => x.IsModClient()).Where(x => x.GetCustomRole().IsImpostor() || (x.GetCustomRole().IsNeutral() && Options.GrenadierCanAffectNeutral.GetBool())).Do(x => x.RPCPlayCustomSound("FlashBang"));
                    }
                    //pc.RpcGuardAndKill(pc);
                    pc.RPCPlayCustomSound("FlashBang");
                    pc.Notify(GetString("GrenadierSkillInUse"), Options.GrenadierSkillDuration.GetFloat());
                    Main.GrenadierCD.TryAdd(pc.PlayerId, Utils.GetTimeStamp());
                    pc.RpcResetAbilityCooldown();
                    Main.GrenadierNumOfUsed[pc.PlayerId] -= 1;
                    Utils.MarkEveryoneDirtySettingsV3();
                }
                else
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                break;
            case CustomRoles.Lighter:
                if (Main.LighterNumOfUsed[pc.PlayerId] >= 1)
                {
                    if (Main.LighterCD.ContainsKey(pc.PlayerId))
                    {
                        pc.Notify(GetString("AbilityOnCooldown"));
                        break;
                    }
                    Main.Lighter.Remove(pc.PlayerId);
                    Main.Lighter.Add(pc.PlayerId, Utils.GetTimeStamp());
                    pc.Notify(GetString("LighterSkillInUse"), Options.LighterSkillDuration.GetFloat());
                    Main.LighterCD.TryAdd(pc.PlayerId, Utils.GetTimeStamp());
                    pc.RpcResetAbilityCooldown();
                    Main.LighterNumOfUsed[pc.PlayerId] -= 1;
                    pc.MarkDirtySettings();
                }
                else
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                break;
            case CustomRoles.SecurityGuard:
                if (Main.SecurityGuardNumOfUsed[pc.PlayerId] >= 1)
                {
                    if (Main.SecurityGuardCD.ContainsKey(pc.PlayerId))
                    {
                        pc.Notify(GetString("AbilityOnCooldown"));
                        break;
                    }
                    Main.BlockSabo.Remove(pc.PlayerId);
                    Main.BlockSabo.Add(pc.PlayerId, Utils.GetTimeStamp());
                    pc.Notify(GetString("SecurityGuardSkillInUse"), Options.SecurityGuardSkillDuration.GetFloat());
                    Main.SecurityGuardCD.TryAdd(pc.PlayerId, Utils.GetTimeStamp());
                    pc.RpcResetAbilityCooldown();
                    Main.SecurityGuardNumOfUsed[pc.PlayerId] -= 1;
                }
                else
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                break;
            case CustomRoles.DovesOfNeace:
                if (Main.DovesOfNeaceNumOfUsed[pc.PlayerId] < 1)
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                else if (Main.DovesOfNeaceCD.ContainsKey(pc.PlayerId))
                {
                    pc.Notify(GetString("AbilityOnCooldown"));
                }
                else
                {
                    Main.DovesOfNeaceNumOfUsed[pc.PlayerId] -= 1;
                    //pc.RpcGuardAndKill(pc);
                    Main.AllAlivePlayerControls.Where(x =>
                    pc.Is(CustomRoles.Madmate) ?
                    (x.CanUseKillButton() && x.GetCustomRole().IsCrewmate()) :
                    x.CanUseKillButton()
                    ).Do(x =>
                    {
                        x.RPCPlayCustomSound("Dove");
                        x.ResetKillCooldown();
                        x.SetKillCooldown();
                        if (x.Is(CustomRoles.SerialKiller))
                        { SerialKiller.OnReportDeadBody(); }
                        x.Notify(Utils.ColorString(Utils.GetRoleColor(CustomRoles.DovesOfNeace), GetString("DovesOfNeaceSkillNotify")));
                    });
                    pc.RPCPlayCustomSound("Dove");
                    pc.Notify(string.Format(GetString("DovesOfNeaceOnGuard"), Main.DovesOfNeaceNumOfUsed[pc.PlayerId]));
                    Main.DovesOfNeaceCD.TryAdd(pc.PlayerId, Utils.GetTimeStamp());
                    pc.RpcResetAbilityCooldown();
                }
                break;
            case CustomRoles.Alchemist:
                Alchemist.OnEnterVent(pc, 0, true);
                pc.RpcResetAbilityCooldown();
                break;
            case CustomRoles.TimeMaster:
                if (Main.TimeMasterNumOfUsed[pc.PlayerId] >= 1)
                {
                    if (Main.TimeMasterCD.ContainsKey(pc.PlayerId))
                    {
                        pc.Notify(GetString("AbilityOnCooldown"));
                        break;
                    }
                    Main.TimeMasterNumOfUsed[pc.PlayerId] -= 1;
                    Main.TimeMasterInProtect.Remove(pc.PlayerId);
                    Main.TimeMasterNumOfUsed[pc.PlayerId] -= 1;
                    Main.TimeMasterInProtect.Add(pc.PlayerId, Utils.GetTimeStamp());
                    //if (!pc.IsModClient()) pc.RpcGuardAndKill(pc);
                    pc.Notify(GetString("TimeMasterOnGuard"), Options.TimeMasterSkillDuration.GetFloat());
                    Main.TimeMasterCD.TryAdd(pc.PlayerId, Utils.GetTimeStamp());
                    pc.RpcResetAbilityCooldown();
                    foreach (var player in Main.AllPlayerControls)
                    {
                        if (Main.TimeMasterBackTrack.ContainsKey(player.PlayerId))
                        {
                            var position = Main.TimeMasterBackTrack[player.PlayerId];
                            Utils.TP(player.NetTransform, position);
                            if (pc != player)
                                player?.MyPhysics?.RpcBootFromVent(player.PlayerId);
                            Main.TimeMasterBackTrack.Remove(player.PlayerId);
                        }
                        else
                        {
                            Main.TimeMasterBackTrack.Add(player.PlayerId, player.GetTruePosition());
                        }
                    }
                }
                else
                {
                    pc.Notify(GetString("OutOfAbilityUsesDoMoreTasks"));
                }
                break;

        }
    }
}