using VSModifier.Memory.ProcessMemory;

namespace VSModifier.Memory.Profiles;

public static class ProfileAddressResolver
{
    public static nint Resolve(ProcessMemorySession memory, AddressDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(definition);
        nint moduleBase = memory.GetModuleBaseAddress(definition.Module);
        return PointerChain.Resolve(memory, moduleBase + checked((nint)definition.BaseOffset), definition.PointerOffsets);
    }
}
