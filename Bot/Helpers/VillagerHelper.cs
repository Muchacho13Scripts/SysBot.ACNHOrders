﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NHSE.Core;
using NHSE.Villagers;
using SysBot.Base;
using ACNHMobileSpawner;
using System.Threading;

namespace SysBot.ACNHOrders
{
    public class VillagerHelper
    {
        const int villagerFlagStart = 0x1267c;
        const int villagerFlagSizeBytes = 0x200;
        public const int ExpectedHouseSize = VillagerHouse.SIZE * 10;

        private readonly IConsoleConnectionAsync? Connection;
        private readonly CrossBot? Bot;

        private readonly List<VillagerHouse>? VillagerHouses;
        private readonly List<Villager2>? VillagerShells;

        public int LastInjectedIndex { get; set; } = 0;

        public VillagerHelper(CrossBot bot, VillagerHouse[] houses, Villager2[] shells)
        {
            Bot = bot;
            Connection = Bot.Connection;

            VillagerHouses = new List<VillagerHouse>(houses);
            VillagerShells = new List<Villager2>(shells);
        }

        public VillagerHelper() { }

        public static VillagerHelper Empty => new VillagerHelper();

        public static async Task<VillagerHelper> GenerateHelper(CrossBot bot, CancellationToken token)
        {
            LogUtil.LogInfo("Fetching villager data, please wait...", bot.Config.IP);
            // houses
            var housesData = await bot.Connection.ReadBytesAsync((uint)OffsetHelper.VillagerHouseAddress, ExpectedHouseSize, token).ConfigureAwait(false);
            var houses = new VillagerHouse[10];
            for (int i = 0; i < 10; ++i)
                houses[i] = new VillagerHouse(housesData.Skip(i * VillagerHouse.SIZE).Take(VillagerHouse.SIZE).ToArray());

            // villager shells
            var villagers = new Villager2[10];
            for (int i = 0; i < 10; ++i)
            {
                var villagerBytes = await bot.Connection.ReadBytesAsync((uint)(OffsetHelper.VillagerAddress + (uint)(i * Villager2.SIZE)), 0x3, token).ConfigureAwait(false);
                villagers[i] = new Villager2(villagerBytes);

                if (villagers[i].Species != (byte)VillagerSpecies.non)
                    LogUtil.LogInfo($"Found villager: {GameInfo.Strings.GetVillager(villagers[i].InternalName)}", bot.Config.IP);
            }

            LogUtil.LogInfo("Villager data loaded...", bot.Config.IP);
            return new VillagerHelper(bot, houses, villagers);
        }

        public async Task<bool> InjectVillager(VillagerRequest vr, CancellationToken token)
        {
            var vd = vr.Villager;
            var index = vr.Index;

            if (VillagerHouses == null || VillagerShells == null || Connection == null || Bot == null)
                return false;

            VillagerHouse? house = VillagerHouses.Find(x => x.NPC1 == (sbyte)index);

            if (house == null)
            {
                LogUtil.LogInfo($"House {index} requested but does not exist. Injecting to first available house", Bot.Config.IP);
                for (int i = 0; i < VillagerHouses.Count; ++i)
                {
                    if (VillagerHouses[i].HouseStatus == 0)
                    {
                        house = VillagerHouses[i];
                        break;
                    }
                }
            }

            if (house == null)
            {
                LogUtil.LogInfo($"No house found to inject to.", Bot.Config.IP);
                return false;
            }

            var houseIndex = VillagerHouses.IndexOf(house);

            var villagerToInject = (byte[])vd.Villager.Clone();
            var villagerAsVillager = new Villager2(villagerToInject);

            LogUtil.LogInfo($"Beginning injection for: {GameInfo.Strings.GetVillager(villagerAsVillager.InternalName)}", Bot.Config.IP);

            // update moving out state
            villagerAsVillager.MoveType = 0;
            villagerAsVillager.MovingOut = true;
            ushort[] flags = villagerAsVillager.GetEventFlagsSave();
            flags[05] = 1; // flag 5 = MoveInCompletion
            flags[09] = 0; // flag 9 = AbandonedHouse
            flags[24] = 0; // flag 24 = ForceMoveOut
            villagerAsVillager.SetEventFlagsSave(flags);

            var houseToInject = (byte[])vd.House.Clone();
            var houseAsHouse = new VillagerHouse(houseToInject)
            {
                HouseStatus = house.HouseStatus == 0 ? 2 : house.HouseStatus,
                NPC1 = house.NPC1 == -1 ? (sbyte)index : house.NPC1,
                NPC2 = house.NPC2
            };
            
            // inject villager
            await Connection.WriteBytesAsync(villagerAsVillager.Data, (uint)OffsetHelper.VillagerAddress + ((uint)index * Villager2.SIZE), token).ConfigureAwait(false);
            await Connection.WriteBytesAsync(villagerAsVillager.Data, (uint)OffsetHelper.VillagerAddress + ((uint)index * Villager2.SIZE) + (uint)OffsetHelper.BackupSaveDiff, token).ConfigureAwait(false);

            // inject house
            await Connection.WriteBytesAsync(houseAsHouse.Data, (uint)OffsetHelper.VillagerHouseAddress + ((uint)houseIndex * VillagerHouse.SIZE), token).ConfigureAwait(false);
            await Connection.WriteBytesAsync(houseAsHouse.Data, (uint)OffsetHelper.VillagerHouseAddress + ((uint)houseIndex * VillagerHouse.SIZE) + (uint)OffsetHelper.BackupSaveDiff, token).ConfigureAwait(false);

            LogUtil.LogInfo($"Villager injection complete.", Bot.Config.IP);
            vr.OnFinish?.Invoke(true);
            return true;
        }

    }
}
