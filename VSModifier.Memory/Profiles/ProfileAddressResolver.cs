using VSModifier.Memory.ProcessMemory;
using VSModifier.Memory.Scanning;

namespace VSModifier.Memory.Profiles;

public static class ProfileAddressResolver
{
    public static nint Resolve(ProcessMemorySession memory, AddressDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(definition);
        ProcessModuleInfo module = memory.GetModuleInfo(definition.Module);
        return ResolveFromModule(memory, module.BaseAddress, module.Size, definition);
    }

    public static nint ResolveFromModule(
        IMemoryAccessor memory,
        nint moduleBase,
        int moduleSize,
        AddressDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(definition);
        nint rootAddress;
        if (string.IsNullOrWhiteSpace(definition.Aob))
        {
            rootAddress = moduleBase + checked((nint)definition.BaseOffset);
        }
        else
        {
            IReadOnlyList<nint> matches = AobScanner.Scan(
                memory,
                moduleBase,
                moduleSize,
                AobPattern.Parse(definition.Aob));
            if (matches.Count != 1)
            {
                throw new InvalidDataException(
                    $"模組 {definition.Module} 的 AOB 預期唯一匹配，實際找到 {matches.Count} 個。");
            }

            nint match = matches[0] + definition.AobOffset;
            if (definition.RipRelativeOffset is int relativeOffset)
            {
                nint displacementAddress = match + relativeOffset;
                int displacement = memory.Read<int>(displacementAddress);
                rootAddress = displacementAddress + sizeof(int) + displacement;
            }
            else
            {
                rootAddress = match;
            }
        }

        return PointerChain.Resolve(memory, rootAddress, definition.PointerOffsets);
    }
}
