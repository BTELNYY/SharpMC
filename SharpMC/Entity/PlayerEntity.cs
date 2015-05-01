﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Timers;
using SharpMC.Enums;
using SharpMC.Networking.Packages;
using SharpMC.Utils;
using SharpMC.Worlds;
using EntityAction = SharpMC.Enums.EntityAction;

namespace SharpMC.Entity
{
	public class Player : Entity
	{
		public Player(Level level) : base(-1 ,level)
		{
			_chunksUsed = new Dictionary<Tuple<int, int>, ChunkColumn>();
			Inventory = new PlayerInventoryManager(this);
			Level = level;

			Width = 0.6;
			Height = 1.62;
			Length = 0.6;
		}

		private readonly Dictionary<Tuple<int, int>, ChunkColumn> _chunksUsed;
		private readonly Vector2 _currentChunkPosition = new Vector2(0, 0);
		public PlayerInventoryManager Inventory; //The player's Inventory
		public string Username { get; set; } //The player's username
		public string Uuid { get; set; } // The player's UUID
		public ClientWrapper Wrapper { get; set; } //The player's associated ClientWrapper
		public Gamemode Gamemode { get; set; } //The player's gamemode
		public bool IsSpawned { get; private set; }  //Is the player spawned?
		public bool Digging { get; set; } // Is the player digging?
		private bool CanFly { get; set; } //Can the player fly?

		//Client settings
		public string Locale { get; set; }
		public byte ViewDistance { get; set; }
		public byte ChatFlags { get; set; }
		public bool ChatColours { get; set; }
		public byte SkinParts { get; set; }
		public bool ForceChunkReload { get; set; }

		//Not Sure Why Stuff
		public EntityAction LastEntityAction { get; set; }

		public void AddToList()
		{
			Level.AddPlayer(this);
		}

		public void PositionChanged(Vector3 location, float yaw = 0.0f, float pitch = 0.0f, bool onGround = false)
		{
			var originalcoordinates = KnownPosition;
			KnownPosition.Yaw = yaw;
			KnownPosition.Pitch = pitch;
			KnownPosition.Y = location.Y;
			KnownPosition.X = location.X;
			KnownPosition.Z = location.Z;
			KnownPosition.OnGround = onGround;

			SendChunksForKnownPosition();
			new EntityTeleport(Wrapper) {UniqueServerID = EntityId, Coordinates = location, OnGround = onGround, Pitch = (byte)pitch, Yaw = (byte)yaw}.Broadcast(false, this);
			//We teleport for now, Entityrelativemove will be used later on when i know what is wrong with it? :(

			//new EntityRelativeMove(Client) {Player = Client.Player, Movement = movement}.Broadcast(false, Client.Player);
		}

		public void HeldItemChanged(int slot)
		{
			Inventory.CurrentSlot = slot;
			BroadcastEquipment();
		}

		public void BroadcastEquipment()
		{
			//HeldItem
			var slotdata = Inventory.GetSlot(36 + Inventory.CurrentSlot);
			new EntityEquipment(Wrapper)
			{
				Slot = EquipmentSlot.Held,
				Item = slotdata,
				EntityId = EntityId
			}.Broadcast(false, this);

			//Helmet
			slotdata = Inventory.GetSlot(5);
			new EntityEquipment(Wrapper)
			{
				Slot = EquipmentSlot.Helmet,
				Item = slotdata,
				EntityId = EntityId
			}.Broadcast(false, this);

			//Chestplate
			slotdata = Inventory.GetSlot(6);
			new EntityEquipment(Wrapper)
			{
				Slot = EquipmentSlot.Chestplate,
				Item = slotdata,
				EntityId = EntityId
			}.Broadcast(false, this);

			//Leggings
			slotdata = Inventory.GetSlot(7);
			new EntityEquipment(Wrapper)
			{
				Slot = EquipmentSlot.Leggings,
				Item = slotdata,
				EntityId = EntityId
			}.Broadcast(false, this);

			//Boots
			slotdata = Inventory.GetSlot(8);
			new EntityEquipment(Wrapper)
			{
				Slot = EquipmentSlot.Boots,
				Item = slotdata,
				EntityId = EntityId
			}.Broadcast(false, this);
		}

		public override void OnTick(object sender, ElapsedEventArgs elapsedEventArgs)
		{
			if (IsSpawned)
			{
				if (Gamemode == Gamemode.Surival)
				{
					HealthManager.OnTick();
				}
			}
		}

		public void SetGamemode(Gamemode target)
		{
			Gamemode = target;

		}

		public void Respawn()
		{
			HealthManager.ResetHealth();
			if (Wrapper != null && Wrapper.TcpClient.Connected) new Respawn(Wrapper) {GameMode = (byte)Gamemode}.Write();
		}

		public void SendHealth()
		{
			new UpdateHealth(Wrapper).Write();
		}

		public void BroadcastEntityAnimation(Animations action)
		{
			new Animation(Wrapper){AnimationId = (byte)action, TargetPlayer = this}.Broadcast();
		}

		public void SendChunksFromPosition()
		{
			if (KnownPosition == null)
			{
				var d = Level.Generator.GetSpawnPoint();
				KnownPosition = new PlayerLocation(d.X, d.Y, d.Z);
				ViewDistance = 8;
			}
			SendChunksForKnownPosition();
		}

		private void InitializePlayer()
		{
			new PlayerPositionAndLook(Wrapper).Write();

			IsSpawned = true;
			Level.AddPlayer(this);
			Wrapper.Player.Inventory.SendToPlayer();
			if (Globals.SupportSharpMC)
			{
				new PlayerListHeaderFooter(Wrapper) {Header = "§6§l" + Globals.ProtocolName, Footer = "§eC# Powered!"}.Write();
			}
			BroadcastEquipment();
		}

		public void SendChunksForKnownPosition(bool force = false)
		{
			var centerX = (int) KnownPosition.X >> 4;
			var centerZ = (int)KnownPosition.Z >> 4;

			if (!force && IsSpawned && _currentChunkPosition == new Vector2(centerX, centerZ)) return;

			_currentChunkPosition.X = centerX;
			_currentChunkPosition.Z = centerZ;

			new Thread(() =>
			{
				var counted = 0;

				foreach (
					var chunk in
						Level.Generator.GenerateChunks((ViewDistance * 16), KnownPosition.X, KnownPosition.Z,
							_chunksUsed, this))
				{
					if (Wrapper != null && Wrapper.TcpClient.Client.Connected)
						new ChunkData(Wrapper, new MSGBuffer(Wrapper)) {Chunk = chunk}.Write();
					Thread.Yield();

					if (counted >= 5 && !IsSpawned)
					{
						InitializePlayer();
					}
					counted++;
				}
			}).Start();
		}
	}
}