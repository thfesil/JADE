using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Jade.Native;

namespace Jade.Core.Storage
{
    internal sealed class ManagedPortableBackend : IJadeStorageBackend
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("JADEMP01");
        private const uint FormatVersion = 1;

        private readonly object _lock = new object();
        private readonly string _dbPath;
        private readonly List<JadeNativeObject> _records = new List<JadeNativeObject>();
        private uint _nextDnt = 1;
        private uint _currentRid = 1000;
        private uint _maxRid = 1000000000;
        private uint _blockSize = 500;
        private bool _simulateExhaustion;
        private bool _disposed;

        private ManagedPortableBackend(string dbPath)
        {
            _dbPath = dbPath;
        }

        public static ManagedPortableBackend OpenOrCreate(string baseDirectory, string databaseName)
        {
            Directory.CreateDirectory(baseDirectory);
            string dbPath = Path.Combine(baseDirectory, databaseName);
            var backend = new ManagedPortableBackend(dbPath);

            if (File.Exists(dbPath))
            {
                backend.Load();
            }
            else
            {
                backend.Flush();
            }

            return backend;
        }

        public int AllocateRid(out uint rid)
        {
            lock (_lock)
            {
                if (_simulateExhaustion || _currentRid >= _maxRid)
                {
                    rid = 0;
                    return JadeConstants.ErrRidExhausted;
                }

                uint allocated = _currentRid;
                _currentRid++;
                Flush();
                rid = allocated;
                return JadeConstants.Ok;
            }
        }

        public int AddObject(ref JadeNativeObject obj, out uint dnt)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(obj.SamAccountName))
                {
                    foreach (var existing in _records)
                    {
                        if (string.Equals(existing.SamAccountName, obj.SamAccountName, StringComparison.OrdinalIgnoreCase))
                        {
                            dnt = 0;
                            return JadeConstants.ErrDuplicateSam;
                        }
                    }
                }

                JadeNativeObject rec = CloneNative(in obj);
                rec.Dnt = _nextDnt++;

                if (rec.ObjectGuid.All(b => b == 0))
                {
                    rec.ObjectGuid = Guid.NewGuid().ToByteArray();
                }

                long now = DateTime.UtcNow.ToFileTimeUtc();
                if (rec.WhenCreated == 0) rec.WhenCreated = now;
                if (rec.WhenChanged == 0) rec.WhenChanged = now;

                _records.Add(rec);
                Flush();

                dnt = rec.Dnt;
                return JadeConstants.Ok;
            }
        }

        public int FindBySam(string sam, out JadeNativeObject obj)
        {
            lock (_lock)
            {
                foreach (var rec in _records)
                {
                    if (string.Equals(rec.SamAccountName, sam, StringComparison.OrdinalIgnoreCase))
                    {
                        obj = CloneNative(in rec);
                        return JadeConstants.Ok;
                    }
                }

                obj = JadeNativeObject.CreateDefault();
                return JadeConstants.ErrRecordNotFound;
            }
        }

        public int FindByDnt(uint dnt, out JadeNativeObject obj)
        {
            lock (_lock)
            {
                int idx = FindIndexByDnt(dnt);
                if (idx < 0)
                {
                    obj = JadeNativeObject.CreateDefault();
                    return JadeConstants.ErrRecordNotFound;
                }

                JadeNativeObject rec = _records[idx];
                obj = CloneNative(in rec);
                return JadeConstants.Ok;
            }
        }

        public int ModifyUac(uint dnt, uint newUac)
        {
            lock (_lock)
            {
                int idx = FindIndexByDnt(dnt);
                if (idx < 0) return JadeConstants.ErrRecordNotFound;

                JadeNativeObject rec = _records[idx];
                rec.UserAccountControl = newUac;
                rec.WhenChanged = DateTime.UtcNow.ToFileTimeUtc();
                _records[idx] = rec;
                Flush();
                return JadeConstants.Ok;
            }
        }

        public int ModifyNcdnt(uint dnt, uint newNcdnt)
        {
            lock (_lock)
            {
                int idx = FindIndexByDnt(dnt);
                if (idx < 0) return JadeConstants.ErrRecordNotFound;

                JadeNativeObject rec = _records[idx];
                rec.Ncdnt = newNcdnt;
                rec.WhenChanged = DateTime.UtcNow.ToFileTimeUtc();
                _records[idx] = rec;
                Flush();
                return JadeConstants.Ok;
            }
        }

        public int SetPassword(uint dnt, byte[] pwd, uint pwdLen)
        {
            lock (_lock)
            {
                int idx = FindIndexByDnt(dnt);
                if (idx < 0) return JadeConstants.ErrRecordNotFound;

                int copyLen = (int)Math.Min(pwdLen, 512u);
                JadeNativeObject rec = _records[idx];
                Array.Clear(rec.UnicodePwd, 0, rec.UnicodePwd.Length);
                if (copyLen > 0)
                {
                    Array.Copy(pwd, 0, rec.UnicodePwd, 0, copyLen);
                }
                rec.UnicodePwdLen = (uint)copyLen;
                rec.WhenChanged = DateTime.UtcNow.ToFileTimeUtc();
                _records[idx] = rec;
                Flush();
                return JadeConstants.Ok;
            }
        }

        public int SetRidExhaustionSimulation(bool active)
        {
            lock (_lock)
            {
                _simulateExhaustion = active;
                Flush();
                return JadeConstants.Ok;
            }
        }

        public int DeleteObject(uint dnt, bool tombstone)
        {
            lock (_lock)
            {
                int idx = FindIndexByDnt(dnt);
                if (idx < 0) return JadeConstants.ErrRecordNotFound;

                if (tombstone)
                {
                    JadeNativeObject rec = _records[idx];
                    rec.IsDeleted = true;
                    rec.SamAccountName = $"{rec.SamAccountName}\nDEL:{dnt}";
                    rec.WhenChanged = DateTime.UtcNow.ToFileTimeUtc();
                    _records[idx] = rec;
                }
                else
                {
                    _records.RemoveAt(idx);
                }

                Flush();
                return JadeConstants.Ok;
            }
        }

        public int CountObjects(out uint count)
        {
            lock (_lock)
            {
                count = (uint)_records.Count;
                return JadeConstants.Ok;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                lock (_lock)
                {
                    Flush();
                }
                _disposed = true;
            }
        }

        private int FindIndexByDnt(uint dnt)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].Dnt == dnt) return i;
            }
            return -1;
        }

        private static JadeNativeObject CloneNative(in JadeNativeObject src)
        {
            JadeNativeObject copy = JadeNativeObject.CreateDefault();
            copy.Dnt = src.Dnt;
            copy.Pdnt = src.Pdnt;
            copy.Ncdnt = src.Ncdnt;
            copy.ObjectClass = src.ObjectClass;
            copy.SamAccountName = src.SamAccountName ?? string.Empty;
            copy.UserPrincipalName = src.UserPrincipalName ?? string.Empty;
            copy.UserAccountControl = src.UserAccountControl;
            copy.UnicodePwdLen = src.UnicodePwdLen;
            copy.ObjectSidLen = src.ObjectSidLen;
            copy.IsDeleted = src.IsDeleted;
            copy.WhenCreated = src.WhenCreated;
            copy.WhenChanged = src.WhenChanged;

            if (src.UnicodePwd != null)
            {
                Array.Copy(src.UnicodePwd, copy.UnicodePwd, Math.Min(src.UnicodePwd.Length, copy.UnicodePwd.Length));
            }
            if (src.ObjectGuid != null)
            {
                Array.Copy(src.ObjectGuid, copy.ObjectGuid, Math.Min(src.ObjectGuid.Length, copy.ObjectGuid.Length));
            }
            if (src.ObjectSid != null)
            {
                Array.Copy(src.ObjectSid, copy.ObjectSid, Math.Min(src.ObjectSid.Length, copy.ObjectSid.Length));
            }

            return copy;
        }

        private void Flush()
        {
            string tmpPath = _dbPath + ".tmp";
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8))
            {
                bw.Write(Magic);
                bw.Write(FormatVersion);
                bw.Write(_nextDnt);
                bw.Write(_currentRid);
                bw.Write(_maxRid);
                bw.Write(_blockSize);
                bw.Write(_simulateExhaustion);
                bw.Write((uint)_records.Count);

                foreach (var rec in _records)
                {
                    bw.Write(rec.Dnt);
                    bw.Write(rec.Pdnt);
                    bw.Write(rec.Ncdnt);
                    bw.Write(rec.ObjectClass);
                    bw.Write(rec.SamAccountName ?? string.Empty);
                    bw.Write(rec.UserPrincipalName ?? string.Empty);
                    bw.Write(rec.UserAccountControl);
                    bw.Write(rec.UnicodePwdLen);
                    bw.Write(rec.UnicodePwd, 0, 512);
                    bw.Write(rec.ObjectGuid, 0, 16);
                    bw.Write(rec.ObjectSidLen);
                    bw.Write(rec.ObjectSid, 0, 68);
                    bw.Write(rec.IsDeleted);
                    bw.Write(rec.WhenCreated);
                    bw.Write(rec.WhenChanged);
                }
                bw.Flush();
                fs.Flush(true);
            }

            File.Move(tmpPath, _dbPath, overwrite: true);
        }

        private void Load()
        {
            using var fs = new FileStream(_dbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs, Encoding.UTF8);

            byte[] fileMagic = br.ReadBytes(Magic.Length);
            if (fileMagic.Length != Magic.Length || !fileMagic.SequenceEqual(Magic))
            {
                throw new JadeException(JadeConstants.ErrDbCreateFailed, $"Formato de banco invalido em {_dbPath}");
            }

            uint version = br.ReadUInt32();
            if (version != FormatVersion)
            {
                throw new JadeException(JadeConstants.ErrDbCreateFailed, $"Versao de banco nao suportada: {version}");
            }

            _nextDnt = br.ReadUInt32();
            _currentRid = br.ReadUInt32();
            _maxRid = br.ReadUInt32();
            _blockSize = br.ReadUInt32();
            _simulateExhaustion = br.ReadBoolean();
            uint count = br.ReadUInt32();

            _records.Clear();
            for (uint i = 0; i < count; i++)
            {
                JadeNativeObject rec = JadeNativeObject.CreateDefault();
                rec.Dnt = br.ReadUInt32();
                rec.Pdnt = br.ReadUInt32();
                rec.Ncdnt = br.ReadUInt32();
                rec.ObjectClass = br.ReadUInt32();
                rec.SamAccountName = br.ReadString();
                rec.UserPrincipalName = br.ReadString();
                rec.UserAccountControl = br.ReadUInt32();
                rec.UnicodePwdLen = br.ReadUInt32();
                byte[] pwd = br.ReadBytes(512);
                Array.Copy(pwd, rec.UnicodePwd, pwd.Length);
                byte[] guid = br.ReadBytes(16);
                Array.Copy(guid, rec.ObjectGuid, guid.Length);
                rec.ObjectSidLen = br.ReadUInt32();
                byte[] sid = br.ReadBytes(68);
                Array.Copy(sid, rec.ObjectSid, sid.Length);
                rec.IsDeleted = br.ReadBoolean();
                rec.WhenCreated = br.ReadInt64();
                rec.WhenChanged = br.ReadInt64();
                _records.Add(rec);
            }
        }
    }
}
