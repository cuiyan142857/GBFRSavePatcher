// src/GBFRSave.Core/Patching/SavePatchEngine.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using Syroot.BinaryData;
using FlatSharp;
using GBFRDataTools.SaveData;
using GBFRDataTools.Hashing;
using GBFRDataTools.FlatBuffers;
using GBFRSave.Core.Models;

namespace GBFRSave.Core.Patching
{
    /// <summary>
    /// Logic-only engine for GBFR save reading, ticket calculation,
    /// factor/wrightstone clearing, voucher→transmarvel conversion, and writing.
    /// No UI/Console code. Throw exceptions on fatal errors.
    /// </summary>
    public sealed class SavePatchEngine
    {
        private const int FOOTER_BYTES = 0x14;
        private const int HASH_BYTES = 10 * 8;

        private const double DEFAULT_VOUCHER_TO_TRANSMARVEL_POINT_RATE = 1.33;

        private struct RawHeader
        {
            public int mainVersion;
            public ulong steamId;
            public int reserved;
            public int subVersion;
            public long offset1;
            public long slotDataOffset;
            public long size1;
            public long slotDataSize;
        }

        public object Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is empty.");
            if (!File.Exists(path)) throw new FileNotFoundException("Save file not found.", path);
            return SaveGameFile.FromFile(path);
        }

        public (int sigilCount, int removedSigilCount,
        int wrightstoneCount, int removedWrightstoneCount,
        long oldTickets, int sigilTickets, int wrightstoneTickets, long newTickets,
        long oldTrans, long newTrans) ComputeTickets(object saveObj)
        {
            var baseDir = AppContext.BaseDirectory;
            var sigilCsvPath = Path.Combine(baseDir, "Data", "sigil_id_with_ticket.csv");
            var wrightCsvPath = Path.Combine(baseDir, "Data", "wrightstone_with_ticket.csv");

            var save = (SaveGameFile)saveObj;

            var intIdx = save.SlotData.IntTable?.ToDictionary(e => ((uint)e.IDType, (int)e.UnitID));
            var uintIdx = save.SlotData.UIntTable?.ToDictionary(e => ((uint)e.IDType, (int)e.UnitID));
            var boolIdx = save.SlotData.BoolTable?.ToDictionary(e => ((uint)e.IDType, (int)e.UnitID));

            // Tickets
            var readVouchersCfg = new ReadConfig { IdType = 1802, UnitId = 145, TableType = TableType.Int };
            long oldTickets = ReadValueFast(readVouchersCfg, intIdx, uintIdx);

            var sigils = GetSigils(intIdx!, uintIdx!);
            sigils = CalculateSigilTickets(sigils, sigilCsvPath);
            sigils = SigilFilter(sigils);

            int sigilCount = sigils.Count();
            int removedSigilCount = sigils.Count(s => s.Keep == 0);
            int sigilTickets = sigils.Where(s => s.Keep == 0).Sum(s => s.TicketCount);

            var wrightstones = GetWrightstones(intIdx!, uintIdx!, boolIdx!);
            wrightstones = CalculateWrightstoneTickets(wrightstones, wrightCsvPath);
            wrightstones = WrightstoneFilter(wrightstones);

            int wrightstoneCount = wrightstones.Count();
            int removedWrightstoneCount = wrightstones.Count(w => w.Keep == 0);
            int wrightstoneTickets = wrightstones.Where(w => w.Keep == 0).Sum(w => w.TicketCount);
            long newTickets = oldTickets + sigilTickets + wrightstoneTickets;

            // Transmarvel
            var readTransCfg = new ReadConfig { IdType = 1105, UnitId = 0, TableType = TableType.Int };

            long oldTrans = ReadValueFast(readTransCfg, intIdx, uintIdx);
            long newTrans = checked((int)Math.Ceiling(oldTrans + newTickets * DEFAULT_VOUCHER_TO_TRANSMARVEL_POINT_RATE));

            return (sigilCount, removedSigilCount,
                    wrightstoneCount, removedWrightstoneCount,
                    oldTickets, sigilTickets, wrightstoneTickets, newTickets,
                    oldTrans, newTrans);
        }

        public (string outputPath, bool hashStatus) ApplyPatch(string inputPath)
        {
            var baseDir = AppContext.BaseDirectory;
            var sigilCsvPath = Path.Combine(baseDir, "Data", "sigil_id_with_ticket.csv");
            var wrightCsvPath = Path.Combine(baseDir, "Data", "wrightstone_with_ticket.csv");
            PatchOptions options = new PatchOptions();

            var inputPath_dir = Path.GetDirectoryName(inputPath) ?? "";
            var inputPath_name = Path.GetFileNameWithoutExtension(inputPath); // e.g. "SaveData1"
            var inputPath_ext = Path.GetExtension(inputPath);                // e.g. ".dat"
            var outputPath = inputPath;
            var tmpPath = inputPath_dir.Length > 0
                ? $"{inputPath_dir}/{inputPath_name}_patcher_tmp{inputPath_ext}"
                : $"{inputPath_name}_patcher_tmp{inputPath_ext}";
            // Console.WriteLine($"SaveFilePath:{outputPath}");
            MoveFile(inputPath, tmpPath);

            var hdr = ReadHeader(tmpPath);
            var save = SaveGameFile.FromFile(tmpPath);

            var intIdx = save.SlotData.IntTable?.ToDictionary(e => ((uint)e.IDType, (int)e.UnitID));
            var uintIdx = save.SlotData.UIntTable?.ToDictionary(e => ((uint)e.IDType, (int)e.UnitID));
            var boolIdx = save.SlotData.BoolTable?.ToDictionary(e => ((uint)e.IDType, (int)e.UnitID));

            // 1) Compute tickets (same as ComputeTickets)
            var sigils = GetSigils(intIdx!, uintIdx!);
            sigils = CalculateSigilTickets(sigils, sigilCsvPath);
            sigils = SigilFilter(sigils);
            int sigilTickets = sigils.Where(s => s.Keep == 0).Sum(s => s.TicketCount);

            var wrightstones = GetWrightstones(intIdx!, uintIdx!, boolIdx!);
            wrightstones = CalculateWrightstoneTickets(wrightstones, wrightCsvPath);
            wrightstones = WrightstoneFilter(wrightstones);
            int wrightTickets = wrightstones.Where(w => w.Keep == 0).Sum(w => w.TicketCount);

            // vouchers:          IdType=1802, UnitId=145, Int
            // transmarvel points:IdType=1105, UnitId=0, Int
            var readVouchersCfg = new ReadConfig { IdType = 1802, UnitId = 145, TableType = TableType.Int };
            var readTransCfg = new ReadConfig { IdType = 1105, UnitId = 0, TableType = TableType.Int };

            long oldVouchers = ReadValueFast(readVouchersCfg, intIdx, uintIdx);
            long added = sigilTickets + wrightTickets;
            long newVouchers = oldVouchers + added;

            if (intIdx?.TryGetValue(((uint)readVouchersCfg.IdType, readVouchersCfg.UnitId), out var voucherUnit) == true)
            {
                voucherUnit.ValueData[0] = checked((int)newVouchers);
            }

            if (options.ClearAllFactors)
            {
                foreach (var s in sigils)
                {
                    if (s.Keep == 1) continue;
                    if (intIdx?.TryGetValue((2704u, s.UnitId), out var u1) == true) u1.ValueData[0] = 0;
                    if (uintIdx?.TryGetValue((2702u, s.UnitId), out var u2) == true) u2.ValueData[0] = 0;
                    if (uintIdx?.TryGetValue((2703u, s.UnitId), out var u3) == true) u3.ValueData[0] = 2289754288;
                    if (uintIdx?.TryGetValue((2706u, s.UnitId), out var u4) == true) u4.ValueData[0] = 2289754288;
                    if (uintIdx?.TryGetValue((2707u, s.UnitId), out var u5) == true) u5.ValueData[0] = 0;
                }
            }

            if (options.ClearAllWrightstones)
            {
                foreach (var w in wrightstones)
                {
                    if (w.Keep == 1) continue;
                    if (uintIdx?.TryGetValue((2102u, w.WrightstoneUnitId), out var u1) == true) u1.ValueData[0] = 2289754288;
                    if (uintIdx?.TryGetValue((2103u, w.WrightstoneUnitId), out var u2) == true) u2.ValueData[0] = 0;
                    if (boolIdx?.TryGetValue((2104u, w.WrightstoneUnitId), out var u3) == true) u3.ValueData[0] = false;
                    if (uintIdx?.TryGetValue((2105u, w.WrightstoneUnitId), out var u4) == true) u4.ValueData[0] = 0;
                }
            }

            // Serialize SlotData + rewrite the file with valid hashes
            var slotBuf = SerializeSlotData(save, tmpPath, hdr, out int newSlotSize);
            WritePatchedFile(tmpPath, outputPath, hdr, slotBuf, newSlotSize);

            bool hashStatus = VerifyHashes(outputPath);
            File.Delete(tmpPath);
            return (outputPath, hashStatus);
        }

        public (string outputPath, bool hashStatus) ConvertTicketsToTransmarvelPoints(string inputPath)
        {
            var inputPath_dir = Path.GetDirectoryName(inputPath) ?? "";
            var inputPath_name = Path.GetFileNameWithoutExtension(inputPath); // e.g. "SaveData1"
            var inputPath_ext = Path.GetExtension(inputPath);                // e.g. ".dat"
            var outputPath = inputPath;
            var tmpPath = inputPath_dir.Length > 0
                ? $"{inputPath_dir}/{inputPath_name}_patcher_tmp{inputPath_ext}"
                : $"{inputPath_name}_patcher_tmp{inputPath_ext}";
            // Console.WriteLine($"SaveFilePath:{outputPath}");
            MoveFile(inputPath, tmpPath);

            var hdr = ReadHeader(tmpPath);
            var save = SaveGameFile.FromFile(tmpPath);

            var intIdx = save.SlotData.IntTable?.ToDictionary(e => ((uint)e.IDType, (int)e.UnitID));
            var uintIdx = save.SlotData.UIntTable?.ToDictionary(e => ((uint)e.IDType, (int)e.UnitID));
            var boolIdx = save.SlotData.BoolTable?.ToDictionary(e => ((uint)e.IDType, (int)e.UnitID));

            // vouchers:          IdType=1802, UnitId=145, Int
            // transmarvel points:IdType=1105, UnitId=0, Int
            var readVouchersCfg = new ReadConfig { IdType = 1802, UnitId = 145, TableType = TableType.Int };
            var readTransCfg = new ReadConfig { IdType = 1105, UnitId = 0, TableType = TableType.Int };

            long oldVouchers = ReadValueFast(readVouchersCfg, intIdx, uintIdx);

            if (intIdx?.TryGetValue(((uint)readVouchersCfg.IdType, readVouchersCfg.UnitId), out var v2) == true)
            {
                v2.ValueData[0] = 0;
            }

            long oldTrans = ReadValueFast(readTransCfg, intIdx, uintIdx);

            long newTrans = checked((int)Math.Ceiling(oldTrans + oldVouchers * DEFAULT_VOUCHER_TO_TRANSMARVEL_POINT_RATE));

            if (intIdx?.TryGetValue(((uint)readTransCfg.IdType, readTransCfg.UnitId), out var transUnit) == true)
            {
                transUnit.ValueData[0] = (int)newTrans;
            }

            // Serialize SlotData + rewrite the file with valid hashes
            var slotBuf = SerializeSlotData(save, tmpPath, hdr, out int newSlotSize);
            WritePatchedFile(tmpPath, outputPath, hdr, slotBuf, newSlotSize);

            bool hashStatus = VerifyHashes(outputPath);
            File.Delete(tmpPath);
            return (outputPath, hashStatus);
        }
        public string BackupFile(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("inputPath is empty.", nameof(inputPath));
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found.", inputPath);
            var inputPath_dir = Path.GetDirectoryName(inputPath) ?? "";
            var inputPath_name = Path.GetFileNameWithoutExtension(inputPath); // e.g. "SaveData1"
            var inputPath_ext = Path.GetExtension(inputPath);                // e.g. ".dat"
            var backupPath = inputPath_dir.Length > 0
                ? $"{inputPath_dir}/{inputPath_name}_patcher_backup{inputPath_ext}"
                : $"{inputPath_name}_patcher_backup{inputPath_ext}";
            // Console.WriteLine($"SaveFilePath:{outputPath}");
            File.Copy(inputPath, backupPath, true);
            return backupPath;
        }

        public bool VerifyHashes(string path)
        {
            if (!ReadHeader(path, out var header, out var fileLen))
                throw new InvalidDataException("Header read failed.");

            if (!ValidateHeaderRanges(header, fileLen))
                throw new InvalidDataException("Header ranges invalid.");

            // Parse with FlatSharp
            SaveGameFile save = SaveGameFile.FromFile(path);

            // Read SlotData raw buffer and recompute hashes
            byte[] slotBuf = ReadSlotDataBuffer(path, header.slotDataOffset, header.slotDataSize, out uint hashesOffset);

            UIntSaveDataUnit seedUnit = save.SlotData.UIntTable!.First(e => e.IDType == (uint)UnitType.SAVEDATA_HASHSEED);
            int activeIdx = (int)(seedUnit.ValueData[0] % SaveGameFile.HashSectionInfos.Count);

            bool allOk = true;
            for (int i = 0; i < SaveGameFile.HashSectionInfos.Count; i++)
            {
                var sect = SaveGameFile.HashSectionInfos[i];
                int length = (int)hashesOffset - (sect.StartOffset + sect.SubSize);
                if (length < 0 || sect.StartOffset + sect.SubSize > hashesOffset)
                {
                    allOk = false;
                    continue;
                }

                var span = slotBuf.AsSpan(sect.StartOffset, length);
                ulong calc = XXHash64.Hash(span, SaveGameFile.XXHASH64_SAVE_SEED);
                ulong stored = save.Hashes[i];
                if (calc != stored) allOk = false;
            }

            if (!allOk) throw new InvalidDataException("Detected hash mismatch after patch.");

            return allOk;
        }

        // ---- Internal implementation (ported from your code) ----------------

        private static RawHeader ReadHeader(string file)
        {
            using var fs = File.OpenRead(file);
            using var bs = new BinaryStream(fs);

            return new RawHeader
            {
                mainVersion = bs.ReadInt32(),
                steamId = bs.ReadUInt64(),
                reserved = bs.ReadInt32(),
                subVersion = bs.ReadInt32(),
                offset1 = bs.ReadInt64(),
                slotDataOffset = bs.ReadInt64(),
                size1 = bs.ReadInt64(),
                slotDataSize = bs.ReadInt64()
            };
        }

        private static bool ReadHeader(string file, out RawHeader h, out long fileLen)
        {
            h = default;
            try
            {
                using var fs = File.OpenRead(file);
                using var bs = new BinaryStream(fs);

                h.mainVersion = bs.ReadInt32();
                h.steamId = bs.ReadUInt64();
                h.reserved = bs.ReadInt32();
                h.subVersion = bs.ReadInt32();
                h.offset1 = bs.ReadInt64();
                h.slotDataOffset = bs.ReadInt64();
                h.size1 = bs.ReadInt64();
                h.slotDataSize = bs.ReadInt64();

                fileLen = fs.Length;
                return true;
            }
            catch
            {
                fileLen = 0;
                return false;
            }
        }

        private static bool ValidateHeaderRanges(RawHeader h, long fileLen)
        {
            long end1 = h.offset1 + h.size1;
            long end2 = h.slotDataOffset + h.slotDataSize;

            if (h.offset1 < 0 || h.size1 < 0 || end1 > fileLen) return false;
            if (h.slotDataOffset < 0 || h.slotDataSize < 0 || end2 > fileLen) return false;
            if (h.offset1 >= h.slotDataOffset && end1 > h.slotDataOffset) return false;

            return true;
        }

        private static byte[] ReadSlotDataBuffer(string file, long slotOff, long slotSize, out uint hashesOffset)
        {
            using var fs = File.OpenRead(file);
            using var bs = new BinaryStream(fs);

            bs.Position = slotOff;
            byte[] slotBuf = bs.ReadBytes((int)slotSize);

            hashesOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                slotBuf.AsSpan(slotBuf.Length - FOOTER_BYTES));

            return slotBuf;
        }

        private static byte[] SerializeSlotData(SaveGameFile save, string inFile, RawHeader hdr, out int newSlotSize)
        {
            int maxSize = FlatBufferSerializer.Default.GetMaxSize(save.SlotData);
            byte[] temp = new byte[maxSize];
            int baseSize = FlatBufferSerializer.Default.Serialize(save.SlotData, temp);

            uint hashesOffset = (uint)baseSize;
            newSlotSize = baseSize + HASH_BYTES + FOOTER_BYTES;

            byte[] slotBuf = new byte[newSlotSize];
            Array.Copy(temp, slotBuf, baseSize);

            using var fsFooter = File.OpenRead(inFile);
            fsFooter.Position = hdr.slotDataOffset + hdr.slotDataSize - FOOTER_BYTES;
            fsFooter.Read(slotBuf, newSlotSize - FOOTER_BYTES, FOOTER_BYTES);
            BinaryPrimitives.WriteUInt32LittleEndian(slotBuf.AsSpan(newSlotSize - FOOTER_BYTES), hashesOffset);

            for (int i = 0; i < SaveGameFile.HashSectionInfos.Count; i++)
            {
                var sect = SaveGameFile.HashSectionInfos[i];
                int len = (int)hashesOffset - (sect.StartOffset + sect.SubSize);
                ulong h = XXHash64.Hash(slotBuf.AsSpan(sect.StartOffset, len), SaveGameFile.XXHASH64_SAVE_SEED);
                BinaryPrimitives.WriteUInt64LittleEndian(slotBuf.AsSpan((int)hashesOffset + i * 8), h);
            }

            return slotBuf;
        }

        private static void WritePatchedFile(string inFile, string outFile, RawHeader hdr, byte[] slotBuf, int newSlotSize)
        {
            using var src = File.OpenRead(inFile);
            using var dst = File.Create(outFile);

            CopyRegion(src, dst, 0, hdr.slotDataOffset);

            // Update slotDataSize @ 0x2C
            dst.Position = 0x2C;
            dst.Write(BitConverter.GetBytes((long)newSlotSize));

            dst.Position = hdr.slotDataOffset;
            dst.Write(slotBuf);
        }

        private static void CopyRegion(Stream src, Stream dst, long off, long size)
        {
            byte[] buf = new byte[65536];
            src.Position = off;
            long remaining = size;
            while (remaining > 0)
            {
                int r = src.Read(buf, 0, (int)Math.Min(buf.Length, remaining));
                if (r <= 0) break;
                dst.Write(buf, 0, r);
                remaining -= r;
            }
        }


        private enum TableType { Int, UInt }

        private sealed class ReadConfig
        {
            public int IdType { get; set; }
            public int UnitId { get; set; }
            public TableType TableType { get; set; }
        }

        private static long ReadValueFast(
            ReadConfig cfg,
            Dictionary<(uint idType, int unitId), IntSaveDataUnit>? intIdx,
            Dictionary<(uint idType, int unitId), UIntSaveDataUnit>? uintIdx)
        {
            return cfg.TableType switch
            {
                TableType.Int =>
                    intIdx != null && intIdx.TryGetValue(((uint)cfg.IdType, cfg.UnitId), out var i)
                        ? i.ValueData[0]
                        : 0,
                TableType.UInt =>
                    uintIdx != null && uintIdx.TryGetValue(((uint)cfg.IdType, cfg.UnitId), out var u)
                        ? u.ValueData[0]
                        : 0,
                _ => 0
            };
        }

        private sealed class Sigil
        {
            public int UnitId { get; set; }
            public int Level { get; set; }
            public uint Type { get; set; }
            public uint EquipStatus { get; set; }
            public uint LockStatus { get; set; }
            public int TicketCount { get; set; }
            public int Keep { get; set; }
        }

        private static List<Sigil> GetSigils(
            Dictionary<(uint idType, int unitId), IntSaveDataUnit> intIdx,
            Dictionary<(uint idType, int unitId), UIntSaveDataUnit> uintIdx)
        {
            const uint ID_PRESENT = 2702u; // owned/present (UInt)
            const uint ID_LEVEL = 2704u; // level (Int)
            const uint ID_TYPE = 2703u; // type hash decimal (UInt)
            const uint ID_FILTER_EQUIPPED = 2706u; // equipped or not (UInt), must == 2289754288
            const uint ID_FILTER_LOCKED = 2707u; // locked or not (UInt), must != 3

            var sigils = new List<Sigil>();

            for (int uid = 30000; uid <= 34999; uid++)
            {
                // Must be present
                if (!(uintIdx?.TryGetValue((ID_PRESENT, uid), out var presentUnit) ?? false) ||
                    presentUnit.ValueData is null || presentUnit.ValueData.Count == 0 ||
                    presentUnit.ValueData[0] == 0)
                {
                    continue;
                }

                // Level <= 11
                if (!(intIdx?.TryGetValue((ID_LEVEL, uid), out var levelUnit) ?? false) ||
                    levelUnit.ValueData is null || levelUnit.ValueData.Count == 0)
                {
                    continue;
                }
                int level = levelUnit.ValueData[0];
                // if (level > 11) continue;

                // Equipped must equal 2289754288
                if (!(uintIdx?.TryGetValue((ID_FILTER_EQUIPPED, uid), out var equipUnit) ?? false) ||
                    equipUnit.ValueData is null || equipUnit.ValueData.Count == 0)
                {
                    continue;
                }
                uint equipStatus = equipUnit.ValueData[0];
                // if (equipStatus != 2289754288) continue;

                // Locked must NOT equal 3
                if (!(uintIdx?.TryGetValue((ID_FILTER_LOCKED, uid), out var lockUnit) ?? false) ||
                    lockUnit.ValueData is null || lockUnit.ValueData.Count == 0)
                {
                    continue;
                }
                uint lockStatus = lockUnit.ValueData[0];
                // if (lockStatus == 3) continue;

                // Type (hash decimal)
                if (!(uintIdx?.TryGetValue((ID_TYPE, uid), out var typeUnit) ?? false) ||
                    typeUnit.ValueData is null || typeUnit.ValueData.Count == 0)
                {
                    continue;
                }
                uint type = typeUnit.ValueData[0];

                sigils.Add(new Sigil
                {
                    UnitId = uid,
                    Level = level,
                    EquipStatus = equipStatus,
                    LockStatus = lockStatus,
                    Type = type
                });
                // Console.WriteLine($"UnitId:{uid}, Level:{level}, EquipStatus:{equipStatus}, LockStatus:{lockStatus}, Type:{type}");
            }

            return sigils;
        }

        private static List<Sigil> CalculateSigilTickets(
            List<Sigil> sigils,
            string csvPath)
        {
            var ticketMap = LoadSigilTicketMap(csvPath);

            foreach (var s in sigils)
            {
                ticketMap.TryGetValue(s.Type, out int tickets);
                s.TicketCount = tickets;
            }

            return sigils;
        }

        private static List<Sigil> SigilFilter(List<Sigil> sigils)
        {
            int levelFilter = 11;
            uint lockStatusFilter = 3;
            uint equipStatusFilter = 2289754288;
            uint typeFilter;

            foreach (var s in sigils)
            {
                s.Keep = 1;
                if (s.Level > levelFilter) continue;
                if (s.LockStatus == lockStatusFilter) continue;
                if (s.EquipStatus != equipStatusFilter) continue;
                s.Keep = 0;
            }

            return sigils;
        }

        private static Dictionary<uint, int> LoadSigilTicketMap(string csvPath)
        {
            var map = new Dictionary<uint, int>();
            bool test = File.Exists(csvPath);
            if (!File.Exists(csvPath))
            {
                // No CSV → treat as all zeros
                return map;
            }


            foreach (var line in File.ReadLines(csvPath).Skip(1)) // skip header
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = line.Split(',');
                if (cols.Length < 5) continue;

                // cols[3] = Id HASH Decimal, cols[4] = Lottery Ticket Count
                if (!uint.TryParse(cols[3].Trim(), out var hashDecimal)) continue;
                if (!int.TryParse(cols[4].Trim(), out var ticketCount)) continue;

                map[hashDecimal] = ticketCount;
            }

            return map;
        }

        // ---- Tickets from Wrightstones -------------------------------------

        private sealed class Wrightstone
        {
            public int WrightstoneUnitId { get; set; }
            public int WrightstoneLevel1 { get; set; }
            public int WrightstoneLevel2 { get; set; }
            public int WrightstoneLevel3 { get; set; }
            public uint WrightstoneType1 { get; set; }
            public uint WrightstoneType2 { get; set; }
            public uint WrightstoneType3 { get; set; }
            public uint ExistStatus { get; set; }
            public bool LockStatus { get; set; }
            public int TicketCount { get; set; }
            public int Keep { get; set; }
        }

        private static List<Wrightstone> GetWrightstones(
            Dictionary<(uint idType, int unitId), IntSaveDataUnit> intIdx,
            Dictionary<(uint idType, int unitId), UIntSaveDataUnit> uintIdx,
            Dictionary<(uint idType, int unitId), BoolSaveDataUnit> boolIdx)
        {
            const uint ID_WRIGHTSTONE_TYPE = 2102u; // UInt
            const uint ID_WRIGHTSTONE_UNIT = 2103u; // UInt
            const uint ID_FILTER_STATE = 2105u; // UInt, must == 2
            const uint ID_FILTER_LOCKED = 2104u; // Bool, must == false (0)
            const uint ID_LEVELS = 1702u; // Int

            var list = new List<Wrightstone>();

            // 2103.unitId IS the WrightstoneUnitId
            //foreach (var (key, _) in uintIdx.Where(p => p.Key.idType == ID_WRIGHTSTONE_UNIT).OrderBy(p => p.Key.unitId))
            for (int uid = 50000; uid <= 54999; uid++)
            {
                if (!TryGetUInt(uintIdx, ID_WRIGHTSTONE_TYPE, uid, out var value) || value == 2289754288) continue;

                if (!TryGetUInt(uintIdx, ID_FILTER_STATE, uid, out var exist)) continue;
                uint existStatus = exist;

                if (!TryGetBool(boolIdx, ID_FILTER_LOCKED, uid, out var locked)) continue;
                bool lockStatus = locked;

                // Levels @ 1702: 14xxxxx00, 14xxxxx01, 14xxxxx02
                int baseLevelUid = 140_000_000 + (uid - 50000) * 100 + 0;
                int lvl1 = GetInt(intIdx, ID_LEVELS, baseLevelUid + 0, 0);
                int lvl2 = GetInt(intIdx, ID_LEVELS, baseLevelUid + 1, 0);
                int lvl3 = GetInt(intIdx, ID_LEVELS, baseLevelUid + 2, 0);

                var w = new Wrightstone
                {
                    WrightstoneUnitId = uid,
                    WrightstoneLevel1 = lvl1,
                    WrightstoneLevel2 = lvl2,
                    WrightstoneLevel3 = lvl3,
                    WrightstoneType1 = 0,
                    WrightstoneType2 = 0,
                    WrightstoneType3 = 0,
                    ExistStatus = existStatus,
                    LockStatus = lockStatus
                };

                list.Add(w);
            }

            return list;

            static int GetInt(
                Dictionary<(uint, int), IntSaveDataUnit> dict,
                uint idType, int unitId, int def = 0)
                => dict.TryGetValue((idType, unitId), out var u) && u?.ValueData?.Count > 0
                   ? u.ValueData[0] : def;

            static bool TryGetUInt(
                Dictionary<(uint, int), UIntSaveDataUnit> dict,
                uint idType, int unitId, out uint value)
            {
                value = 0;
                if (dict.TryGetValue((idType, unitId), out var u) &&
                    u?.ValueData?.Count > 0)
                {
                    value = u.ValueData[0];
                    return true;
                }
                return false;
            }

            static bool TryGetBool(
                Dictionary<(uint, int), BoolSaveDataUnit> dict,
                uint idType, int unitId, out bool value)
            {
                value = false;
                if (dict.TryGetValue((idType, unitId), out var u) &&
                    u?.ValueData?.Count > 0)
                {
                    value = u.ValueData[0];
                    return true;
                }
                return false;
            }
        }

        private static List<Wrightstone> CalculateWrightstoneTickets(
            List<Wrightstone> wrightstones,
            string csvPath)
        {
            Func<Wrightstone, int> ticketRule = BuildWrightstoneTicketRuleFromCsv(csvPath);

            foreach (var w in wrightstones)
            {
                w.TicketCount = ticketRule?.Invoke(w) ?? 0;
            }

            return wrightstones;
        }

        private static Func<Wrightstone, int> BuildWrightstoneTicketRuleFromCsv(string csvPath)
        {
            if (!File.Exists(csvPath))
            {
                // No CSV → treat as 0 tickets for all combinations.
                return _ => 0;
            }

            var lines = File.ReadAllLines(csvPath).Skip(1);
            var ticketMap = new Dictionary<(int, int, int), int>();

            foreach (var line in lines)
            {
                var cols = line.Split(',', StringSplitOptions.TrimEntries);
                if (cols.Length < 4) continue;

                if (int.TryParse(cols[0], out int lvl1) &&
                    int.TryParse(cols[1], out int lvl2) &&
                    int.TryParse(cols[2], out int lvl3) &&
                    int.TryParse(cols[3], out int tickets))
                {
                    ticketMap[(lvl1, lvl2, lvl3)] = tickets;
                }
            }

            return w => ticketMap.TryGetValue(
                            (w.WrightstoneLevel1, w.WrightstoneLevel2, w.WrightstoneLevel3),
                            out int ticketCount)
                        ? ticketCount
                        : 0;
        }

        private static List<Wrightstone> WrightstoneFilter(List<Wrightstone> wrightstones)
        {
            int levelFilter = 20;
            bool lockStatusFilter = true;
            uint typeFilter;

            foreach (var w in wrightstones)
            {
                w.Keep = 1;
                if (w.WrightstoneLevel1 >= levelFilter) continue;
                if (w.LockStatus == lockStatusFilter) continue;
                w.Keep = 0;
            }

            return wrightstones;
        }

        public bool MoveFile(string inputPath, string outputPath, bool overwrite = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(inputPath))
                    throw new ArgumentException("inputPath is empty.", nameof(inputPath));
                if (!File.Exists(inputPath))
                    throw new FileNotFoundException("Input file not found.", inputPath);

                if (string.IsNullOrWhiteSpace(outputPath))
                    throw new ArgumentException("outputPath is empty.", nameof(outputPath));

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.Move(inputPath, outputPath, overwrite);  // removes the original
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Move file failed: {ex.Message}");
                return false;
            }
        }
    }
}
