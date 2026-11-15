using System;
using Jade.Native;

namespace Jade.Core.Storage
{
    internal interface IJadeStorageBackend : IDisposable
    {
        int AllocateRid(out uint rid);
        int AddObject(ref JadeNativeObject obj, out uint dnt);
        int FindBySam(string sam, out JadeNativeObject obj);
        int FindByDnt(uint dnt, out JadeNativeObject obj);
        int ModifyUac(uint dnt, uint newUac);
        int ModifyNcdnt(uint dnt, uint newNcdnt);
        int SetPassword(uint dnt, byte[] pwd, uint pwdLen);
        int SetRidExhaustionSimulation(bool active);
        int DeleteObject(uint dnt, bool tombstone);
        int CountObjects(out uint count);
    }
}
