using Vintagestory.API.Common;
using Vintagestory.API.Server;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Vintagestory.API.Common.Entities;

namespace VSImmersiveJavelinsPatch;

public class VSImmersiveJavelinsPatchModSystem : ModSystem
{
    private static ICoreServerAPI? ServerAPI { get; set; }
    private Harmony? harmony;
    public override bool ShouldLoad(EnumAppSide side) { //Not sure if I should be using IJModSystem yet.
        return side == EnumAppSide.Server;
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        ServerAPI = api;

        harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll();
    }

    public override void Dispose() {
        harmony?.UnpatchAll(Mod.Info.ModID);
        base.Dispose();
    }

    [HarmonyPatch(typeof(ImmersiveJavelins.ImmersiveJavelinsMod), "UpdateCirceMesh", new Type[] {typeof(float)})]
    public class UpdateCirceMeshPatch {
        public static bool Prefix() {
            //This just makes the server skip the UpdateCirceMesh method, which presumably only needs to run on the client.
            return false;
        }
    }

    [HarmonyPatch(typeof(ImmersiveJavelins.EntityPlayer_LightHsv_Patched), "OnHeldInteractStop_Prefix", new Type[] {typeof(float), typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection)})]
    public class OnHeldInteractStop_PrefixPatch {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++) {
                if(codes[i] != null) {
                    if(codes[i].operand != null) {
                        if (codes[i].operand.ToString() == "sounds/player/strike" && ServerAPI != null) {
                            if(codes[i-3].opcode.ToString() == "ldloc.2") {
                                Label label = generator.DefineLabel(); //Define a label
                                codes[i-3].opcode = OpCodes.Br_S; //Change the start of the method stack to a jump to after the method.
                                codes[i-3].operand = label; //Assign label to jump to.
                                codes[i+19].labels.Add(label); //Assign the label to after the method that plays the sound.
                                //This code makes the server side skip byPlayer.Entity.World.PlaySoundAt(new AssetLocation("sounds/player/strike"), byPlayer.Entity, byPlayer, pitch * 0.9f + (float)ImmersiveJavelinsMod.capi.World.Rand.NextDouble() * 0.2f, 16, 0.35f);
                            }
                        }
                    }
                }
            }
            return codes.AsEnumerable();
        }
    }

    [HarmonyPatch(typeof(ImmersiveJavelins.ImmersiveJavelinsMod), "OnGameTick", new Type[] {typeof(float)})]
    public class onGameTickPatch {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            var codes = new List<CodeInstruction>(instructions);
            var localPlayer = generator.DeclareLocal(typeof(IPlayer));
            for (int i = 0; i < codes.Count; i++) {
                if(codes[i] != null) {
                    if(codes[i].operand != null) {
                        if (codes[i].operand.ToString() == "isCrafting" && ServerAPI != null) {
                            if(codes[i-5].operand.ToString() == "Vintagestory.API.Client.ICoreClientAPI capi") {
                                codes[i-5].opcode = OpCodes.Nop;
                                codes[i-5].operand = null; //Replacing these two calls with a no operation just in case, rather than removing them.
                                codes[i-4].opcode = OpCodes.Nop;
                                codes[i-4].operand = null; //Replacing these two calls with a no operation just in case, rather than removing them.
                                codes[i-3].opcode = OpCodes.Ldloc_S;
                                codes[i-3].operand = 2; //Use the IServerPlayer, which is stored at index 2 (IPlayer would've also worked, but it's not used in the IL code.)
                                codes[i-2].opcode = OpCodes.Callvirt;
                                codes[i-2].operand = AccessTools.Method(localPlayer.LocalType, "get_Entity"); //Get the entity of the player.
                                codes[i-1].opcode = OpCodes.Ldfld;
                                codes[i-1].operand = AccessTools.Field(typeof(Entity), "Attributes"); //Get the attributes of the player entity.
                                //Essentially, this replaces the capi.World.Player.Entity.Attributes.SetBool("isCrafting", [boolean value]); calls with serverPlayer.Entity.Attributes.SetBool("isCrafting", [boolean value]);
                                //This is my first time writing a transpiler patch, so I have no idea if I did this in the best or easiest way.
                            }
                        }
                    }
                }
            }
            return codes.AsEnumerable();
        }
    }
}
