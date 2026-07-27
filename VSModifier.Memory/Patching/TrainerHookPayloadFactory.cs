namespace VSModifier.Memory.Patching;

internal sealed record RemoteHookPayload(byte[] Code, IReadOnlyDictionary<string, int> DataOffsets);

internal static class TrainerHookPayloadFactory
{
    internal const string EnabledFlag = "enabled";
    internal const string ItemId = "itemId";

    internal static RemoteHookPayload CreateForcedLevelUpItem(
        nint caveAddress,
        nint targetAddress,
        ReadOnlySpan<byte> originalBytes)
    {
        X64CodeBuilder code = new();
        code.Emit(0x53, 0x51, 0x52);                         // push rbx, rcx, rdx
        code.Emit(0x80, 0x3D);                               // cmp byte ptr [rip+enabled], 0
        code.EmitRelative32(EnabledFlag);
        code.Emit(0x00);
        code.Emit(0x0F, 0x84);                               // je done
        code.EmitRelative32("done");
        code.Emit(0x48, 0x8B, 0x50, 0x10);                   // mov rdx, [rax+10h]
        code.Emit(0x83, 0x3D);                               // cmp dword ptr [rip+itemId], 0
        code.EmitRelative32(ItemId);
        code.Emit(0x00);
        code.Emit(0x0F, 0x84);                               // je done
        code.EmitRelative32("done");
        code.Emit(0xB9, 0x2C, 0x00, 0x00, 0x00);             // mov ecx, 2Ch
        code.MarkLabel("scan");
        code.Emit(0x83, 0xF9, 0x20);                         // cmp ecx, 20h
        code.Emit(0x0F, 0x84);                               // je done
        code.EmitRelative32("done");
        code.Emit(0x83, 0x3C, 0x0A, 0x00);                   // cmp dword ptr [rdx+rcx], 0
        code.Emit(0x0F, 0x85);                               // jne replace
        code.EmitRelative32("replace");
        code.Emit(0x83, 0xE9, 0x04);                         // sub ecx, 4
        code.Emit(0xE9);
        code.EmitRelative32("scan");
        code.MarkLabel("replace");
        code.Emit(0x8B, 0x1D);                               // mov ebx, [rip+itemId]
        code.EmitRelative32(ItemId);
        code.Emit(0x89, 0x1C, 0x0A);                         // mov [rdx+rcx], ebx
        code.MarkLabel("done");
        code.Emit(0x5A, 0x59, 0x5B);                         // pop rdx, rcx, rbx
        code.Emit(originalBytes.ToArray());
        code.Emit(0xE9);
        code.EmitRelative32(targetAddress + originalBytes.Length);
        code.MarkLabel(ItemId);
        code.EmitInt32(0);
        code.MarkLabel(EnabledFlag);
        code.Emit(0);

        return new RemoteHookPayload(
            code.Build(caveAddress),
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [ItemId] = code.GetLabelOffset(ItemId),
                [EnabledFlag] = code.GetLabelOffset(EnabledFlag)
            });
    }

    internal static RemoteHookPayload CreateDuplicateNext(
        nint caveAddress,
        nint targetAddress,
        ReadOnlySpan<byte> originalBytes)
    {
        X64CodeBuilder code = new();
        code.Emit(originalBytes.ToArray());
        code.Emit(0x80, 0x3D);                               // cmp byte ptr [rip+enabled], 0
        code.EmitRelative32(EnabledFlag);
        code.Emit(0x00);
        code.Emit(0x0F, 0x84);                               // je normal
        code.EmitRelative32("normal");
        code.Emit(0xC6, 0x05);                               // mov byte ptr [rip+enabled], 0
        code.EmitRelative32(EnabledFlag);
        code.Emit(0x00);
        code.Emit(0x48, 0x31, 0xC0);                         // xor rax, rax
        code.Emit(0xE9);                                     // skip the five-byte duplicate guard call
        code.EmitRelative32(targetAddress + originalBytes.Length + 5);
        code.MarkLabel("normal");
        code.Emit(0xE9);
        code.EmitRelative32(targetAddress + originalBytes.Length);
        code.MarkLabel(EnabledFlag);
        code.Emit(0);

        return new RemoteHookPayload(
            code.Build(caveAddress),
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [EnabledFlag] = code.GetLabelOffset(EnabledFlag)
            });
    }
}
