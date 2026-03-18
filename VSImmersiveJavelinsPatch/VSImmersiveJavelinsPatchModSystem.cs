using Vintagestory.API.Common;
using Vintagestory.API.Server;
using HarmonyLib;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using System.Text;
using Vintagestory.API.MathTools;

namespace VSImmersiveJavelinsPatch;

public class VSImmersiveJavelinsPatchModSystem : ModSystem
{
    public static ICoreServerAPI? ServerAPI { get; set; }
    public static ICoreClientAPI? ClientAPI { get; set; }

    private bool isAnimating = false;
    private static bool alreadySentMessageThisAction = false;
    private readonly Dictionary<string, float> craftingStartTimes = new Dictionary<string, float>();

    private readonly int boneJavelinCraftTime = 1500;
	private readonly int fletchingCraftingTime = 1000;
    private MeshRef? _circleMesh;
    private Harmony? harmony;

    public override double ExecuteOrder() {
        return double.MaxValue;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        ServerAPI = api;

        ServerAPI.Event.RegisterGameTickListener(new Action<float>(this.OnGameTick), 50, 0);

        //This loop looks for the feather and bone items, and makes sure they have the offhand storage flag.
        foreach (Item item in api.World.Items) {
            if(item == null) { continue; }
            if (item.Code == null) { continue; }
            if (item.Code.ToString() == "game:feather" || item.Code.ToString() == "game:bone") {
                if(item.StorageFlags.HasFlag(EnumItemStorageFlags.Offhand) == false) {
                    item.StorageFlags |= EnumItemStorageFlags.Offhand;
                }
            }
        }
    }

    public override void StartClientSide(ICoreClientAPI api) {
        base.StartClientSide(api);
        ClientAPI = api;

        ClientAPI.Event.RegisterGameTickListener(new Action<float>(this.OnClientTick), 50, 0);
    }

     public override void Start(ICoreAPI api) {
        base.Start(api);
        Patch();
    }

    public override void Dispose() {
        harmony?.UnpatchAll(Mod.Info.ModID);
        base.Dispose();
    }

    private void Patch() {
        harmony = new Harmony(Mod.Info.ModID);
        if(Harmony.HasAnyPatches("immersivejavelins")) {
            harmony.UnpatchAll("immersivejavelins");
        } else {
            ServerAPI?.Logger.Warning("VS Immersive Javelins Patch WARNING: Could not find Immersive Javelins Harmony patches. This could cause issues!");
            ClientAPI?.Logger.Warning("VS Immersive Javelins Patch WARNING: Could not find Immersive Javelins Harmony patches. This could cause issues!");
        }

        if(Harmony.HasAnyPatches(Mod.Info.ModID)) return; //Avoid duplicate patches.

        var og = typeof(ItemSpear).GetMethod("OnHeldInteractStop");
        var prefix = typeof(EntityPlayer_LightHsv_Patched).GetMethod("OnHeldInteractStop_Prefix");

        harmony.Patch(og, prefix: new HarmonyMethod(prefix));

        var og2 = typeof(CollectibleObject).GetMethod("OnHeldAttackStart");
		var prefix2 = typeof(EntityPlayer_LightHsv_Patched).GetMethod("OnHeldAttackStart_Prefix");

		harmony.Patch(og2, prefix: new HarmonyMethod(prefix2));

		var og3 = typeof(ItemSpear).GetMethod("GetHeldItemInfo");
		var prefix3 = typeof(EntityPlayer_LightHsv_Patched).GetMethod("GetHeldItemInfo_Prefix");

		harmony.Patch(og3, prefix: new HarmonyMethod(prefix3));
        harmony.PatchAll();
    }

    #region Disabler Patches
    //Disables the original code in the mod.
    [HarmonyPatch(typeof(ImmersiveJavelins.ImmersiveJavelinsMod), "StartServerSide", new Type[] {typeof(ICoreServerAPI)})]
    public class StartServerSidePatch {
        public static bool Prefix() {
            return false;
        }
    }

    [HarmonyPatch(typeof(ImmersiveJavelins.ImmersiveJavelinsMod), "StartClientSide", new Type[] {typeof(ICoreClientAPI)})]
    public class StartClientSidePatch {
        public static bool Prefix() {
            return false;
        }
    }

    [HarmonyPatch(typeof(ImmersiveJavelins.ImmersiveJavelinsMod), "Start", new Type[] {typeof(ICoreAPI)})]
    public class StartPatch {
        public static bool Prefix() {
            return false;
        }
    }
    #endregion

    //Much of this code below is from Arahvin's Immersive Javelins, released with a CC0 license. It is included here so it may be recompiled for newer VS versions and be improved upon. Thank you Arahvin!
    #region OnGameTick
    private void OnGameTick(float dt) { //MFZ: This would appear to just handle the crafting mechanics.
        bool flag = ServerAPI == null;
		if (ServerAPI != null && ServerAPI.Server.CurrentRunPhase == EnumServerRunPhase.RunGame) {
			IPlayer[] allPlayers = ServerAPI.World.AllOnlinePlayers;
			
			for (int i = 0; i < allPlayers.Length; i++) {
				IPlayer player = allPlayers[i];
				IServerPlayer? serverPlayer = player as IServerPlayer;

				if (serverPlayer != null && serverPlayer.ConnectionState == EnumClientState.Playing) {
					string? playerUID = (serverPlayer != null) ? serverPlayer.PlayerUID : null;

					if (playerUID != null && serverPlayer != null) { //MFZ: Redundant but causes a warning if we don't check the player again.
						bool rightMouseDown = serverPlayer.Entity.Controls.RightMouseDown;
						if (rightMouseDown) {
							ItemSlot? itemSlot = null;
							ItemSlot? leftHandItemSlot = null;
							if (serverPlayer != null) {
								IPlayerInventoryManager inventoryManager = serverPlayer.InventoryManager;
								itemSlot = (inventoryManager != null) ? inventoryManager.ActiveHotbarSlot : null;
								leftHandItemSlot = serverPlayer.Entity != null && serverPlayer.Entity.LeftHandItemSlot != null ? serverPlayer.Entity.LeftHandItemSlot : null;
							}

							string? itemClass;
							string? itemCodePath;
							if (itemSlot == null) {
								itemClass = null;
								itemCodePath = null;
							}
							else {
								ItemStack? itemstack = itemSlot.Itemstack;
								if (itemstack == null) {
									itemClass = null;
									itemCodePath = null;
								}
								else {
									CollectibleObject collectible = itemstack.Collectible;
									if (collectible == null) {
										itemClass = null;
										itemCodePath = null;
									}
									else {
										itemClass = collectible?.Class;
										itemCodePath = itemstack?.Item?.Code?.Path;
									}
								}
							}

							if(itemClass == "ItemKnife") {
								if(leftHandItemSlot?.Itemstack?.Collectible.Code.Path == "bone") {
									bool playerCrafting = this.craftingStartTimes.ContainsKey(playerUID);
									if (!playerCrafting) {
										// Get him crafting
										serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", true);
										// Define an array of possible sounds
										string[] soundEffects = { "game:sounds/player/chalkdraw1", "game:sounds/player/chalkdraw2", "game:sounds/player/chalkdraw3" };

										// Select a random sound effect
										int randomIndex = new Random().Next(soundEffects.Length);
										string selectedSound = soundEffects[randomIndex];

										// Play the selected sound effect
										if(serverPlayer?.Entity != null) ServerAPI.World.PlaySoundAt(new AssetLocation(selectedSound), serverPlayer.Entity.Pos.X, serverPlayer.Entity.Pos.Y, serverPlayer.Entity.Pos.Z, null, true, 32f, 1f);
										this.craftingStartTimes[playerUID] = (float)ServerAPI.World.ElapsedMilliseconds;
									} else {
										float heldDuration = (float)ServerAPI.World.ElapsedMilliseconds - this.craftingStartTimes[playerUID];
										if (heldDuration >= (float)this.boneJavelinCraftTime) {
											this.UpdateCirceMesh(0.5f);

											if(serverPlayer?.Entity != null) {
                                                this.CraftJavelinHeads(serverPlayer, leftHandItemSlot, serverPlayer);
											    itemSlot?.Itemstack?.Collectible.DamageItem(ServerAPI.World, serverPlayer.Entity, itemSlot, 1);
                                            }

											this.craftingStartTimes.Remove(playerUID);
											serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", false);
										}
									}
								} else if(leftHandItemSlot?.Itemstack?.Collectible.Code.Path == "feather") {
									bool playerCrafting = this.craftingStartTimes.ContainsKey(playerUID);
									if (!playerCrafting) {
										// Get him crafting
										serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", true);
										// Define an array of possible sounds
										string[] soundEffects = { "game:sounds/player/gluerepair1", "game:sounds/player/gluerepair2", "game:sounds/player/gluerepair3", "game:sounds/player/gluerepair4" };

										// Select a random sound effect
										int randomIndex = new Random().Next(soundEffects.Length);
										string selectedSound = soundEffects[randomIndex];

										// Play the selected sound effect
										if(serverPlayer?.Entity != null) ServerAPI.World.PlaySoundAt(new AssetLocation(selectedSound), serverPlayer.Entity.Pos.X, serverPlayer.Entity.Pos.Y, serverPlayer.Entity.Pos.Z, null, true, 32f, 1f);
										this.craftingStartTimes[playerUID] = (float)ServerAPI.World.ElapsedMilliseconds;
									} else {
										float heldDuration = (float)ServerAPI.World.ElapsedMilliseconds - this.craftingStartTimes[playerUID];
										if (heldDuration >= (float)this.fletchingCraftingTime)
										{
											if(serverPlayer?.Entity != null) {
                                                this.CraftJavelinFletchings(serverPlayer, leftHandItemSlot, serverPlayer);
											    itemSlot?.Itemstack?.Collectible.DamageItem(ServerAPI.World, serverPlayer.Entity, itemSlot, 1);
                                            }
                                            
											this.craftingStartTimes.Remove(playerUID);
											serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", false);
										}
									}
								} else {
									this.craftingStartTimes.Remove(playerUID);
									serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", false);
								}
							} else {
								if(itemCodePath == "javelinhead-bone") {
									bool playerCrafting = this.craftingStartTimes.ContainsKey(playerUID);
									ItemSlot? stickSlot = (serverPlayer != null) ? this.FindItemInHotBarOrBackpack(serverPlayer, "stick") : null;
									if (!playerCrafting) {
										// Get him crafting
										if(stickSlot == null) {
											if(stickSlot == null && !alreadySentMessageThisAction && serverPlayer != null) serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("I need a stick to attach that to. I could also grab some fletching while I am at it..."), EnumChatType.Notification);
											alreadySentMessageThisAction = true;
											return;
										}
										serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", true);
										if(serverPlayer?.Entity != null) ServerAPI.World.PlaySoundAt(new AssetLocation("game:sounds/bow-draw"), serverPlayer.Entity.Pos.X, serverPlayer.Entity.Pos.Y, serverPlayer.Entity.Pos.Z, null, true, 32f, 1f);
										this.craftingStartTimes[playerUID] = (float)ServerAPI.World.ElapsedMilliseconds;
									} else {
										float heldDuration = (float)ServerAPI.World.ElapsedMilliseconds - this.craftingStartTimes[playerUID];
										if (heldDuration >= (float)this.boneJavelinCraftTime) {
											if(serverPlayer?.Entity != null) this.CraftJavelin(serverPlayer, itemSlot, stickSlot, null, null);
											this.craftingStartTimes.Remove(playerUID);
											serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", false);
										}
									}
								} else if(itemCodePath == "javelinfletching") {
									bool playerCrafting = this.craftingStartTimes.ContainsKey(playerUID);
									ItemSlot? stickSlot = (serverPlayer != null) ? this.FindItemInHotBarOrBackpack(serverPlayer, "stick") : null;
									ItemSlot? javelinheadSlot = (serverPlayer != null) ? this.FindItemInHotBarOrBackpack(serverPlayer, "javelinhead-bone"): null;
									ItemSlot? crudeJavelinSlot = (serverPlayer != null) ? this.FindItemInHotBarOrBackpack(serverPlayer, "crudejavelin-bone"): null;
									if (!playerCrafting) {
										// Get him crafting
										if((stickSlot == null || javelinheadSlot == null) && crudeJavelinSlot == null) {
											if(stickSlot == null && crudeJavelinSlot == null && !alreadySentMessageThisAction && serverPlayer != null) serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("I need a stick or a crude javelin to attach that to."), EnumChatType.Notification);
											if(javelinheadSlot == null && !alreadySentMessageThisAction && serverPlayer != null) serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("I am missing a javelin head to assemble this."), EnumChatType.Notification);
											alreadySentMessageThisAction = true;
											return;
										}
										serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", true);
										if(serverPlayer?.Entity != null) ServerAPI.World.PlaySoundAt(new AssetLocation("game:sounds/bow-draw"), serverPlayer.Entity.Pos.X, serverPlayer.Entity.Pos.Y, serverPlayer.Entity.Pos.Z, null, true, 32f, 1f);
										this.craftingStartTimes[playerUID] = (float)ServerAPI.World.ElapsedMilliseconds;
									} else {
										float heldDuration = (float)ServerAPI.World.ElapsedMilliseconds - this.craftingStartTimes[playerUID];
										if (heldDuration >= (float)this.boneJavelinCraftTime)
										{
											if(serverPlayer?.Entity != null) this.CraftJavelin(serverPlayer, javelinheadSlot, stickSlot, itemSlot, crudeJavelinSlot);
											this.craftingStartTimes.Remove(playerUID);
											serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", false);
										}
									}
								} else {
									this.craftingStartTimes.Remove(playerUID);
									serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", false);
								}
							}
						    } else {
						    	alreadySentMessageThisAction = false;
						    	this.craftingStartTimes.Remove(playerUID);
						    	serverPlayer?.Entity?.Attributes?.SetBool("isCrafting", false);
						    }
						}
					}
				}
			}
    }

    #endregion

    #region OnClientTick
        private void OnClientTick(float dt) { //MFZ: This handles the crafting animations on the client side.
			if (ClientAPI == null) return;

			var world = ClientAPI.World;
			var clientPlayer = world?.Player;
			var serverPlayer = clientPlayer as IServerPlayer;
			var entityPlayer = clientPlayer?.Entity;

            if(entityPlayer == null) return;

			// Check if item path contains "head" or "knifeblade"
			bool playerCrafting = entityPlayer.Attributes.GetBool("isCrafting");

			if (playerCrafting) {
				if (!this.isAnimating && entityPlayer != null) {
					this.StartCraftAnimation(entityPlayer);
					this.isAnimating = true;
				}
			}
			else if (this.isAnimating && entityPlayer != null) {
				this.StopCraftAnimation(entityPlayer);
				this.isAnimating = false;
			}
		}
    #endregion
    #region Helper functions
		private void StartCraftAnimation(Entity entity) {
			AnimationMetaData animationMetaData = new AnimationMetaData {
				Animation = "squeezehoneycomb",
				Code = "squeezehoneycomb",
				EaseInSpeed = 7f,
				EaseOutSpeed = 7f,
				Weight = 8f,
				BlendMode = EnumAnimationBlendMode.AddAverage,
				ElementWeight = new Dictionary<string, float> {
					{
						"UpperArmR",
						200f
					},
					{
						"LowerArmR",
						200f
					},
					{
						"UpperArmL",
						200f
					},
					{
						"LowerArmL",
						200f
					},
					{
						"ItemAnchor",
						40f
					}
				},
				ElementBlendMode = new Dictionary<string, EnumAnimationBlendMode> {
					{
						"UpperArmR",
						EnumAnimationBlendMode.AddAverage
					},
					{
						"LowerArmR",
						EnumAnimationBlendMode.AddAverage
					},
					{
						"UpperArmL",
						EnumAnimationBlendMode.AddAverage
					},
					{
						"LowerArmL",
						EnumAnimationBlendMode.AddAverage
					},
					{
						"ItemAnchor",
						EnumAnimationBlendMode.AddAverage
					}
				}
			};
			entity.AnimManager.StartAnimation(animationMetaData.Init());
		}
		private void StopCraftAnimation(Entity entity) {
			entity.AnimManager.StopAnimation("squeezehoneycomb");
		}

		private ItemSlot? FindItemInHotBarOrBackpack(IServerPlayer player, string wantedPath)
		{
			IInventory? inventory;
			IInventory? inventory2;
			if (player == null) {
				return null;
			}

			IPlayerInventoryManager inventoryManager = player.InventoryManager;
			inventory = ((inventoryManager != null) ? inventoryManager.GetHotbarInventory() : null);
			inventory2 = ((inventoryManager != null) ? inventoryManager.GetOwnInventory("backpack") : null);

			if (inventory == null) {
				return null;
			}

			for (int i = 0; i < inventory.Count; i++) {
				ItemSlot itemSlot = inventory[i];

				if (itemSlot == null) {
					continue;
				}
				ItemStack? itemstack = itemSlot.Itemstack;
				if (itemstack == null) {
					continue;
				}

				CollectibleObject collectible = itemstack.Collectible;
				if (collectible == null) {
					continue;
				}

				AssetLocation code = collectible.Code;
				if (code == null) {
					continue;
				}

				string path = code.Path;
				if(path != null && path == wantedPath) {
					return itemSlot;
				}
			}

			if(inventory == null) return null;
            if(inventory2 == null) return null;

			for (int i = 0; i < inventory2.Count; i++) {
				ItemSlot itemSlot = inventory2[i];

				if (itemSlot == null) {
					continue;
				}
				ItemStack? itemstack = itemSlot.Itemstack;
				if (itemstack == null) {
					continue;
				}

				CollectibleObject collectible = itemstack.Collectible;
				if (collectible == null) {
					continue;
				}

				AssetLocation code = collectible.Code;
				if (code == null) {
					continue;
				}

				string path = code.Path;
				if(path != null && path == wantedPath) {
					return itemSlot;
				}
			}
			return null;
		}
		private void CraftJavelinHeads(IServerPlayer player, ItemSlot leftHandItemSlot, IServerPlayer serverPlayer) {
			if(leftHandItemSlot != null && leftHandItemSlot.Itemstack != null && ServerAPI != null) {
				leftHandItemSlot.TakeOut(1);

				Item? javelinHead = ServerAPI.World.GetItem(new AssetLocation("immersivejavelins:javelinhead-bone"));
                ItemStack javelinHeadStack = new ItemStack(javelinHead, 2);
				bool itemGiven = player.InventoryManager.TryGiveItemstack(javelinHeadStack, false);
				if(itemGiven) {
					leftHandItemSlot.MarkDirty();
				} else {
					serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("My invetory is full... I can't craft that."), EnumChatType.Notification);
				}
			}
		}

		private void CraftJavelinFletchings(IServerPlayer player, ItemSlot leftHandItemSlot, IServerPlayer serverPlayer) {
			if(leftHandItemSlot != null && leftHandItemSlot.Itemstack != null && ServerAPI != null) {
				leftHandItemSlot.TakeOut(1);

				Item? javelingFletchings = ServerAPI.World.GetItem(new AssetLocation("immersivejavelins:javelinfletching"));
				ItemStack javelingFletchingsStack = new ItemStack(javelingFletchings, 1);
				bool itemGiven = player.InventoryManager.TryGiveItemstack(javelingFletchingsStack, false);
				if(itemGiven) {
					leftHandItemSlot.MarkDirty();
				} else {
					serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("My invetory is full... I can't craft that."), EnumChatType.Notification);
				}
			}
		}
    
    	private void CraftJavelin(IServerPlayer player, ItemSlot? javelinHeadSlot, ItemSlot? stickSlot, ItemSlot? fletchingSlot, ItemSlot? crudeJavelinSlot) {
			// Check if the player has the necessary items in the slots
			bool hasJavelinHead = javelinHeadSlot?.Itemstack != null;
			bool hasStick = stickSlot?.Itemstack != null;
			bool hasFletching = fletchingSlot?.Itemstack != null;
			bool hasCrudeJavelin = crudeJavelinSlot?.Itemstack != null;
            if(ServerAPI == null) return;

			// Determine the type of javelin to create
			if (hasFletching && (hasCrudeJavelin || (hasStick && hasJavelinHead))) {
				// Create bone javelin
				Item? boneJavelin = ServerAPI.World.GetItem(new AssetLocation("immersivejavelins:javelin-bone"));
				ItemStack boneJavelinStack = new ItemStack(boneJavelin, 1);

				// Attempt to give the bone javelin to the player
				if (player.InventoryManager.TryGiveItemstack(boneJavelinStack, false)) {
					if (hasCrudeJavelin) {
                        crudeJavelinSlot?.TakeOut(1);
						crudeJavelinSlot?.MarkDirty();
					}
					else {
						javelinHeadSlot?.TakeOut(1);
						stickSlot?.TakeOut(1);
						javelinHeadSlot?.MarkDirty();
						stickSlot?.MarkDirty();
					}
					fletchingSlot?.TakeOut(1);
					fletchingSlot?.MarkDirty();
				}
				else {
					player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("My invetory is full... I can't craft that."), EnumChatType.Notification);
				}
			}
			else if (hasJavelinHead && hasStick && !hasFletching) {
				// Create crude javelin
				Item? crudeJavelin = ServerAPI.World.GetItem(new AssetLocation("immersivejavelins:crudejavelin-bone"));
				ItemStack crudeJavelinStack = new ItemStack(crudeJavelin, 1);

				// Attempt to give the crude javelin to the player
				if (player.InventoryManager.TryGiveItemstack(crudeJavelinStack, false)) {
					javelinHeadSlot?.TakeOut(1);
					stickSlot?.TakeOut(1);
					javelinHeadSlot?.MarkDirty();
					stickSlot?.MarkDirty();
				}
				else {
					player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("My invetory is full... I can't craft that."), EnumChatType.Notification);
				}
			}
			else {
				ServerAPI.World.Logger.Event("Player lacks required items to craft javelin.");
			}
		}

		private void UpdateCirceMesh(float progress) { //MFZ: I'll be honest, I don't know what this is doing but I'm pretty sure it's client side rendering stuff.
            if(ClientAPI == null) return; //MFZ: Skip this function on the server.
			int num = 1 + (int)Math.Ceiling((double)(16f * progress));
			MeshData meshData = new MeshData(num * 2, num * 6, false, false, true, false);
			for (int i = 0; i < num; i++) {
				double num2 = (double)Math.Min(progress, (float)i * 0.0625f) * 3.141592653589793 * 2.0;
				float num3 = (float)Math.Sin(num2);
				float num4 = -(float)Math.Cos(num2);
				meshData.AddVertexSkipTex(num3, num4, 0f, -1);
				meshData.AddVertexSkipTex(num3 * 0.75f, num4 * 0.75f, 0f, -1);
				if (i > 0) {
					meshData.AddIndices(new int[] {
						i * 2 - 2,
						i * 2 - 1,
						i * 2
					});
					meshData.AddIndices(new int[] {
						i * 2,
						i * 2 - 1,
						i * 2 + 1
					});
				}
			}
			if (this._circleMesh != null) {
				ClientAPI.Render.UpdateMesh(this._circleMesh, meshData);
				return;
			}
			this._circleMesh = ClientAPI.Render.UploadMesh(meshData);
		}
}
    #endregion
    #region EntityPlayer_LightHsv_Patched
    public class EntityPlayer_LightHsv_Patched {

		public static bool GetHeldItemInfo_Prefix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo) {
			if (inSlot == null) return false;
            if (inSlot.Itemstack == null) return false;
            if (inSlot.Itemstack.Collectible.Code.Domain != "immersivejavelins") return true;

            float damage = 1.5f;
            float breakChanceOnImpact = 0f;

            if (inSlot.Itemstack.Collectible.Attributes != null) {
                damage = inSlot.Itemstack.Collectible.Attributes["damage"].AsFloat(0);
                breakChanceOnImpact = inSlot.Itemstack.Collectible.Attributes["breakChanceOnImpact"].AsFloat(0.5f);
            }

            dsc.AppendLine(damage + Lang.Get("piercing-damage-thrown"));
            dsc.AppendLine(Lang.Get("breakchanceonimpact", (int)(breakChanceOnImpact * 100)));

			dsc.AppendLine("\n" + inSlot.Itemstack.Collectible.GetItemDescText());
			return false;
        }

		public static bool OnHeldAttackStart_Prefix(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling) {
			if(slot != null) {
                if(slot.Itemstack != null) {
                    if (slot.Itemstack.Collectible.Code.Domain != "immersivejavelins") return true;
                }
            }
			handling = EnumHandHandling.PreventDefault;
			return false;
		}

        public static bool OnHeldInteractStop_Prefix(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel) 
		{
			if(slot.Itemstack == null) {
                return true;
            }

            if (slot.Itemstack.Collectible.Code.Domain != "immersivejavelins") return true;
			if (byEntity.Attributes.GetInt("aimingCancel") == 1) return false;

			CollectibleObject co = slot.Itemstack.Collectible;

			byEntity.Attributes.SetInt("aiming", 0);
			byEntity.StopAnimation("aim");

			if (secondsUsed < 0.35f) return false;

			float damage = slot.Itemstack.Collectible.Attributes?["damage"].AsFloat(1.5f) ?? 1.5f;
			VSImmersiveJavelinsPatchModSystem.ClientAPI?.World.AddCameraShake(0.17f);
            

			// Take out one item from the stack only once here

			IPlayer? byPlayer = null;
			if (byEntity is EntityPlayer) byPlayer = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);

			byEntity.World.PlaySoundAt(new AssetLocation("sounds/player/throw"), byEntity, byPlayer, false, 8);

			EntityProperties? type = byEntity.World.GetEntityType(new AssetLocation(slot.Itemstack?.Collectible?.Attributes?["spearEntityCode"].AsString()));
			EntityProjectile? enpr = byEntity.World.ClassRegistry.CreateEntity(type) as EntityProjectile;
			ItemStack stack = slot.TakeOut(1);
			slot.MarkDirty();

            if(enpr != null) {
                enpr.FiredBy = byEntity;
                enpr.Damage = damage;
                enpr.ProjectileStack = stack;

                // Set break chance directly on projectile for impact handling
			    enpr.DropOnImpactChance = 1 - slot.Itemstack?.Collectible?.Attributes?["breakChanceOnImpact"].AsFloat() ?? 0.2f;
			    enpr.DamageStackOnImpact = false;
                enpr.Collectible = true; //MFZ: For some reason this needs to be set now in 1.22.
            }
			
			// Motion and velocity setup
			float acc = 1 - byEntity.Attributes.GetFloat("aimingAccuracy", 0);
			double rndpitch = byEntity.WatchedAttributes.GetDouble("aimingRandPitch", 1) * acc * 0.75;
			double rndyaw = byEntity.WatchedAttributes.GetDouble("aimingRandYaw", 1) * acc * 0.75;
			Vec3d pos = byEntity.Pos.XYZ.Add(0, byEntity.LocalEyePos.Y - 0.2, 0);
			Vec3d aheadPos = pos.AheadCopy(1, byEntity.Pos.Pitch + rndpitch, byEntity.Pos.Yaw + rndyaw);
			Vec3d velocity = (aheadPos - pos) * 0.8;
			Vec3d spawnPos = byEntity.Pos.BehindCopy(0.21).XYZ.Add(byEntity.LocalEyePos.X, byEntity.LocalEyePos.Y - 0.2, byEntity.LocalEyePos.Z);
			
            if(enpr != null) {
                enpr.Pos.SetPosWithDimension(spawnPos);
                enpr.Pos.Motion.Set(velocity);

                enpr.Pos.SetFrom(enpr.Pos);
                enpr.World = byEntity.World;
                //enpr.SetRotation();
                enpr.SetRotationFromMotion();
            
                byEntity.World.SpawnEntity(enpr);
                byEntity.StartAnimation("throw");
            }

			if (byEntity is EntityPlayer) co.RefillSlotIfEmpty(slot, byEntity, (itemstack) => itemstack.Collectible is ItemSpear);

            var pitch = (byEntity as EntityPlayer)?.talkUtil.pitchModifier;
            if(pitch == null) pitch = 0.5f; //MFZ: Fall back to 0.5 if null.
            float randVal = (VSImmersiveJavelinsPatchModSystem.ServerAPI != null) ? (float)VSImmersiveJavelinsPatchModSystem.ServerAPI.World.Rand.NextDouble() : 0.5f; //MFZ: Fall back to 0.5 if the random number function cannot be used.
            float pitchVal = (float)(pitch * 0.9f + randVal * 0.2f);
            if (byPlayer != null && VSImmersiveJavelinsPatchModSystem.ClientAPI != null) byPlayer.Entity.World.PlaySoundAt(new AssetLocation("sounds/player/strike"), byPlayer.Entity, byPlayer, pitchVal, 16, 0.35f); //TODO: Make sure all sounds are still working!
			return false;
		}
	}
    #endregion
